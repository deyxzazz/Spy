using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using AssetsTools.NET;
using AssetsTools.NET.Extra;
using ICSharpCode.Decompiler;
using ICSharpCode.Decompiler.CSharp;
using ICSharpCode.Decompiler.TypeSystem;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using AvaloniaEdit;
using Avalonia.Styling;


using DecompilerLang = ICSharpCode.Decompiler.CSharp.LanguageVersion;

namespace SpyTool2;

public record AssetItemRow(long PathId, string Name, string TypeName, string Details)
{

};
public class DnNode
{
    public string Title { get; set; } = "";
    public string DllPath { get; set; } = "";
    public string FullTypeName { get; set; } = "";
    public ITypeDefinition? TypeDef { get; set; }
    public IMember? Member { get; set; }
    public ObservableCollection<DnNode> Children { get; } = new();
}

public partial class MainWindow : Window
{
    private readonly List<(string FileItem, long PathId, string Name)> _modifiedAssets = new();
    private int _modCursor = -1;
    private Button? _bNextMod;
    private readonly Image _previewImg = new() { Stretch = Stretch.Uniform, MaxHeight = 320, Margin = new Thickness(0, 8) };
    private readonly TextBox _previewTxt = new() { IsReadOnly = true, FontFamily = new FontFamily("Consolas"), FontSize = 12, TextWrapping = TextWrapping.Wrap, AcceptsReturn = true, IsVisible = false };
    private readonly TextBlock _previewInfo = new() { Text = "Выберите ассет для предпросмотра", TextWrapping = TextWrapping.Wrap, FontSize = 12.5 };
    private System.Threading.CancellationTokenSource? _previewCts;
    private readonly List<string> _customDlls = new();

    private static string ResolveRealFile(string sel)
    {
        int idx = sel.IndexOf(" -> ", StringComparison.Ordinal);
        if (idx >= 0) return sel.Substring(0, idx);
        return sel.EndsWith(" [Все]") ? sel.Substring(0, sel.Length - 6) : sel;
    }



    private void MarkModified(string fileItem, long pid, string name)
    {
        if (!_modifiedAssets.Any(m => m.FileItem == fileItem && m.PathId == pid)) _modifiedAssets.Add((fileItem, pid, name));
        if (_bNextMod != null) _bNextMod.Content = $"★ Изменённые ({_modifiedAssets.Count})";
    }

    private void OnNextModifiedClick(object? s, RoutedEventArgs e)
    {
        if (_modifiedAssets.Count == 0) { StatusText.Text = "Изменённых ассетов пока нет."; return; }
        _modCursor = (_modCursor + 1) % _modifiedAssets.Count;
        var t = _modifiedAssets[_modCursor];
        if (!Equals(AssetFileBox.SelectedItem, t.FileItem)) AssetFileBox.SelectedItem = t.FileItem;
        var row = _allAssets.FirstOrDefault(x => x.PathId == t.PathId);
        if (row != null) { AssetsGrid.SelectedItem = row; AssetsGrid.ScrollIntoView(row, null); }
        StatusText.Text = $"Изменённый [{_modCursor + 1}/{_modifiedAssets.Count}]: {t.Name} ({t.FileItem})";
    }

    private string _editTargetType = "";
    private string? _editTargetMethod = null;
    private string _dataDir = "", _activeDll = "";
    private readonly Dictionary<string, CSharpDecompiler> _decompilers = new();
    private readonly AssetsManager _assetsManager = new();

    private readonly TextBox GamePathBox = new() { PlaceholderText = "Папка с игрой или .exe..." };
    private readonly TextBlock EngineInfoText = new() { Text = "Unity: не выбрано", VerticalAlignment = VerticalAlignment.Center };
    private readonly ComboBox LangVersionBox = new() { Width = 200, ItemsSource = new List<string> { "C# 4.0 (Unity 4/5)", "C# Latest" }, SelectedIndex = 0 };
    private readonly TextBox TreeSearchBox = new() { PlaceholderText = "Поиск класса / метода...", Margin = new Thickness(0, 0, 0, 6) };
    private readonly TreeView AssemblyTree = new();
    private readonly TextEditor CodeViewerBox = CodeEditorHelper.CreateCSharpEditor(isReadOnly: true);
    private readonly ComboBox AssetFileBox = new() { Width = 300, PlaceholderText = "Выберите .assets..." };
    private readonly DataGrid AssetsGrid = new() { AutoGenerateColumns = false, IsReadOnly = true };
    private readonly TextBox RipOutputBox = new() { PlaceholderText = @"Папка вывода (D:\RippedGame)" };
    private readonly TextBlock StatusText = new() { Text = "Готово. Укажите папку с игрой." };

