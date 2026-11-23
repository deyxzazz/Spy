using AssetsTools.NET;
using AssetsTools.NET.Extra;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.VisualTree;
using AvaloniaEdit;
using AvaloniaEdit.Utils;
using ICSharpCode.Decompiler;
using ICSharpCode.Decompiler.CSharp;
using ICSharpCode.Decompiler.TypeSystem;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using DecompilerLang = ICSharpCode.Decompiler.CSharp.LanguageVersion;

namespace SpyTool2;

public record AssetItemRow(long PathId, string Name, string TypeName, string Details);

public class DnNode
{
    public string Title { get; set; } = "";
    public string DllPath { get; set; } = "";
    public string FullTypeName { get; set; } = "";
    public ITypeDefinition? TypeDef { get; set; }
    public IMember? Member { get; set; }
    public Il2CppTypeInfo? Il2CppType { get; set; }
    public string? Il2CppMember { get; set; }
    public ObservableCollection<DnNode> Children { get; } = new();
}

public partial class MainWindow : Window
{
    private readonly List<(string FileItem, long PathId, string Name)> _modifiedAssets = new();
    private int _modCursor = -1;
    private Button? _bNextMod;
    private Border? _windowFrameBorder;
    private Border? _topBarContainer;
    private Border? _statusBarContainer;
    private Button? _maxRestoreBtn;
    private Button? _minBtn;
    private Button? _closeBtn;
    private Button? _browseBtn;
    private Button? _resetBtn;
    private Button? _saveAllBtn;
    private ComboBox? _themeBox;
    private ComboBox? _langBox;
    private MenuItem? _editMethodMenuItem;
    private MenuItem? _editClassMenuItem;
    private MenuItem? _stubMethodMenuItem;
    private Button? _bDec;
    private Button? _bFind;
    private Button? _bCmp;
    private Button? _bSav;
    private Button? _bAddDll;
    private Button? _bLoad;
    private Button? _bExp;
    private Button? _bEdt;
    private Button? _bRep;
    private Button? _bRip;
    private TabItem? _tabCode;
    private TabItem? _tabAssets;
    private TabItem? _tabExport;
    private TabItem? _tabDumper;
    private TabItem? _tabAbout;
    private DataGridTextColumn? _colPathId;
    private DataGridTextColumn? _colName;
    private DataGridTextColumn? _colType;
    private DataGridTextColumn? _colDetails;

    private string _statusKey = "status.ready";
    private object[] _statusArgs = Array.Empty<object>();
    private string _engineInfoKey = "engine.not_selected";
    private object[] _engineInfoArgs = Array.Empty<object>();

    private readonly List<string> _customDlls = new();
    private string _editTargetType = "", _dataDir = "", _activeDll = "";
    private string? _editTargetMethod = null;
    private readonly Dictionary<string, CSharpDecompiler> _decompilers = new();
    private readonly AssetsManager _assetsManager = new();
    private List<AssetItemRow> _allAssets = new();