    public MainWindow()
    {
        InitializeComponent();
        Title = "Spy";
        Width = 1380;
        Height = 850;
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DropEvent, (_, e) => {
            var items = e.DataTransfer.TryGetFiles()?.Select(x => x.Path.LocalPath).ToList();
            if (items == null || items.Count == 0) return;
            var dlls = items.Where(x => x.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)).ToList();
            if (dlls.Count > 0)
            {
                foreach (var d in dlls)
                {
                    if (!_customDlls.Contains(d, StringComparer.OrdinalIgnoreCase)) _customDlls.Add(d);
                    _decompilers.Remove(d);
                }
                RebuildTree(TreeSearchBox.Text ?? "");
            }
            else Scan(items.First());
        });

        var asm = typeof(MainWindow).Assembly;
        string? resName = asm.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith("classdata.tpk"));
        if (resName != null)
        {
            using var stream = asm.GetManifestResourceStream(resName)!;
            _assetsManager.LoadClassPackage(stream);
        }

        BuildUI();
        ApplyTheme(AppState.Current.ThemeIndex);
        if (!string.IsNullOrEmpty(AppState.Current.LastGamePath) && Directory.Exists(AppState.Current.LastGamePath))
            Scan(AppState.Current.LastGamePath);
    }

    private List<AssetItemRow> _allAssets = new();
    private readonly TextBox AssetSearchBox = new() { Watermark = "Поиск ассета по имени или типу...", Width = 260 };


    private void ApplyTheme(int idx)
    {
        AppState.Current.ThemeIndex = idx; AppState.Save();
        bool isLight = idx == 2 || (idx == 0 && Application.Current!.ActualThemeVariant == ThemeVariant.Light);
        Application.Current!.RequestedThemeVariant = isLight ? ThemeVariant.Light : ThemeVariant.Dark;

        var (winBg, codeBg, codeFg, accentFg) = idx switch
        {
            2 => ("#F3F4F6", "#FFFFFF", "#1E1E1E", "#111827"), // Светлая
            3 => ("#21222C", "#282A36", "#F8F8F2", "#BD93F9"), // Dracula
            4 => ("#242933", "#2E3440", "#D8DEE9", "#88C0D0"), // Nord
            5 => ("#1E1F1C", "#272822", "#F8F8F2", "#A6E22E"), // Monokai
            6 => ("#0D0221", "#120428", "#00F0FF", "#FF2A6D"), // Cyberpunk
            7 => ("#050505", "#0A0F0A", "#39FF14", "#CCFF00"), // Acid Neon (кислотный)
            _ => isLight
                ? ("#F3F4F6", "#FFFFFF", "#1E1E1E", "#111827")
                : ("#181818", "#1E1E1E", "#D4D4D4", "#569CD6") // Системная / Тёмная
        };

        Background = new SolidColorBrush(Color.Parse(winBg));
        Foreground = new SolidColorBrush(Color.Parse(codeFg));
        CodeViewerBox.Background = new SolidColorBrush(Color.Parse(codeBg));
        CodeViewerBox.Foreground = new SolidColorBrush(Color.Parse(codeFg));
        EngineInfoText.Foreground = new SolidColorBrush(Color.Parse(accentFg));
        StatusText.Foreground = new SolidColorBrush(Color.Parse(accentFg));
    }


    private void BuildUI()
    {
        var browseBtn = new Button { Content = "Обзор...", Margin = new Thickness(8, 0) }; browseBtn.Click += OnBrowseClick;
        var resetBtn = new Button { Content = "Сбросить", Margin = new Thickness(0, 0, 8, 0) }; resetBtn.Click += (_, _) => ResetProject(true);
        var saveAllBtn = new Button { Content = "Сохранить всё", Margin = new Thickness(0, 0, 8, 0) }; saveAllBtn.Click += OnExportAllClick;
        var themeBox = new ComboBox { ItemsSource = new[] { "Тема: Системная", "Тема: Тёмная", "Тема: Светлая", "Тема: Dracula", "Тема: Nord", "Тема: Monokai", "Тема: Cyberpunk", "Тема: Acid Neon" }, SelectedIndex = Math.Clamp(AppState.Current.ThemeIndex, 0, 7), Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center }; themeBox.SelectionChanged += (_, _) => ApplyTheme(themeBox.SelectedIndex);
        var topBar = new DockPanel { Margin = new Thickness(12) }; var titleLbl = new TextBlock { Text = "SPY", FontWeight = FontWeight.Bold, FontSize = 20, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
        DockPanel.SetDock(titleLbl, Dock.Left); DockPanel.SetDock(EngineInfoText, Dock.Right); DockPanel.SetDock(themeBox, Dock.Right); DockPanel.SetDock(saveAllBtn, Dock.Right); DockPanel.SetDock(resetBtn, Dock.Right); DockPanel.SetDock(browseBtn, Dock.Right);
        topBar.Children.Add(titleLbl); topBar.Children.Add(EngineInfoText); topBar.Children.Add(themeBox); topBar.Children.Add(saveAllBtn); topBar.Children.Add(resetBtn); topBar.Children.Add(browseBtn); topBar.Children.Add(GamePathBox);
        AssemblyTree.ItemTemplate = new FuncTreeDataTemplate<DnNode>((n, _) => new TextBlock { Text = n.Title, FontSize = 12.5 }, n => n.Children);
        AssemblyTree.SelectionChanged += (_, _) => { if (AssemblyTree.SelectedItem is not DnNode node || string.IsNullOrEmpty(node.DllPath)) return; _activeDll = node.DllPath; try { var dec = GetDecompiler(node.DllPath); CodeViewerBox.Text = node.Member != null ? dec.DecompileAsString(node.Member.MetadataToken) : (node.TypeDef != null ? dec.DecompileAsString(node.TypeDef.MetadataToken) : ""); AppState.Current.LastDllName = Path.GetFileName(node.DllPath); AppState.Current.LastTypeName = node.FullTypeName; AppState.Current.LastMethodName = node.Member?.Name ?? ""; AppState.Save(); } catch (Exception ex) { StatusText.Text = $"Ошибка: {ex.Message}"; } };
        var editM = new MenuItem { Header = "C#  Изменить метод (C#)..." }; editM.Click += (_, _) => { if (AssemblyTree.SelectedItem is DnNode n) OpenCodeEditorDialog(n, true); };
        var editC = new MenuItem { Header = "C#  Изменить класс (C#)..." }; editC.Click += (_, _) => { if (AssemblyTree.SelectedItem is DnNode n) OpenCodeEditorDialog(n, false); };
        var stubM = new MenuItem { Header = "Заменить тело метода заглушкой..." }; stubM.Click += (_, _) => { if (AssemblyTree.SelectedItem is DnNode n && n.Member != null) { var (ok, msg) = DllPatcher.StubMethod(n.DllPath, n.FullTypeName, n.Member.Name); StatusText.Text = msg; if (ok) { _decompilers.Remove(n.DllPath); CodeViewerBox.Text = GetDecompiler(n.DllPath).DecompileAsString(n.Member.MetadataToken); } } };
        var dnMenu = new ContextMenu(); dnMenu.Items.Add(editM); dnMenu.Items.Add(editC); dnMenu.Items.Add(new Separator()); dnMenu.Items.Add(stubM); AssemblyTree.ContextMenu = dnMenu; CodeViewerBox.ContextMenu = dnMenu;
        TreeSearchBox.TextChanged += (_, _) => RebuildTree(TreeSearchBox.Text ?? "");
        var bDec = new Button { Content = "Загрузить сборки" }; bDec.Click += (_, _) => { _decompilers.Clear(); RebuildTree(TreeSearchBox.Text ?? ""); };
        var bFind = new Button { Content = "Поиск (Ctrl+F)" }; bFind.Click += (_, _) => { CodeViewerBox.Focus(); AvaloniaEdit.Search.SearchPanel.Install(CodeViewerBox).Open(); };
        var bCmp = new Button { Content = "Скомпилировать DLL" }; bCmp.Click += OnCompileDllClick;
        var bSav = new Button { Content = "Экспорт .cs" }; bSav.Click += async (_, _) => { var s = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { SuggestedFileName = "Exported.cs", DefaultExtension = "cs" }); if (s != null) await File.WriteAllTextAsync(s.Path.LocalPath, CodeViewerBox.Text ?? ""); };
        var leftPanel = new DockPanel { Width = 330 }; DockPanel.SetDock(TreeSearchBox, Dock.Top); leftPanel.Children.Add(TreeSearchBox); leftPanel.Children.Add(AssemblyTree);
        var bAddDll = new Button { Content = "Добавить DLL..." }; bAddDll.Click += async (_, _) => { var fs = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Выберите одну или несколько сборок (.dll / .exe)", AllowMultiple = true, FileTypeFilter = new[] { new FilePickerFileType("Сборки .NET") { Patterns = new[] { "*.dll", "*.exe" } } } }); if (fs.Count == 0) return; foreach (var f in fs) { string p = f.Path.LocalPath; if (!_customDlls.Contains(p, StringComparer.OrdinalIgnoreCase)) _customDlls.Add(p); _decompilers.Remove(p); } RebuildTree(TreeSearchBox.Text ?? ""); };
        var codeBar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 8) }; codeBar.Children.Add(LangVersionBox); codeBar.Children.Add(bAddDll); codeBar.Children.Add(bDec); codeBar.Children.Add(bFind); codeBar.Children.Add(bCmp); codeBar.Children.Add(bSav);
        CodeViewerBox.Margin = new Thickness(10, 0, 0, 0); var tab1 = new DockPanel(); DockPanel.SetDock(codeBar, Dock.Top); DockPanel.SetDock(leftPanel, Dock.Left); tab1.Children.Add(codeBar); tab1.Children.Add(leftPanel); tab1.Children.Add(new Grid { Children = { CodeViewerBox } });
        AssetFileBox.SelectionChanged += (_, _) => { if (AssetFileBox.SelectedItem != null) OnLoadAssetsClick(null, new RoutedEventArgs()); };
        var bLoad = new Button { Content = "Обновить" }; bLoad.Click += OnLoadAssetsClick;
        var bExp = new Button { Content = "Извлечь (.png/.txt/.bin)" }; bExp.Click += async (_, _) => { if (AssetFileBox.SelectedItem is string fn && AssetsGrid.SelectedItem is AssetItemRow r) StatusText.Text = await AssetEditorHelper.ExportAsset(this, _assetsManager, Path.Combine(_dataDir, ResolveRealFile(fn)), r.PathId, r.Name.TrimStart('★', ' '), r.TypeName == "Texture2D" ? "png" : (r.TypeName == "TextAsset" ? "txt" : "raw")); else StatusText.Text = "Выберите ассет."; };
        var bEdt = new Button { Content = "Редактировать текст" }; bEdt.Click += async (_, _) => { if (AssetFileBox.SelectedItem is string fn && AssetsGrid.SelectedItem is AssetItemRow r) { if (r.TypeName != "TextAsset") { StatusText.Text = $"Редактирование текста недоступно для {r.TypeName}."; return; } StatusText.Text = await AssetEditorHelper.EditTextAssetDialog(this, _assetsManager, Path.Combine(_dataDir, ResolveRealFile(fn)), r.PathId, r.Name.TrimStart('★', ' ')); MarkModified(fn, r.PathId, r.Name.TrimStart('★', ' ')); OnLoadAssetsClick(null, new RoutedEventArgs()); } else StatusText.Text = "Выберите TextAsset."; };
        var bRep = new Button { Content = "Заменить файлом..." }; bRep.Click += async (_, _) => { if (AssetFileBox.SelectedItem is string fn && AssetsGrid.SelectedItem is AssetItemRow r) { if (r.TypeName is not ("Texture2D" or "TextAsset")) { StatusText.Text = $"Для типа {r.TypeName} пока нет способа замены."; return; } StatusText.Text = await AssetEditorHelper.ReplaceAssetFromFile(this, _assetsManager, Path.Combine(_dataDir, ResolveRealFile(fn)), r.PathId, r.TypeName); MarkModified(fn, r.PathId, r.Name.TrimStart('★', ' ')); OnLoadAssetsClick(null, new RoutedEventArgs()); } else StatusText.Text = "Выберите ассет."; };
        _bNextMod = new Button { Content = "★ Изменённые (0)" }; _bNextMod.Click += OnNextModifiedClick;
        AssetSearchBox.TextChanged += (_, _) => { string q = AssetSearchBox.Text ?? ""; AssetsGrid.ItemsSource = string.IsNullOrWhiteSpace(q) ? _allAssets : _allAssets.Where(a => a.Name.Contains(q, StringComparison.OrdinalIgnoreCase) || a.TypeName.Contains(q, StringComparison.OrdinalIgnoreCase) || a.Details.Contains(q, StringComparison.OrdinalIgnoreCase)).ToList(); };
        var mPng = new MenuItem { Header = "Экспортировать текстуру (.png)..." }; mPng.Click += async (_, _) => { if (AssetFileBox.SelectedItem is string fn && AssetsGrid.SelectedItem is AssetItemRow r) StatusText.Text = await AssetEditorHelper.ExportAsset(this, _assetsManager, Path.Combine(_dataDir, ResolveRealFile(fn)), r.PathId, r.Name.TrimStart('★', ' '), "png"); };
        var mTxt = new MenuItem { Header = "Экспортировать текст (.txt)..." }; mTxt.Click += async (_, _) => { if (AssetFileBox.SelectedItem is string fn && AssetsGrid.SelectedItem is AssetItemRow r) StatusText.Text = await AssetEditorHelper.ExportAsset(this, _assetsManager, Path.Combine(_dataDir, ResolveRealFile(fn)), r.PathId, r.Name.TrimStart('★', ' '), "txt"); };
        var mRaw = new MenuItem { Header = "Экспортировать сырой дамп (.bin)..." }; mRaw.Click += async (_, _) => { if (AssetFileBox.SelectedItem is string fn && AssetsGrid.SelectedItem is AssetItemRow r) StatusText.Text = await AssetEditorHelper.ExportAsset(this, _assetsManager, Path.Combine(_dataDir, ResolveRealFile(fn)), r.PathId, r.Name.TrimStart('★', ' '), "raw"); };
        var mEd = new MenuItem { Header = "Редактировать текст в окне..." }; mEd.Click += async (_, _) => { if (AssetFileBox.SelectedItem is string fn && AssetsGrid.SelectedItem is AssetItemRow r) { StatusText.Text = await AssetEditorHelper.EditTextAssetDialog(this, _assetsManager, Path.Combine(_dataDir, ResolveRealFile(fn)), r.PathId, r.Name.TrimStart('★', ' ')); MarkModified(fn, r.PathId, r.Name.TrimStart('★', ' ')); OnLoadAssetsClick(null, new RoutedEventArgs()); } };
        var mRp = new MenuItem { Header = "Заменить ассет файлом (.png / .txt / .bin)..." }; mRp.Click += async (_, _) => { if (AssetFileBox.SelectedItem is string fn && AssetsGrid.SelectedItem is AssetItemRow r) { StatusText.Text = await AssetEditorHelper.ReplaceAssetFromFile(this, _assetsManager, Path.Combine(_dataDir, ResolveRealFile(fn)), r.PathId, r.TypeName); MarkModified(fn, r.PathId, r.Name.TrimStart('★', ' ')); OnLoadAssetsClick(null, new RoutedEventArgs()); } };
        var aMenu = new ContextMenu(); aMenu.Items.Add(mPng); aMenu.Items.Add(mTxt); aMenu.Items.Add(mRaw); aMenu.Items.Add(new Separator()); aMenu.Items.Add(mEd); aMenu.Items.Add(mRp);
        aMenu.Opening += (_, _) => { if (AssetsGrid.SelectedItem is not AssetItemRow r) return; bool isTex = r.TypeName == "Texture2D", isTxt = r.TypeName == "TextAsset", isBun = AssetFileBox.SelectedItem is string s && (s.Contains(".unity3d") || s.Contains(".bundle")); mPng.IsEnabled = isTex; mPng.Header = isTex ? "Экспортировать текстуру (.png)..." : $"Экспортировать текстуру (.png) — недоступно для {r.TypeName}"; mTxt.IsEnabled = isTxt; mTxt.Header = isTxt ? "Экспортировать текст (.txt)..." : $"Экспортировать текст (.txt) — недоступно для {r.TypeName}"; mEd.IsEnabled = isTxt && !isBun; mEd.Header = isTxt ? (isBun ? "Редактировать текст — пока нет способа для бандлов (.unity3d)" : "Редактировать текст в окне...") : $"Редактировать текст — недоступно для {r.TypeName}"; bool canRep = (isTex || isTxt) && !isBun; mRp.IsEnabled = canRep; mRp.Header = canRep ? "Заменить ассет файлом (.png / .txt)..." : (isBun ? "Заменить ассет — пока нет способа для бандлов (.unity3d)" : $"Заменить {r.TypeName} — пока нет способа замены"); };
        AssetsGrid.ContextMenu = aMenu;
        AssetsGrid.Columns.Add(new DataGridTextColumn { Header = "PathID", Binding = new Binding("PathId"), Width = new DataGridLength(100) });
        AssetsGrid.Columns.Add(new DataGridTextColumn { Header = "Имя", Binding = new Binding("Name"), Width = new DataGridLength(300) });
        AssetsGrid.Columns.Add(new DataGridTextColumn { Header = "Тип", Binding = new Binding("TypeName"), Width = new DataGridLength(130) });
        AssetsGrid.Columns.Add(new DataGridTextColumn { Header = "Детали / Архив", Binding = new Binding("Details"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        var assetBar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 8) }; assetBar.Children.Add(AssetFileBox); assetBar.Children.Add(bLoad); assetBar.Children.Add(AssetSearchBox); assetBar.Children.Add(bExp); assetBar.Children.Add(bEdt); assetBar.Children.Add(bRep); assetBar.Children.Add(_bNextMod);
        var previewPanel = new DockPanel { Width = 360, Margin = new Thickness(10, 0, 0, 0) }; DockPanel.SetDock(_previewInfo, Dock.Top); previewPanel.Children.Add(_previewInfo); previewPanel.Children.Add(new Grid { Children = { _previewImg, _previewTxt } } );
        AssetsGrid.SelectionChanged += async (_, _) => { _previewCts?.Cancel(); var cts = _previewCts = new(); if (_previewImg.Source is IDisposable d) d.Dispose(); _previewImg.Source = null; _previewTxt.IsVisible = false; if (AssetFileBox.SelectedItem is not string fn || AssetsGrid.SelectedItem is not AssetItemRow r) { _previewInfo.Text = "Выберите ассет"; return; } _previewInfo.Text = "Загрузка превью..."; string fp = Path.Combine(_dataDir, ResolveRealFile(fn)); long pid = r.PathId; string tp = r.TypeName, nm = r.Name.TrimStart('★', ' '); var res = await System.Threading.Tasks.Task.Run(() => AssetEditorHelper.GetAssetPreviewData(_assetsManager, fp, pid, tp)); if (cts.IsCancellationRequested) { res.bmp?.Dispose(); return; } _previewInfo.Text = $"{nm}\n{res.info}"; if (res.bmp != null) _previewImg.Source = res.bmp; else if (res.txt != null) { _previewTxt.Text = res.txt; _previewTxt.IsVisible = true; } };
        var tab2 = new DockPanel(); DockPanel.SetDock(assetBar, Dock.Top); DockPanel.SetDock(previewPanel, Dock.Right); tab2.Children.Add(assetBar); tab2.Children.Add(previewPanel); tab2.Children.Add(AssetsGrid);
        var bRip = new Button { Content = "Экспортировать проект" }; bRip.Click += OnRunRipperClick; var tab3 = new StackPanel { Spacing = 10, Margin = new Thickness(10), MaxWidth = 550, HorizontalAlignment = HorizontalAlignment.Left }; tab3.Children.Add(RipOutputBox); tab3.Children.Add(bRip);
        var tabs = new TabControl { Margin = new Thickness(10, 0) }; tabs.Items.Add(new TabItem { Header = "1. Просмотр / Редактор кода", Content = tab1 }); tabs.Items.Add(new TabItem { Header = "2. Ассеты", Content = tab2 }); tabs.Items.Add(new TabItem { Header = "3. Экспорт проекта", Content = tab3 });
        var statusBorder = new Border { Padding = new Thickness(12, 6), Child = StatusText }; var root = new DockPanel(); DockPanel.SetDock(topBar, Dock.Top); DockPanel.SetDock(statusBorder, Dock.Bottom); root.Children.Add(topBar); root.Children.Add(statusBorder); root.Children.Add(tabs); Content = root;
    }


    private void ResetProject(bool clear = true)
    {
        _decompilers.Clear();
        _assetsManager.UnloadAllAssetsFiles();
        _allAssets.Clear();
        _modifiedAssets.Clear();
        _modCursor = -1;
        _activeDll = "";
        if (_bNextMod != null) _bNextMod.Content = "★ Изменённые (0)";

        AssemblyTree.ItemsSource = null;
        CodeViewerBox.Text = "";
        AssetsGrid.ItemsSource = null;
        AssetFileBox.ItemsSource = null;
        AssetSearchBox.Text = "";
        TreeSearchBox.Text = "";
        EngineInfoText.Text = "";

        if (clear)
        {
            _dataDir = "";
            GamePathBox.Text = "";
            AppState.Current.LastGamePath = "";
            AppState.Current.LastDllName = "";
            AppState.Current.LastTypeName = "";
            AppState.Current.LastMethodName = "";
            AppState.Save();
            StatusText.Text = "Проект очищен.";
        }
    }


    private void OnExportAllClick(object? s, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_dataDir) || !Directory.Exists(_dataDir))
        {
            StatusText.Text = "Сначала выберите папку с игрой.";
            return;
        }

        // Закрываем все открытые потоки и сбрасываем кэши, чтобы все изменения 100% записались в файлы игры
        _decompilers.Clear();
        _assetsManager.UnloadAllAssetsFiles();

        var modifiedFiles = Directory.GetFiles(_dataDir, "*.bak", SearchOption.AllDirectories)
            .Select(f => Path.GetFileName(f[..^4]))
            .Distinct()
            .ToList();

        StatusText.Text = modifiedFiles.Count > 0
            ? $"Все изменения сохранены в папке игры! Изменённые файлы: {string.Join(", ", modifiedFiles)}"
            : "Все файлы синхронизированы с диском. Изменённых файлов пока нет.";
    }

    private async void OpenCodeEditorDialog(DnNode node, bool singleMethod)
    {
        if (node.TypeDef == null) return;
        string targetType = node.FullTypeName;
        string? targetMethod = singleMethod && node.Member != null ? node.Member.Name : null;

        var dec = GetDecompiler(node.DllPath);
        string fullClassCode = dec.DecompileAsString(node.TypeDef.MetadataToken);
        string editorCode;

        if (!string.IsNullOrEmpty(targetMethod) && node.Member != null)
        {
            var usings = fullClassCode.Split('\n')
                .Where(l => l.TrimStart().StartsWith("using ") && l.TrimEnd().EndsWith(";"))
                .Select(l => l.TrimEnd());

            string methodCode = string.Join("\n", dec.DecompileAsString(node.Member.MetadataToken)
                .Split('\n')
                .Where(l => !l.TrimStart().StartsWith("using ") || !l.TrimEnd().EndsWith(";"))).TrimEnd();

            string indentedMethod = string.Join("\n", methodCode.Split('\n').Select(l => "\t" + l));
            string shortClassName = node.TypeDef.Name;
            string ns = node.TypeDef.Namespace;

            editorCode = string.IsNullOrEmpty(ns)
                ? $"{string.Join("\n", usings)}\n\npublic partial class {shortClassName}\n{{\n{indentedMethod}\n}}"
                : $"{string.Join("\n", usings)}\n\nnamespace {ns}\n{{\n\tpublic partial class {shortClassName}\n\t{{\n{indentedMethod}\n\t}}\n}}";
        }
        else
        {
            editorCode = fullClassCode;
        }

        var editor = CodeEditorHelper.CreateCSharpEditor(
            isReadOnly: false,
            getDecompilers: () => _decompilers.Values);
        editor.Text = editorCode;

        var errorBox = new TextBox
        {
            Height = 115,
            IsReadOnly = true,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 12,
            Foreground = Brushes.IndianRed,
            PlaceholderText = "Ошибки компиляции (если появятся, отобразятся здесь с номером строки)..."
        };

        var compileBtn = new Button { Content = "Компилировать", Padding = new Thickness(18, 6) };
        var cancelBtn = new Button { Content = "Отмена", Padding = new Thickness(14, 6) };

        var win = new Window
        {
            Title = singleMethod && targetMethod != null
                ? $"Редактирование метода — {targetType}.{targetMethod}"
                : $"Редактирование класса — {targetType}",
            Width = 920,
            Height = 680,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };

        compileBtn.Click += (_, _) =>
        {
            var (ok, msg) = DllPatcher.CompileAndInject(node.DllPath, editor.Text ?? "", targetType, targetMethod, fullClassCode);
            if (!ok)
            {
                errorBox.Text = msg;
                return;
            }

            errorBox.Text = "";
            StatusText.Text = msg;
            _decompilers.Remove(node.DllPath);
            CodeViewerBox.Text = GetDecompiler(node.DllPath).DecompileAsString(
                node.Member != null ? node.Member.MetadataToken : node.TypeDef.MetadataToken);
            win.Close();
        };
        cancelBtn.Click += (_, _) => win.Close();

        var bottomBtns = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Margin = new Thickness(10, 8)
        };
        bottomBtns.Children.Add(compileBtn);
        bottomBtns.Children.Add(cancelBtn);

        var bottomArea = new StackPanel();
        bottomArea.Children.Add(new TextBlock { Text = "Ошибки:", Margin = new Thickness(10, 4, 10, 2), FontWeight = FontWeight.SemiBold });
        bottomArea.Children.Add(new Border { Margin = new Thickness(10, 0), Child = errorBox });
        bottomArea.Children.Add(bottomBtns);

        var editorHost = new Grid { Children = { editor } };

        var layout = new DockPanel();
        DockPanel.SetDock(bottomArea, Dock.Bottom);
        layout.Children.Add(bottomArea);
        layout.Children.Add(editorHost);

        win.Content = layout;
        await win.ShowDialog(this);
    }


    private CSharpDecompiler GetDecompiler(string dllPath)
    {
        if (!_decompilers.TryGetValue(dllPath, out var dec))
        {
            var lang = LangVersionBox.SelectedIndex == 0 ? DecompilerLang.CSharp4 : DecompilerLang.Latest;
            dec = new CSharpDecompiler(dllPath, new DecompilerSettings(lang) { ThrowOnAssemblyResolveErrors = false });
            _decompilers[dllPath] = dec;
        }
        return dec;
    }

    private void RebuildTree(string filter)
    {
        var dllSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string mDir = string.IsNullOrEmpty(_dataDir) ? "" : Path.Combine(_dataDir, "Managed");
        if (Directory.Exists(mDir))
        {
            var mDlls = Directory.GetFiles(mDir, "Assembly-*.dll");
            if (mDlls.Length == 0) mDlls = Directory.GetFiles(mDir, "*.dll").Take(25).ToArray();
            foreach (var d in mDlls) dllSet.Add(d);
        }
        foreach (var d in _customDlls.Where(File.Exists)) dllSet.Add(d);

        if (dllSet.Count == 0)
        {
            AssemblyTree.ItemsSource = null;
            StatusText.Text = "Сборки не загружены. Выберите папку с игрой или нажмите «Добавить DLL...»";
            return;
        }

        var rootNodes = new List<DnNode>();
        foreach (var dll in dllSet)
        {
            try
            {
                var dec = GetDecompiler(dll);
                var asmNode = new DnNode { Title = Path.GetFileName(dll), DllPath = dll };
                var byNs = dec.TypeSystem.MainModule.TypeDefinitions
                    .Where(t => !t.Name.StartsWith("<") && t.DeclaringType == null)
                    .GroupBy(t => string.IsNullOrEmpty(t.Namespace) ? "-" : t.Namespace)
                    .OrderBy(g => g.Key);

                foreach (var ns in byNs)
                {
                    var nsNode = new DnNode { Title = ns.Key, DllPath = dll };
                    foreach (var t in ns.OrderBy(x => x.Name))
                    {
                        bool tMatch = string.IsNullOrWhiteSpace(filter) || t.Name.Contains(filter, StringComparison.OrdinalIgnoreCase);
                        var tNode = new DnNode { Title = t.Name, DllPath = dll, FullTypeName = t.FullName, TypeDef = t };
                        foreach (var m in t.Members.Where(x => !x.Name.StartsWith("<")))
                        {
                            if (tMatch || m.Name.Contains(filter, StringComparison.OrdinalIgnoreCase))
                                tNode.Children.Add(new DnNode { Title = m is IMethod ? $"{m.Name}()" : m.Name, DllPath = dll, FullTypeName = t.FullName, TypeDef = t, Member = m });
                        }
                        if (tMatch || tNode.Children.Count > 0) nsNode.Children.Add(tNode);
                    }
                    if (nsNode.Children.Count > 0) asmNode.Children.Add(nsNode);
                }
                rootNodes.Add(asmNode);
            }
            catch { }
        }
        AssemblyTree.ItemsSource = rootNodes;
        StatusText.Text = $"Загружено сборок: {rootNodes.Count}";
    }


    private void OnFileDrop(object? s, DragEventArgs e)
    {
        var f = e.DataTransfer.TryGetFiles()?.FirstOrDefault()?.Path.LocalPath;
        if (!string.IsNullOrEmpty(f)) Scan(f);
    }
    private async void OnBrowseClick(object? s, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Выберите папку с игрой или *_Data",
            AllowMultiple = false
        });
        if (folders.Count > 0)
        {
            Scan(folders[0].Path.LocalPath.TrimEnd('\\', '/'));
        }
    }

    private void Scan(string p)
    {
        try
        {
            ResetProject(clear: false);
            string r = (File.Exists(p) ? Path.GetDirectoryName(p)! : p).TrimEnd('\\', '/');
            if (!Directory.Exists(r)) return;
            GamePathBox.Text = r; AppState.Current.LastGamePath = r; AppState.Save();
            _dataDir = Directory.GetDirectories(r, "*_Data").FirstOrDefault() ?? r;

            var files = new List<string>();
            foreach (var f in Directory.GetFiles(_dataDir))
            {
                string ext = Path.GetExtension(f).ToLowerInvariant(), nm = Path.GetFileName(f), lnm = nm.ToLowerInvariant();
                if (ext is ".unity3d" or ".bundle" || lnm == "data.unity3d")
                {
                    files.Add($"{nm} [Все]");
                    try
                    {
                        var bun = _assetsManager.LoadBundleFile(f, true);
                        foreach (var di in bun.file.BlockAndDirInfo.DirectoryInfos)
                        {
                            if (di.Name.EndsWith(".resS", StringComparison.OrdinalIgnoreCase) || di.Name.EndsWith(".resource", StringComparison.OrdinalIgnoreCase)) continue;
                            files.Add($"{nm} -> {di.Name}");
                        }
                    }
                    catch { }
                }
                else if (ext == ".assets" || lnm is "maindata" or "globalgamemanagers" || (string.IsNullOrEmpty(ext) && lnm.StartsWith("level")))
                {
                    files.Add(nm);
                }
            }
            AssetFileBox.ItemsSource = files; if (files.Count > 0) AssetFileBox.SelectedIndex = 0;

            string uVer = "неизвестна";
            foreach (var pr in new[] { "globalgamemanagers", "mainData", "data.unity3d" })
            {
                string pf = Path.Combine(_dataDir, pr); if (!File.Exists(pf)) continue;
                try
                {
                    if (pr == "data.unity3d")
                    {
                        var b = _assetsManager.LoadBundleFile(pf, true);
                        for (int i = 0; i < b.file.BlockAndDirInfo.DirectoryInfos.Count; i++)
                        {
                            try { var a = _assetsManager.LoadAssetsFileFromBundle(b, i, true); uVer = a.file.Metadata.UnityVersion; if (!string.IsNullOrWhiteSpace(uVer) && uVer != "0.0.0") break; } catch { }
                        }
                    }
                    else { var a = _assetsManager.LoadAssetsFile(pf, true); uVer = a.file.Metadata.UnityVersion; }
                    if (!string.IsNullOrWhiteSpace(uVer) && uVer != "0.0.0") break;
                }
                catch { }
            }
            string gameRoot = Path.GetDirectoryName(_dataDir) ?? _dataDir;
            bool il2 = File.Exists(Path.Combine(_dataDir, "il2cpp_data", "Metadata", "global-metadata.dat")) || File.Exists(Path.Combine(gameRoot, "GameAssembly.dll"));
            EngineInfoText.Text = $"Unity {uVer} • {(il2 ? "IL2CPP" : "Mono")}";

            RebuildTree("");
            if (files.Count > 0) OnLoadAssetsClick(null, new RoutedEventArgs());
        }
        catch (Exception ex) { StatusText.Text = $"Ошибка сканирования: {ex.Message}"; }
    }



    private void OnCompileDllClick(object? s, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_activeDll) || string.IsNullOrEmpty(_editTargetType))
        {
            StatusText.Text = "Нажмите ПКМ по методу или классу в дереве -> «Изменить метод (C#)...»";
            return;
        }
        var (ok, msg) = DllPatcher.CompileAndInject(_activeDll, CodeViewerBox.Text ?? "", _editTargetType, _editTargetMethod);
        StatusText.Text = msg;
        if (ok) _decompilers.Remove(_activeDll);
    }

    private void OnLoadAssetsClick(object? s, RoutedEventArgs e)
    {
        if (AssetFileBox.SelectedItem is not string sel) return;
        string realFile = ResolveRealFile(sel);
        int arrowIdx = sel.IndexOf(" -> ", StringComparison.Ordinal);
        string? subFilter = arrowIdx >= 0 ? sel.Substring(arrowIdx + 4) : null;
        string full = Path.Combine(_dataDir, realFile);
        var rows = new List<AssetItemRow>();
        try
        {
            var targets = new List<(string subName, AssetsFileInstance inst)>();
            bool isBun = realFile.EndsWith(".unity3d", StringComparison.OrdinalIgnoreCase) || realFile.EndsWith(".bundle", StringComparison.OrdinalIgnoreCase);
            if (isBun)
            {
                var bun = _assetsManager.LoadBundleFile(full, true);
                for (int i = 0; i < bun.file.BlockAndDirInfo.DirectoryInfos.Count; i++)
                {
                    var di = bun.file.BlockAndDirInfo.DirectoryInfos[i];
                    if (di.Name.EndsWith(".resS", StringComparison.OrdinalIgnoreCase) || di.Name.EndsWith(".resource", StringComparison.OrdinalIgnoreCase)) continue;
                    if (subFilter != null && !di.Name.Equals(subFilter, StringComparison.OrdinalIgnoreCase)) continue;
                    try { var ins = _assetsManager.LoadAssetsFileFromBundle(bun, i, true); _assetsManager.LoadClassDatabaseFromPackage(ins.file.Metadata.UnityVersion); targets.Add((di.Name, ins)); } catch { }
                }
            }
            else
            {
                var ins = _assetsManager.LoadAssetsFile(full, true);
                try { _assetsManager.LoadClassDatabaseFromPackage(ins.file.Metadata.UnityVersion); } catch { }
                targets.Add((realFile, ins));
            }

            foreach (var (subName, inst) in targets)
            {
                foreach (var inf in inst.file.AssetInfos.Take(8000))
                {
                    var t = (AssetClassID)inf.TypeId; string n = $"Asset_{inf.PathId}", d = isBun ? $"[{subName}] {inf.ByteSize} байт" : $"{inf.ByteSize} байт";
                    try
                    {
                        var bf = _assetsManager.GetBaseField(inst, inf); var nf = bf.Get("m_Name");
                        if (!nf.IsDummy && !string.IsNullOrEmpty(nf.AsString)) n = nf.AsString;
                        if (t == AssetClassID.Texture2D) d = isBun ? $"[{subName}] {bf.Get("m_Width").AsInt}x{bf.Get("m_Height").AsInt}" : $"{bf.Get("m_Width").AsInt}x{bf.Get("m_Height").AsInt}";
                    }
                    catch { }
                    bool isMod = _modifiedAssets.Any(m => m.FileItem == sel && m.PathId == inf.PathId);
                    rows.Add(new AssetItemRow(inf.PathId, isMod ? $"★ {n}" : n, t.ToString(), d));
                    if (rows.Count >= 8000) break;
                }
                if (rows.Count >= 8000) break;
            }
            _allAssets = rows; AssetsGrid.ItemsSource = _allAssets; StatusText.Text = $"Загружено из {sel}: {_allAssets.Count} ассетов";
        }
        catch
        {
            try
            {
                _allAssets = LegacyUnityAssets.ReadEntries(full).Select(x => new AssetItemRow(x.PathId, x.Name, x.TypeName, $"{x.DataSize} байт (offset: 0x{x.DataOffset:X})")).ToList();
                AssetsGrid.ItemsSource = _allAssets; StatusText.Text = $"Ассетов (Unity 3/4 Native): {_allAssets.Count}";
            }
            catch (Exception ex) { StatusText.Text = $"Ошибка чтения {realFile}: {ex.Message}"; }
        }
    }




    private void OnRunRipperClick(object? s, RoutedEventArgs e)
    {
        string exe = Path.Combine(AppContext.BaseDirectory, "Tools", "AssetRipper", "AssetRipper.exe");
        if (!File.Exists(exe) || string.IsNullOrEmpty(_dataDir))
        {
            StatusText.Text = $"Положите AssetRipper в {Path.GetDirectoryName(exe)} и выберите игру.";
            return;
        }
        string outD = string.IsNullOrWhiteSpace(RipOutputBox.Text) ? Path.Combine(Path.GetDirectoryName(_dataDir)!, "Ripped") : RipOutputBox.Text;
        Process.Start(new ProcessStartInfo { FileName = exe, Arguments = $"\"{_dataDir}\" -o \"{outD}\"", UseShellExecute = true });
    }
}