    private readonly TextBox GamePathBox = new() { PlaceholderText = Loc.T("main.game_path.placeholder"), CornerRadius = new CornerRadius(6) };
    private readonly TextBlock EngineInfoText = new() { Text = Loc.T("engine.not_selected"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
    private readonly ComboBox LangVersionBox = new() { Width = 200, ItemsSource = new List<string> { "C# 4.0 (Unity 4/5)", "C# Latest" }, SelectedIndex = 0, CornerRadius = new CornerRadius(6) };
    private readonly TextBox TreeSearchBox = new() { PlaceholderText = Loc.T("main.tree_search.placeholder"), Margin = new Thickness(0, 0, 0, 6), CornerRadius = new CornerRadius(6) };
    private readonly TreeView AssemblyTree = new();
    private readonly TextEditor CodeViewerBox = CodeEditorHelper.CreateCSharpEditor(isReadOnly: true);
    private readonly ComboBox AssetFileBox = new() { Width = 300, PlaceholderText = Loc.T("main.asset_file.placeholder"), CornerRadius = new CornerRadius(6) };
    private readonly DataGrid AssetsGrid = new() { AutoGenerateColumns = false, IsReadOnly = true };
    private readonly TextBox AssetSearchBox = new() { Watermark = Loc.T("main.asset_search.watermark"), Width = 260, CornerRadius = new CornerRadius(6) };
    private readonly TextBox RipOutputBox = new() { PlaceholderText = Loc.T("main.rip_output.placeholder"), CornerRadius = new CornerRadius(6) };
    private readonly TextBlock StatusText = new() { Text = Loc.T("status.ready") };

    private void SetStatus(string key, params object[] args)
    {
        _statusKey = key;
        _statusArgs = args;
        StatusText.Text = args.Length > 0 ? string.Format(Loc.T(key), args) : Loc.T(key);
    }

    private void SetEngineInfo(string key, params object[] args)
    {
        _engineInfoKey = key;
        _engineInfoArgs = args;
        EngineInfoText.Text = args.Length > 0 ? string.Format(Loc.T(key), args) : Loc.T(key);
    }

    private static string ResolveRealFile(string sel)
    {
        int i = sel.IndexOf(" -> ", StringComparison.Ordinal);
        string allSuffix = $" [{Loc.T("common.all")}]";
        if (i >= 0) return sel[..i];
        if (sel.EndsWith(allSuffix, StringComparison.Ordinal)) return sel[..^allSuffix.Length];
        return sel.EndsWith(" [Все]", StringComparison.Ordinal) ? sel[..^6] : sel;
    }

    private void MarkModified(string f, long pid, string nm)
    {
        if (!_modifiedAssets.Any(m => m.FileItem == f && m.PathId == pid)) _modifiedAssets.Add((f, pid, nm));
        if (_bNextMod != null) _bNextMod.Content = string.Format(Loc.T("main.btn.modified_count"), _modifiedAssets.Count);
    }

    private void OnNextModifiedClick(object? s, RoutedEventArgs e)
    {
        if (_modifiedAssets.Count == 0) { SetStatus("status.no_modified_assets"); return; }
        _modCursor = (_modCursor + 1) % _modifiedAssets.Count;
        var t = _modifiedAssets[_modCursor];
        if (!Equals(AssetFileBox.SelectedItem, t.FileItem)) AssetFileBox.SelectedItem = t.FileItem;
        var row = _allAssets.FirstOrDefault(x => x.PathId == t.PathId);
        if (row != null) { AssetsGrid.SelectedItem = row; AssetsGrid.ScrollIntoView(row, null); }
        SetStatus("status.modified_item", _modCursor + 1, _modifiedAssets.Count, t.Name);
    }

    public MainWindow()
    {
        InitializeComponent();
        Title = "Spy";
        Width = 1380;
        Height = 850;
        SystemDecorations = Avalonia.Controls.WindowDecorations.BorderOnly;
        ExtendClientAreaToDecorationsHint = true;

        this.GetObservable(WindowStateProperty).Subscribe(new WindowStateObserver(this));

        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DropEvent, (_, e) =>
        {
            var items = e.DataTransfer.TryGetFiles()?.Select(x => x.Path.LocalPath).ToList();
            if (items == null || items.Count == 0) return;
            var singleFiles = items.Where(x => File.Exists(x) && !x.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)).ToList();
            if (singleFiles.Count > 0)
            {
                foreach (var d in singleFiles)
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

        if (AppState.Current.WindowWidth >= 800) Width = AppState.Current.WindowWidth;
        if (AppState.Current.WindowHeight >= 500) Height = AppState.Current.WindowHeight;
        if (AppState.Current.IsMaximized) WindowState = WindowState.Maximized;

        BuildUI();
        Loc.LanguageChanged += UpdateLocalization;
        Closing += (_, _) =>
        {
            AppState.Current.IsMaximized = WindowState == WindowState.Maximized;
            if (WindowState == WindowState.Normal && Bounds.Width >= 800 && Bounds.Height >= 500)
            {
                AppState.Current.WindowWidth = Bounds.Width;
                AppState.Current.WindowHeight = Bounds.Height;
            }
            AppState.Save();
        };
        Closed += (_, _) => Loc.LanguageChanged -= UpdateLocalization;

        UpdateWindowChromeState(WindowState);
        ApplyTheme(AppState.Current.ThemeIndex);
        if (!string.IsNullOrEmpty(AppState.Current.LastGamePath) && Directory.Exists(AppState.Current.LastGamePath))
            Scan(AppState.Current.LastGamePath);
    }

    private void UpdateLocalization()
    {
        GamePathBox.PlaceholderText = Loc.T("main.game_path.placeholder");
        TreeSearchBox.PlaceholderText = Loc.T("main.tree_search.placeholder");
        AssetFileBox.PlaceholderText = Loc.T("main.asset_file.placeholder");
        AssetSearchBox.Watermark = Loc.T("main.asset_search.watermark");
        RipOutputBox.PlaceholderText = Loc.T("main.rip_output.placeholder");

        if (_minBtn != null) ToolTip.SetTip(_minBtn, Loc.T("window.minimize"));
        if (_maxRestoreBtn != null)
        {
            bool isMax = WindowState == WindowState.Maximized;
            ToolTip.SetTip(_maxRestoreBtn, isMax ? Loc.T("window.restore") : Loc.T("window.maximize"));
        }
        if (_closeBtn != null) ToolTip.SetTip(_closeBtn, Loc.T("window.close"));

        if (_browseBtn != null) _browseBtn.Content = Loc.T("main.btn.browse");
        if (_resetBtn != null) _resetBtn.Content = Loc.T("main.btn.reset");
        if (_saveAllBtn != null) _saveAllBtn.Content = Loc.T("main.btn.save_all");

        if (_langBox != null)
        {
            ToolTip.SetTip(_langBox, Loc.T("main.lang.tooltip"));
            int targetLangIdx = Loc.Current == "EN" ? 1 : 0;
            if (_langBox.SelectedIndex != targetLangIdx)
                _langBox.SelectedIndex = targetLangIdx;
        }

        if (_themeBox != null)
        {
            int prevIdx = _themeBox.SelectedIndex;
            _themeBox.ItemsSource = GetThemeItems();
            _themeBox.SelectedIndex = Math.Clamp(prevIdx, 0, 7);
        }

        if (_editMethodMenuItem != null) _editMethodMenuItem.Header = Loc.T("main.menu.edit_method");
        if (_editClassMenuItem != null) _editClassMenuItem.Header = Loc.T("main.menu.edit_class");
        if (_stubMethodMenuItem != null) _stubMethodMenuItem.Header = Loc.T("main.menu.stub_method");
        if (_bAddDll != null) _bAddDll.Content = Loc.T("main.btn.add_dll");
        if (_bDec != null) _bDec.Content = Loc.T("main.btn.load_assemblies");
        if (_bFind != null) _bFind.Content = Loc.T("main.btn.find");
        if (_bCmp != null) _bCmp.Content = Loc.T("main.btn.compile_dll");
        if (_bSav != null) _bSav.Content = Loc.T("main.btn.export_cs");
        if (_bLoad != null) _bLoad.Content = Loc.T("main.btn.refresh");
        if (_bExp != null) _bExp.Content = Loc.T("main.btn.extract_asset");
        if (_bEdt != null) _bEdt.Content = Loc.T("main.btn.edit_text");
        if (_bRep != null) _bRep.Content = Loc.T("main.btn.replace_file");
        if (_bNextMod != null) _bNextMod.Content = string.Format(Loc.T("main.btn.modified_count"), _modifiedAssets.Count);
        if (_colPathId != null) _colPathId.Header = Loc.T("main.grid.path_id");
        if (_colName != null) _colName.Header = Loc.T("main.grid.name");
        if (_colType != null) _colType.Header = Loc.T("main.grid.type");
        if (_colDetails != null) _colDetails.Header = Loc.T("main.grid.details");
        if (_bRip != null) _bRip.Content = Loc.T("main.btn.run_ripper");
        if (_tabCode != null) _tabCode.Header = Loc.T("main.tab.code");
        if (_tabAssets != null) _tabAssets.Header = Loc.T("main.tab.assets");
        if (_tabExport != null) _tabExport.Header = Loc.T("main.tab.export");
        if (_tabDumper != null) _tabDumper.Header = Loc.T("main.tab.il2cpp_dumper");
        if (_tabAbout != null) _tabAbout.Header = Loc.T("main.tab.about");

        if (!string.IsNullOrEmpty(_engineInfoKey))
            EngineInfoText.Text = _engineInfoArgs.Length > 0 ? string.Format(Loc.T(_engineInfoKey), _engineInfoArgs) : Loc.T(_engineInfoKey);
        if (!string.IsNullOrEmpty(_statusKey))
            StatusText.Text = _statusArgs.Length > 0 ? string.Format(Loc.T(_statusKey), _statusArgs) : Loc.T(_statusKey);
    }

    private static string[] GetThemeItems() => new[]
    {
        Loc.T("theme.system"),
        Loc.T("theme.dark"),
        Loc.T("theme.light"),
        Loc.T("theme.dracula"),
        Loc.T("theme.nord"),
        Loc.T("theme.monokai"),
        Loc.T("theme.cyberpunk"),
        Loc.T("theme.acid_neon")
    };

    private sealed class WindowStateObserver(MainWindow owner) : IObserver<WindowState>
    {
        public void OnCompleted() { }
        public void OnError(Exception error) { }
        public void OnNext(WindowState value) => owner.UpdateWindowChromeState(value);
    }

    private void UpdateWindowChromeState(WindowState state)
    {
        bool isMax = state == WindowState.Maximized;
        if (_windowFrameBorder != null)
        {
            _windowFrameBorder.Padding = isMax ? new Thickness(7) : new Thickness(0);
            _windowFrameBorder.BorderThickness = isMax ? new Thickness(0) : new Thickness(1);
        }
        if (_maxRestoreBtn != null)
        {
            _maxRestoreBtn.Content = isMax ? "❐" : "□";
            ToolTip.SetTip(_maxRestoreBtn, isMax ? Loc.T("window.restore") : Loc.T("window.maximize"));
        }
    }

    private void ToggleMaximizeRestore()
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    private void ApplyTheme(int idx)
    {
        if (idx < 0) return;
        AppState.Current.ThemeIndex = idx;
        AppState.Save();
        bool isLight = idx == 2 || (idx == 0 && Application.Current!.ActualThemeVariant == ThemeVariant.Light);
        Application.Current!.RequestedThemeVariant = isLight ? ThemeVariant.Light : ThemeVariant.Dark;
        var (winBg, codeBg, codeFg, accentFg, borderHex, barBg) = idx switch
        {
            2 => ("#F3F4F6", "#FFFFFF", "#1E1E1E", "#111827", "#D1D5DB", "#E5E7EB"),
            3 => ("#21222C", "#282A36", "#F8F8F2", "#BD93F9", "#44475A", "#191A21"),
            4 => ("#242933", "#2E3440", "#D8DEE9", "#88C0D0", "#3B4252", "#1F242D"),
            5 => ("#1E1F1C", "#272822", "#F8F8F2", "#A6E22E", "#3E3D32", "#171814"),
            6 => ("#0D0221", "#120428", "#00F0FF", "#FF2A6D", "#FF2A6D", "#090118"),
            7 => ("#050505", "#0A0F0A", "#39FF14", "#CCFF00", "#1F4D1F", "#020402"),
            _ => isLight
            ? ("#F3F4F6", "#FFFFFF", "#1E1E1E", "#111827", "#D1D5DB", "#E5E7EB")
            : ("#181818", "#1E1E1E", "#D4D4D4", "#569CD6", "#333333", "#141414")
        };
        var fgBrush = new SolidColorBrush(Color.Parse(codeFg));
        var accentBrush = new SolidColorBrush(Color.Parse(accentFg));
        var borderBrush = new SolidColorBrush(Color.Parse(borderHex));
        var barBrush = new SolidColorBrush(Color.Parse(barBg));
        Background = new SolidColorBrush(Color.Parse(winBg));
        Foreground = fgBrush;
        CodeViewerBox.Background = new SolidColorBrush(Color.Parse(codeBg));
        CodeViewerBox.Foreground = fgBrush;
        EngineInfoText.Foreground = accentBrush;
        StatusText.Foreground = accentBrush;
        if (_windowFrameBorder != null)
            _windowFrameBorder.BorderBrush = borderBrush;
        if (_topBarContainer != null)
        {
            _topBarContainer.Background = barBrush;
            _topBarContainer.BorderBrush = borderBrush;
        }
        if (_statusBarContainer != null)
        {
            _statusBarContainer.Background = barBrush;
            _statusBarContainer.BorderBrush = borderBrush;
        }
    }

    private void BuildUI()
    {
        _minBtn = new Button { Content = "—", Classes = { "winCtrl" } };
        ToolTip.SetTip(_minBtn, Loc.T("window.minimize"));
        _minBtn.Click += (_, _) => WindowState = WindowState.Minimized;
        _maxRestoreBtn = new Button { Content = "□", Classes = { "winCtrl" } };
        ToolTip.SetTip(_maxRestoreBtn, Loc.T("window.maximize"));
        _maxRestoreBtn.Click += (_, _) => ToggleMaximizeRestore();
        _closeBtn = new Button { Content = "✕", Classes = { "winClose" } };
        ToolTip.SetTip(_closeBtn, Loc.T("window.close"));
        _closeBtn.Click += (_, _) => Close();
        var windowControlsPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 2,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(6, 0, 0, 0)
        };
        windowControlsPanel.Children.Add(_minBtn);
        windowControlsPanel.Children.Add(_maxRestoreBtn);
        windowControlsPanel.Children.Add(_closeBtn);

        _browseBtn = new Button { Content = Loc.T("main.btn.browse"), Margin = new Thickness(8, 0), CornerRadius = new CornerRadius(6) };
        _browseBtn.Click += OnBrowseClick;
        _resetBtn = new Button { Content = Loc.T("main.btn.reset"), Margin = new Thickness(0, 0, 8, 0), CornerRadius = new CornerRadius(6) };
        _resetBtn.Click += (_, _) => ResetProject(true);
        _saveAllBtn = new Button { Content = Loc.T("main.btn.save_all"), Margin = new Thickness(0, 0, 8, 0), CornerRadius = new CornerRadius(6) };
        _saveAllBtn.Click += OnExportAllClick;
        _themeBox = new ComboBox { ItemsSource = GetThemeItems(), SelectedIndex = Math.Clamp(AppState.Current.ThemeIndex, 0, 7), Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center, CornerRadius = new CornerRadius(6) };
        _themeBox.SelectionChanged += (_, _) => ApplyTheme(_themeBox.SelectedIndex);
        _langBox = new ComboBox
        {
            ItemsSource = new[] { "RU", "EN" },
            SelectedIndex = Loc.Current == "EN" ? 1 : 0,
            Width = 68,
            Margin = new Thickness(0, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center,
            CornerRadius = new CornerRadius(6)
        };
        ToolTip.SetTip(_langBox, Loc.T("main.lang.tooltip"));
        _langBox.SelectionChanged += (_, _) =>
        {
            if (_langBox.SelectedItem is string lang)
                Loc.SetLanguage(lang);
        };

        var topBar = new DockPanel { Margin = new Thickness(12, 8, 8, 8), Background = Brushes.Transparent };
        var titleLbl = new TextBlock { Text = "SPY", FontWeight = FontWeight.Bold, FontSize = 18, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 14, 0) };
        DockPanel.SetDock(titleLbl, Dock.Left);
        DockPanel.SetDock(windowControlsPanel, Dock.Right);
        DockPanel.SetDock(EngineInfoText, Dock.Right);
        DockPanel.SetDock(_langBox, Dock.Right);
        DockPanel.SetDock(_themeBox, Dock.Right);
        DockPanel.SetDock(_saveAllBtn, Dock.Right);
        DockPanel.SetDock(_resetBtn, Dock.Right);
        DockPanel.SetDock(_browseBtn, Dock.Right);
        topBar.Children.Add(titleLbl);
        topBar.Children.Add(windowControlsPanel);
        topBar.Children.Add(EngineInfoText);
        topBar.Children.Add(_langBox);
        topBar.Children.Add(_themeBox);
        topBar.Children.Add(_saveAllBtn);
        topBar.Children.Add(_resetBtn);
        topBar.Children.Add(_browseBtn);
        topBar.Children.Add(GamePathBox);

        _topBarContainer = new Border
        {
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = topBar
        };
        _topBarContainer.PointerPressed += (_, e) =>
        {
            if (e.Source is Control src && (src is TextBox || src is Button || src is ComboBox || src.FindAncestorOfType<TextBox>() != null || src.FindAncestorOfType<Button>() != null || src.FindAncestorOfType<ComboBox>() != null))
                return;
            if (e.ClickCount == 2)
            {
                ToggleMaximizeRestore();
                e.Handled = true;
                return;
            }
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            {
                BeginMoveDrag(e);
            }
        };

        AssemblyTree.ItemTemplate = new FuncTreeDataTemplate<DnNode>((n, _) => new TextBlock { Text = n.Title, FontSize = 12.5 }, n => n.Children);
        AssemblyTree.SelectionChanged += (_, _) => {
            if (AssemblyTree.SelectedItem is not DnNode node) return;
            if (node.Il2CppType != null)
            {
                CodeViewerBox.Text = node.Il2CppType.ToPseudoCSharp(node.Il2CppMember);
                _statusKey = "";
                StatusText.Text = $"IL2CPP: {node.Il2CppType.FullName}";
                return;
            }
            if (string.IsNullOrEmpty(node.DllPath)) return;
            _activeDll = node.DllPath;
            try
            {
                var dec = GetDecompiler(node.DllPath);
                CodeViewerBox.Text = node.Member != null ? dec.DecompileAsString(node.Member.MetadataToken) : (node.TypeDef != null ? dec.DecompileAsString(node.TypeDef.MetadataToken) : "");
                AppState.Current.LastDllName = Path.GetFileName(node.DllPath);
                AppState.Current.LastTypeName = node.FullTypeName;
                AppState.Current.LastMethodName = node.Member?.Name ?? "";
                AppState.Save();
            }
            catch (Exception ex) { SetStatus("status.error", ex.Message); }
        };

        _editMethodMenuItem = new MenuItem { Header = Loc.T("main.menu.edit_method") };
        _editMethodMenuItem.Click += (_, _) => { if (AssemblyTree.SelectedItem is DnNode n) OpenCodeEditorDialog(n, true); };
        _editClassMenuItem = new MenuItem { Header = Loc.T("main.menu.edit_class") };
        _editClassMenuItem.Click += (_, _) => { if (AssemblyTree.SelectedItem is DnNode n) OpenCodeEditorDialog(n, false); };
        _stubMethodMenuItem = new MenuItem { Header = Loc.T("main.menu.stub_method") };
        _stubMethodMenuItem.Click += (_, _) => { if (AssemblyTree.SelectedItem is DnNode n && n.Member != null) { var (ok, msg) = DllPatcher.StubMethod(n.DllPath, n.FullTypeName, n.Member.Name); _statusKey = ""; StatusText.Text = msg; if (ok) { _decompilers.Remove(n.DllPath); CodeViewerBox.Text = GetDecompiler(n.DllPath).DecompileAsString(nodeOrMemberToken(n)); } } };
        var dnMenu = new ContextMenu(); dnMenu.Items.Add(_editMethodMenuItem); dnMenu.Items.Add(_editClassMenuItem); dnMenu.Items.Add(new Separator()); dnMenu.Items.Add(_stubMethodMenuItem); AssemblyTree.ContextMenu = dnMenu; CodeViewerBox.ContextMenu = dnMenu;

        TreeSearchBox.TextChanged += (_, _) => RebuildTree(TreeSearchBox.Text ?? "");
        _bDec = new Button { Content = Loc.T("main.btn.load_assemblies"), CornerRadius = new CornerRadius(6) };
        _bDec.Click += (_, _) => { _decompilers.Clear(); RebuildTree(TreeSearchBox.Text ?? ""); };
        _bFind = new Button { Content = Loc.T("main.btn.find"), CornerRadius = new CornerRadius(6) };
        _bFind.Click += (_, _) => { CodeViewerBox.Focus(); AvaloniaEdit.Search.SearchPanel.Install(CodeViewerBox).Open(); };
        _bCmp = new Button { Content = Loc.T("main.btn.compile_dll"), CornerRadius = new CornerRadius(6) };
        _bCmp.Click += OnCompileDllClick;
        _bSav = new Button { Content = Loc.T("main.btn.export_cs"), CornerRadius = new CornerRadius(6) };
        _bSav.Click += async (_, _) => { var s = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { SuggestedFileName = "Exported.cs", DefaultExtension = "cs" }); if (s != null) await File.WriteAllTextAsync(s.Path.LocalPath, CodeViewerBox.Text ?? ""); };
        var leftPanel = new DockPanel { Width = 330 }; DockPanel.SetDock(TreeSearchBox, Dock.Top); leftPanel.Children.Add(TreeSearchBox); leftPanel.Children.Add(AssemblyTree);
        _bAddDll = new Button { Content = Loc.T("main.btn.add_dll"), CornerRadius = new CornerRadius(6) };
        _bAddDll.Click += async (_, _) => {
            var fs = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = Loc.T("dialog.select_assemblies.title"),
                AllowMultiple = true,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType(Loc.T("dialog.select_assemblies.filter")) { Patterns = new[] { "*" } }
                }
            });
            if (fs.Count == 0) return;
            foreach (var f in fs)
            {
                string p = f.Path.LocalPath;
                if (!_customDlls.Contains(p, StringComparer.OrdinalIgnoreCase)) _customDlls.Add(p);
                _decompilers.Remove(p);
            }
            RebuildTree(TreeSearchBox.Text ?? "");
        };
        var codeBar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 8) };
        codeBar.Children.Add(LangVersionBox); codeBar.Children.Add(_bAddDll); codeBar.Children.Add(_bDec); codeBar.Children.Add(_bFind); codeBar.Children.Add(_bCmp); codeBar.Children.Add(_bSav);
        CodeViewerBox.Margin = new Thickness(10, 0, 0, 0);
        var tab1 = new DockPanel(); DockPanel.SetDock(codeBar, Dock.Top); DockPanel.SetDock(leftPanel, Dock.Left);
        tab1.Children.Add(codeBar); tab1.Children.Add(leftPanel); tab1.Children.Add(new Grid { Children = { CodeViewerBox } });

        AssetFileBox.SelectionChanged += (_, _) => { if (AssetFileBox.SelectedItem != null) OnLoadAssetsClick(null, new RoutedEventArgs()); };
        _bLoad = new Button { Content = Loc.T("main.btn.refresh"), CornerRadius = new CornerRadius(6) };
        _bLoad.Click += OnLoadAssetsClick;
        _bExp = new Button { Content = Loc.T("main.btn.extract_asset"), CornerRadius = new CornerRadius(6) };
        _bExp.Click += async (_, _) => { if (AssetFileBox.SelectedItem is string fn && AssetsGrid.SelectedItem is AssetItemRow r) { _statusKey = ""; StatusText.Text = await AssetEditorHelper.ExportAsset(this, _assetsManager, Path.Combine(_dataDir, ResolveRealFile(fn)), r.PathId, r.Name.TrimStart('★', ' '), r.TypeName == "Texture2D" ? "png" : (r.TypeName == "TextAsset" ? "txt" : "raw")); } else SetStatus("status.select_asset"); };
        _bEdt = new Button { Content = Loc.T("main.btn.edit_text"), CornerRadius = new CornerRadius(6) };
        _bEdt.Click += async (_, _) => { if (AssetFileBox.SelectedItem is string fn && AssetsGrid.SelectedItem is AssetItemRow r) { if (r.TypeName != "TextAsset") { SetStatus("status.unavailable_for_type", r.TypeName); return; } _statusKey = ""; StatusText.Text = await AssetEditorHelper.EditTextAssetDialog(this, _assetsManager, Path.Combine(_dataDir, ResolveRealFile(fn)), r.PathId, r.Name.TrimStart('★', ' ')); MarkModified(fn, r.PathId, r.Name.TrimStart('★', ' ')); OnLoadAssetsClick(null, new RoutedEventArgs()); } else SetStatus("status.select_text_asset"); };
        _bRep = new Button { Content = Loc.T("main.btn.replace_file"), CornerRadius = new CornerRadius(6) };
        _bRep.Click += async (_, _) => { if (AssetFileBox.SelectedItem is string fn && AssetsGrid.SelectedItem is AssetItemRow r) { if (r.TypeName is not ("Texture2D" or "TextAsset")) { SetStatus("status.no_replacement_for_type", r.TypeName); return; } _statusKey = ""; StatusText.Text = await AssetEditorHelper.ReplaceAssetFromFile(this, _assetsManager, Path.Combine(_dataDir, ResolveRealFile(fn)), r.PathId, r.TypeName); MarkModified(fn, r.PathId, r.Name.TrimStart('★', ' ')); OnLoadAssetsClick(null, new RoutedEventArgs()); } else SetStatus("status.select_asset"); };
        _bNextMod = new Button { Content = string.Format(Loc.T("main.btn.modified_count"), 0), CornerRadius = new CornerRadius(6) };
        _bNextMod.Click += OnNextModifiedClick;
        AssetSearchBox.TextChanged += (_, _) => { string q = AssetSearchBox.Text ?? ""; AssetsGrid.ItemsSource = string.IsNullOrWhiteSpace(q) ? _allAssets : _allAssets.Where(a => a.Name.Contains(q, StringComparison.OrdinalIgnoreCase) || a.TypeName.Contains(q, StringComparison.OrdinalIgnoreCase) || a.Details.Contains(q, StringComparison.OrdinalIgnoreCase)).ToList(); };
        _colPathId = new DataGridTextColumn { Header = Loc.T("main.grid.path_id"), Binding = new Avalonia.Data.Binding("PathId"), Width = new DataGridLength(110) };
        _colName = new DataGridTextColumn { Header = Loc.T("main.grid.name"), Binding = new Avalonia.Data.Binding("Name"), Width = new DataGridLength(260) };
        _colType = new DataGridTextColumn { Header = Loc.T("main.grid.type"), Binding = new Avalonia.Data.Binding("TypeName"), Width = new DataGridLength(140) };
        _colDetails = new DataGridTextColumn { Header = Loc.T("main.grid.details"), Binding = new Avalonia.Data.Binding("Details"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) };
        AssetsGrid.Columns.Add(_colPathId);
        AssetsGrid.Columns.Add(_colName);
        AssetsGrid.Columns.Add(_colType);
        AssetsGrid.Columns.Add(_colDetails);
        var assetBar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 8) };
        assetBar.Children.Add(AssetFileBox); assetBar.Children.Add(AssetSearchBox); assetBar.Children.Add(_bLoad); assetBar.Children.Add(_bExp); assetBar.Children.Add(_bEdt); assetBar.Children.Add(_bRep); assetBar.Children.Add(_bNextMod);
        var tab2 = new DockPanel(); DockPanel.SetDock(assetBar, Dock.Top); tab2.Children.Add(assetBar); tab2.Children.Add(AssetsGrid);

        _bRip = new Button { Content = Loc.T("main.btn.run_ripper"), CornerRadius = new CornerRadius(6) };
        _bRip.Click += OnRunRipperClick;
        var tab3 = new StackPanel { Spacing = 10, Margin = new Thickness(12) }; tab3.Children.Add(RipOutputBox); tab3.Children.Add(_bRip);

        _tabCode = new TabItem { Header = Loc.T("main.tab.code"), Content = tab1 };
        _tabAssets = new TabItem { Header = Loc.T("main.tab.assets"), Content = tab2 };
        _tabExport = new TabItem { Header = Loc.T("main.tab.export"), Content = tab3 };
        _tabDumper = new TabItem { Header = Loc.T("main.tab.il2cpp_dumper"), Content = Cpp2ILTab.Build(this, () => _dataDir, () => EngineInfoText.Text?.Split('•').FirstOrDefault()?.Replace("Unity", "").Trim() ?? "", dlls => { foreach (var d in dlls) if (!_customDlls.Contains(d, StringComparer.OrdinalIgnoreCase)) _customDlls.Add(d); RebuildTree(TreeSearchBox.Text ?? ""); }, s => { _statusKey = ""; StatusText.Text = s; }) };
        _tabAbout = new TabItem { Header = Loc.T("main.tab.about"), Content = CreditsTab.Build(this, s => { _statusKey = ""; StatusText.Text = s; }) };

        var tabs = new TabControl { Margin = new Thickness(12, 6, 12, 8) };
        tabs.Items.Add(_tabCode);
        tabs.Items.Add(_tabAssets);
        tabs.Items.Add(_tabExport);
        tabs.Items.Add(_tabDumper);
        tabs.Items.Add(_tabAbout);

        _statusBarContainer = new Border
        {
            Padding = new Thickness(12, 6),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Child = StatusText
        };
        var root = new DockPanel();
        DockPanel.SetDock(_topBarContainer, Dock.Top);
        DockPanel.SetDock(_statusBarContainer, Dock.Bottom);
        root.Children.Add(_topBarContainer);
        root.Children.Add(_statusBarContainer);
        root.Children.Add(tabs);
        _windowFrameBorder = new Border
        {
            BorderThickness = new Thickness(1),
            Child = root
        };
        Content = _windowFrameBorder;
    }

    private static System.Reflection.Metadata.EntityHandle nodeOrMemberToken(DnNode n) => n.Member!.MetadataToken;

    private void ResetProject(bool clear = true)
    {
        _decompilers.Clear(); _assetsManager.UnloadAllAssetsFiles(); _allAssets.Clear(); _modifiedAssets.Clear(); _modCursor = -1; _activeDll = "";
        if (_bNextMod != null) _bNextMod.Content = string.Format(Loc.T("main.btn.modified_count"), 0);
        AssemblyTree.ItemsSource = null; CodeViewerBox.Text = ""; AssetsGrid.ItemsSource = null; AssetFileBox.ItemsSource = null; AssetSearchBox.Text = ""; TreeSearchBox.Text = "";
        _engineInfoKey = "";
        EngineInfoText.Text = "";
        if (clear)
        {
            _customDlls.Clear(); _dataDir = ""; GamePathBox.Text = "";
            AppState.Current.LastGamePath = ""; AppState.Current.LastDllName = ""; AppState.Current.LastTypeName = ""; AppState.Current.LastMethodName = "";
            AppState.Save();
            SetStatus("status.project_cleared");
        }
    }

    private void OnExportAllClick(object? s, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_dataDir) || !Directory.Exists(_dataDir)) { SetStatus("status.select_game_first"); return; }
        _decompilers.Clear(); _assetsManager.UnloadAllAssetsFiles();
        var mod = Directory.GetFiles(_dataDir, "*.bak", SearchOption.AllDirectories).Select(f => Path.GetFileName(f[..^4])).Distinct().ToList();
        if (mod.Count > 0)
            SetStatus("status.modified_files", string.Join(", ", mod));
        else
            SetStatus("status.all_synced");
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
            var usings = fullClassCode.Split('\n').Where(l => l.TrimStart().StartsWith("using ") && l.TrimEnd().EndsWith(";")).Select(l => l.TrimEnd());
            string methodCode = string.Join("\n", dec.DecompileAsString(node.Member.MetadataToken).Split('\n').Where(l => !l.TrimStart().StartsWith("using ") || !l.TrimEnd().EndsWith(";"))).TrimEnd();
            string indented = string.Join("\n", methodCode.Split('\n').Select(l => "\t" + l)), ns = node.TypeDef.Namespace, cls = node.TypeDef.Name;
            editorCode = string.IsNullOrEmpty(ns) ? $"{string.Join("\n", usings)}\n\npublic partial class {cls}\n{{\n{indented}\n}}" : $"{string.Join("\n", usings)}\n\nnamespace {ns}\n{{\n\tpublic partial class {cls}\n\t{{\n{indented}\n\t}}\n}}";
        }
        else editorCode = fullClassCode;
        var editor = CodeEditorHelper.CreateCSharpEditor(isReadOnly: false, getDecompilers: () => _decompilers.Values);
        editor.Text = editorCode;
        var errBox = new TextBox { Height = 115, IsReadOnly = true, FontFamily = new FontFamily("Consolas"), FontSize = 12, Foreground = Brushes.IndianRed, CornerRadius = new CornerRadius(6) };
        var bCmp = new Button { Content = Loc.T("dialog.editor.compile"), Padding = new Thickness(18, 6), CornerRadius = new CornerRadius(6) };
        var bCan = new Button { Content = Loc.T("common.cancel"), Padding = new Thickness(14, 6), CornerRadius = new CornerRadius(6) };
        var win = new Window { Title = string.Format(Loc.T("dialog.editor.title"), targetType), Width = 920, Height = 680, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        void OnEditorLangChanged()
        {
            bCmp.Content = Loc.T("dialog.editor.compile");
            bCan.Content = Loc.T("common.cancel");
            win.Title = string.Format(Loc.T("dialog.editor.title"), targetType);
        }
        Loc.LanguageChanged += OnEditorLangChanged;
        win.Closed += (_, _) => Loc.LanguageChanged -= OnEditorLangChanged;
        bCmp.Click += (_, _) => { var (ok, msg) = DllPatcher.CompileAndInject(node.DllPath, editor.Text ?? "", targetType, targetMethod, fullClassCode); if (!ok) { errBox.Text = msg; return; } _statusKey = ""; StatusText.Text = msg; _decompilers.Remove(node.DllPath); CodeViewerBox.Text = GetDecompiler(node.DllPath).DecompileAsString(node.Member != null ? node.Member.MetadataToken : node.TypeDef.MetadataToken); win.Close(); };
        bCan.Click += (_, _) => win.Close();
        var btns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Margin = new Thickness(10, 8) }; btns.Children.Add(bCmp); btns.Children.Add(bCan);
        var bot = new StackPanel(); bot.Children.Add(new Border { Margin = new Thickness(10, 4), Child = errBox }); bot.Children.Add(btns);
        var lay = new DockPanel(); DockPanel.SetDock(bot, Dock.Bottom); lay.Children.Add(bot); lay.Children.Add(new Grid { Children = { editor } });
        win.Content = lay; await win.ShowDialog(this);
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
        var metaSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string mDir = string.IsNullOrEmpty(_dataDir) ? "" : Path.Combine(_dataDir, "Managed");
        if (Directory.Exists(mDir))
        {
            var mDlls = Directory.GetFiles(mDir, "Assembly-*.dll");
            if (mDlls.Length == 0) mDlls = Directory.GetFiles(mDir, "*.dll").Take(25).ToArray();
            foreach (var d in mDlls) dllSet.Add(d);
        }
        string metaDir = string.IsNullOrEmpty(_dataDir) ? "" : Path.Combine(_dataDir, "il2cpp_data", "Metadata");
        if (Directory.Exists(metaDir))
        {
            foreach (var mf in Directory.GetFiles(metaDir, "global-metadata*")) metaSet.Add(mf);
        }
        foreach (var f in _customDlls.Where(File.Exists))
        {
            if (f.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            {
                if (!Path.GetFileName(f).Equals("GameAssembly.dll", StringComparison.OrdinalIgnoreCase)) dllSet.Add(f);
            }
            else if (!f.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                metaSet.Add(f);
            }
        }
        if (dllSet.Count == 0 && metaSet.Count == 0)
        {
            AssemblyTree.ItemsSource = null;
            SetStatus("status.assemblies_not_found");
            return;
        }
        var rootNodes = new List<DnNode>();
        string lastStatus = "";
        foreach (var dll in dllSet)
        {
            try
            {
                var dec = GetDecompiler(dll);
                var asmNode = new DnNode { Title = Path.GetFileName(dll), DllPath = dll };
                foreach (var ns in dec.TypeSystem.MainModule.TypeDefinitions.Where(t => !t.Name.StartsWith("<") && t.DeclaringType == null).GroupBy(t => string.IsNullOrEmpty(t.Namespace) ? "-" : t.Namespace).OrderBy(g => g.Key))
                {
                    var nsNode = new DnNode { Title = ns.Key, DllPath = dll };
                    foreach (var t in ns.OrderBy(x => x.Name))
                    {
                        bool tMatch = string.IsNullOrWhiteSpace(filter) || t.Name.Contains(filter, StringComparison.OrdinalIgnoreCase);
                        var tNode = new DnNode { Title = t.Name, DllPath = dll, FullTypeName = t.FullName, TypeDef = t };
                        foreach (var m in t.Members.Where(x => !x.Name.StartsWith("<")))
                            if (tMatch || m.Name.Contains(filter, StringComparison.OrdinalIgnoreCase))
                                tNode.Children.Add(new DnNode { Title = m is IMethod ? $"{m.Name}()" : m.Name, DllPath = dll, FullTypeName = t.FullName, TypeDef = t, Member = m });
                        if (tMatch || tNode.Children.Count > 0) nsNode.Children.Add(tNode);
                    }
                    if (nsNode.Children.Count > 0) asmNode.Children.Add(nsNode);
                }
                rootNodes.Add(asmNode);
            }
            catch (Exception ex) { lastStatus = string.Format(Loc.T("status.dll_error"), Path.GetFileName(dll), ex.Message); }
        }
        foreach (var mf in metaSet)
        {
            var (ok, info, types) = Il2CppMetadataParser.Parse(mf);
            lastStatus = info;
            if (!ok) { _engineInfoKey = ""; EngineInfoText.Text = info; continue; }
            _engineInfoKey = "";
            EngineInfoText.Text = info;
            var metaNode = new DnNode { Title = Path.GetFileName(mf), DllPath = mf };
            foreach (var ns in types.Where(t => !t.Name.StartsWith("<")).GroupBy(t => t.Namespace).OrderBy(g => g.Key))
            {
                var nsNode = new DnNode { Title = ns.Key };
                foreach (var t in ns.OrderBy(x => x.Name))
                {
                    bool tMatch = string.IsNullOrWhiteSpace(filter) || t.FullName.Contains(filter, StringComparison.OrdinalIgnoreCase);
                    var mList = t.Methods.Where(m => !m.StartsWith("<") && (tMatch || m.Contains(filter, StringComparison.OrdinalIgnoreCase))).ToList();
                    var fList = t.Fields.Where(f => !f.StartsWith("<") && (tMatch || f.Contains(filter, StringComparison.OrdinalIgnoreCase))).ToList();
                    if (!tMatch && mList.Count == 0 && fList.Count == 0) continue;
                    var tNode = new DnNode { Title = t.Name, FullTypeName = t.FullName, Il2CppType = t };
                    foreach (var f in fList) tNode.Children.Add(new DnNode { Title = $"[F] {f}", FullTypeName = t.FullName, Il2CppType = t, Il2CppMember = f });
                    foreach (var m in mList) tNode.Children.Add(new DnNode { Title = $"{m}()", FullTypeName = t.FullName, Il2CppType = t, Il2CppMember = m });
                    nsNode.Children.Add(tNode);
                }
                if (nsNode.Children.Count > 0) metaNode.Children.Add(nsNode);
            }
            if (metaNode.Children.Count > 0) rootNodes.Add(metaNode);
        }
        AssemblyTree.ItemsSource = rootNodes;
        if (rootNodes.Count > 0)
            SetStatus("status.modules_loaded", rootNodes.Count, lastStatus);
        else if (string.IsNullOrEmpty(lastStatus))
            SetStatus("status.modules_not_loaded");
        else
        {
            _statusKey = "";
            StatusText.Text = lastStatus;
        }
    }

    private async void OnBrowseClick(object? s, RoutedEventArgs e)
    {
        var fs = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = Loc.T("dialog.select_game_folder.title"),
            AllowMultiple = false
        });
        if (fs.Count > 0) Scan(fs[0].Path.LocalPath.TrimEnd('\\', '/'));
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
            string allTag = Loc.T("common.all");
            foreach (var f in Directory.GetFiles(_dataDir))
            {
                string ext = Path.GetExtension(f).ToLowerInvariant(), nm = Path.GetFileName(f), lnm = nm.ToLowerInvariant();
                if (ext is ".unity3d" or ".bundle" || lnm == "data.unity3d")
                {
                    files.Add($"{nm} [{allTag}]");
                    try { var bun = _assetsManager.LoadBundleFile(f, true); foreach (var di in bun.file.BlockAndDirInfo.DirectoryInfos) if (!di.Name.EndsWith(".resS", StringComparison.OrdinalIgnoreCase) && !di.Name.EndsWith(".resource", StringComparison.OrdinalIgnoreCase)) files.Add($"{nm} -> {di.Name}"); } catch { }
                }
                else if (ext == ".assets" || lnm is "maindata" or "globalgamemanagers" || (string.IsNullOrEmpty(ext) && lnm.StartsWith("level"))) files.Add(nm);
            }
            AssetFileBox.ItemsSource = files; if (files.Count > 0) AssetFileBox.SelectedIndex = 0;
            string uVer = "";
            foreach (var pr in new[] { "globalgamemanagers", "mainData", "data.unity3d" })
            {
                string pf = Path.Combine(_dataDir, pr); if (!File.Exists(pf)) continue;
                try { if (pr == "data.unity3d") { var b = _assetsManager.LoadBundleFile(pf, true); for (int i = 0; i < b.file.BlockAndDirInfo.DirectoryInfos.Count; i++) { try { var a = _assetsManager.LoadAssetsFileFromBundle(b, i, true); uVer = a.file.Metadata.UnityVersion; if (!string.IsNullOrWhiteSpace(uVer) && uVer != "0.0.0") break; } catch { } } } else { var a = _assetsManager.LoadAssetsFile(pf, true); uVer = a.file.Metadata.UnityVersion; } if (!string.IsNullOrWhiteSpace(uVer) && uVer != "0.0.0") break; } catch { }
            }
            string gameRoot = Path.GetDirectoryName(_dataDir) ?? _dataDir;
            bool il2 = File.Exists(Path.Combine(_dataDir, "il2cpp_data", "Metadata", "global-metadata.dat")) || File.Exists(Path.Combine(gameRoot, "GameAssembly.dll"));
            string verDisplay = string.IsNullOrWhiteSpace(uVer) || uVer == "0.0.0" ? Loc.T("engine.version_unknown") : uVer;
            SetEngineInfo("engine.info_format", verDisplay, il2 ? "IL2CPP" : "Mono");
            RebuildTree("");
            if (files.Count > 0) OnLoadAssetsClick(null, new RoutedEventArgs());
        }
        catch (Exception ex) { SetStatus("status.scan_error", ex.Message); }
    }

    private void OnCompileDllClick(object? s, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_activeDll) || string.IsNullOrEmpty(_editTargetType)) { SetStatus("status.rmb_hint"); return; }
        var (ok, msg) = DllPatcher.CompileAndInject(_activeDll, CodeViewerBox.Text ?? "", _editTargetType, _editTargetMethod);
        _statusKey = "";
        StatusText.Text = msg;
        if (ok) _decompilers.Remove(_activeDll);
    }

    private void OnLoadAssetsClick(object? s, RoutedEventArgs e)
    {
        if (AssetFileBox.SelectedItem is not string sel) return;
        string realFile = ResolveRealFile(sel), full = Path.Combine(_dataDir, realFile);
        int arrowIdx = sel.IndexOf(" -> ", StringComparison.Ordinal); string? subFilter = arrowIdx >= 0 ? sel[(arrowIdx + 4)..] : null;
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
            else { var ins = _assetsManager.LoadAssetsFile(full, true); try { _assetsManager.LoadClassDatabaseFromPackage(ins.file.Metadata.UnityVersion); } catch { } targets.Add((realFile, ins)); }
            string bytesUnit = Loc.T("common.bytes");
            foreach (var (subName, inst) in targets)
            {
                foreach (var inf in inst.file.AssetInfos.Take(8000))
                {
                    var t = (AssetClassID)inf.TypeId; string n = $"Asset_{inf.PathId}", d = isBun ? $"[{subName}] {inf.ByteSize} {bytesUnit}" : $"{inf.ByteSize} {bytesUnit}";
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
            _allAssets = rows; AssetsGrid.ItemsSource = _allAssets; SetStatus("status.assets_loaded", sel, _allAssets.Count);
        }
        catch
        {
            try
            {
                string bytesUnit = Loc.T("common.bytes");
                _allAssets = LegacyUnityAssets.ReadEntries(full).Select(x => new AssetItemRow(x.PathId, x.Name, x.TypeName, $"{x.DataSize} {bytesUnit} (offset: 0x{x.DataOffset:X})")).ToList();
                AssetsGrid.ItemsSource = _allAssets;
                SetStatus("status.assets_loaded_legacy", _allAssets.Count);
            }
            catch (Exception ex) { SetStatus("status.read_error", realFile, ex.Message); }
        }
    }

    private void OnRunRipperClick(object? s, RoutedEventArgs e)
    {
        string exe = Path.Combine(AppContext.BaseDirectory, "Tools", "AssetRipper", "AssetRipper.exe");
        if (!File.Exists(exe) || string.IsNullOrEmpty(_dataDir)) { SetStatus("status.ripper_missing", Path.GetDirectoryName(exe) ?? ""); return; }
        string outD = string.IsNullOrWhiteSpace(RipOutputBox.Text) ? Path.Combine(Path.GetDirectoryName(_dataDir)!, "Ripped") : RipOutputBox.Text;
        Process.Start(new ProcessStartInfo { FileName = exe, Arguments = $"\"{_dataDir}\" -o \"{outD}\"", UseShellExecute = true });
    }
}
