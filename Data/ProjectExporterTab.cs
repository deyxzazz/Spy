using AssetRipper.Primitives;
using AssetsTools.NET;
using AssetsTools.NET.Extra;
using AssetsTools.NET.Texture;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Cpp2IL.Core;
using Cpp2IL.Core.Api;
using Cpp2IL.Core.InstructionSets;
using Cpp2IL.Core.Logging;
using Cpp2IL.Core.OutputFormats;
using Cpp2IL.Core.ProcessingLayers;
using ICSharpCode.Decompiler;
using ICSharpCode.Decompiler.CSharp;
using ICSharpCode.Decompiler.TypeSystem;
using LibCpp2IL;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using DecompilerLang = ICSharpCode.Decompiler.CSharp.LanguageVersion;

namespace SpyTool2
{
    public static class ProjectExporterTab
    {
        private static Action<string>? _activeLogSink;
        private static bool _cpp2IlInitialized;
        private static readonly object _initLock = new();

        private static readonly string[] ExcludedSystemPrefixes =
        {
            "System",
            "UnityEngine",
            "Unity.",
            "Mono.",
            "Microsoft.",
            "mscorlib",
            "netstandard",
            "Newtonsoft.",
            "__Generated"
        };

        private static void EnsureCpp2IlSetup(Action<string> appendLog)
        {
            _activeLogSink = appendLog;

            lock (_initLock)
            {
                if (_cpp2IlInitialized)
                    return;

                Logger.InfoLog += (message, source) =>
                {
                    var text = string.IsNullOrWhiteSpace(source) ? message : $"[{source}] {message}";
                    _activeLogSink?.Invoke(text?.TrimEnd() ?? string.Empty);
                };

                Logger.WarningLog += (message, source) =>
                {
                    var text = string.IsNullOrWhiteSpace(source) ? $"[WARN] {message}" : $"[WARN][{source}] {message}";
                    _activeLogSink?.Invoke(text?.TrimEnd() ?? string.Empty);
                };

                Logger.ErrorLog += (message, source) =>
                {
                    var text = string.IsNullOrWhiteSpace(source) ? $"[ERR] {message}" : $"[ERR][{source}] {message}";
                    _activeLogSink?.Invoke(text?.TrimEnd() ?? string.Empty);
                };

                Logger.VerboseLog += (_, _) => { };

                try
                {
                    var loggerType = typeof(Logger);
                    foreach (var field in loggerType.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
                    {
                        if (field.GetValue(null) == null && typeof(Delegate).IsAssignableFrom(field.FieldType))
                        {
                            var invoke = field.FieldType.GetMethod("Invoke");
                            if (invoke != null && invoke.ReturnType == typeof(void))
                            {
                                var parameters = invoke.GetParameters()
                                    .Select(p => Expression.Parameter(p.ParameterType, p.Name))
                                    .ToArray();
                                var lambda = Expression.Lambda(field.FieldType, Expression.Empty(), parameters);
                                field.SetValue(null, lambda.Compile());
                            }
                        }
                    }
                }
                catch
                {
                    // Игнорируем
                }

                InstructionSetRegistry.RegisterInstructionSet<X86InstructionSet>(DefaultInstructionSets.X86_32);
                InstructionSetRegistry.RegisterInstructionSet<X86InstructionSet>(DefaultInstructionSets.X86_64);

                _cpp2IlInitialized = true;
            }
        }

        public static Control Build(
            Window owner,
            Func<string> getDataDir,
            Func<string> getVer,
            Action<IEnumerable<string>> onLoadDllsToCodeTab,
            Action<string> setStatus)
        {
            var outBox = new TextBox
            {
                Watermark = Loc.T("exp.watermark.out"),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                CornerRadius = new CornerRadius(6)
            };

            var btnBrowseOut = new Button
            {
                Content = Loc.T("exp.btn.browse"),
                CornerRadius = new CornerRadius(6)
            };

            var btnOpenOut = new Button
            {
                Content = Loc.T("exp.btn.open_out"),
                CornerRadius = new CornerRadius(6)
            };

            var btnRun = new Button
            {
                Content = Loc.T("exp.btn.run"),
                FontWeight = FontWeight.SemiBold,
                CornerRadius = new CornerRadius(6)
            };

            var filterBox = new ComboBox
            {
                Width = 320,
                SelectedIndex = 0,
                CornerRadius = new CornerRadius(6),
                ItemsSource = GetFilterItems()
            };

            var chkDecompileCs = new CheckBox
            {
                Content = Loc.T("exp.chk.decompile_cs"),
                IsChecked = true
            };

            var chkSaveDummyDll = new CheckBox
            {
                Content = Loc.T("exp.chk.save_dummydll"),
                IsChecked = true
            };

            var chkAutoLoadCode = new CheckBox
            {
                Content = Loc.T("exp.chk.autoload_code"),
                IsChecked = true
            };

            var chkTextures = new CheckBox
            {
                Content = Loc.T("exp.chk.textures"),
                IsChecked = true
            };

            var chkTextAssets = new CheckBox
            {
                Content = Loc.T("exp.chk.text_assets"),
                IsChecked = true
            };

            var chkRawAssets = new CheckBox
            {
                Content = Loc.T("exp.chk.raw_assets"),
                IsChecked = false
            };

            var logBox = new TextBox
            {
                IsReadOnly = true,
                AcceptsReturn = true,
                TextWrapping = TextWrapping.NoWrap,
                FontFamily = new FontFamily("Consolas, Cascadia Code, monospace"),
                FontSize = 12,
                MinHeight = 250,
                VerticalAlignment = VerticalAlignment.Stretch,
                CornerRadius = new CornerRadius(6)
            };

            void AppendLog(string message)
            {
                if (string.IsNullOrWhiteSpace(message))
                    return;

                var line = $"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}";
                Dispatcher.UIThread.Post(() =>
                {
                    logBox.Text = (logBox.Text ?? string.Empty) + line;
                    logBox.CaretIndex = logBox.Text.Length;
                });
            }

            string ResolveDefaultOutputDir(string dataDir)
            {
                var gameRoot = Directory.GetParent(dataDir)?.FullName ?? dataDir;
                return Path.Combine(gameRoot, "ExportedProject");
            }

            btnBrowseOut.Click += async (_, _) =>
            {
                var dirs = await owner.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
                {
                    Title = Loc.T("exp.watermark.out"),
                    AllowMultiple = false
                });
                if (dirs.Count > 0)
                    outBox.Text = dirs[0].Path.LocalPath;
            };

            btnOpenOut.Click += (_, _) =>
            {
                var targetDir = outBox.Text?.Trim();
                if (string.IsNullOrWhiteSpace(targetDir))
                {
                    var dataDir = getDataDir?.Invoke() ?? string.Empty;
                    if (!string.IsNullOrWhiteSpace(dataDir) && Directory.Exists(dataDir))
                        targetDir = ResolveDefaultOutputDir(dataDir);
                }

                if (string.IsNullOrWhiteSpace(targetDir) || !Directory.Exists(targetDir))
                {
                    AppendLog(Loc.T("exp.log.out_not_found"));
                    return;
                }

                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = targetDir,
                        UseShellExecute = true
                    });
                }
                catch (Exception ex)
                {
                    AppendLog($"[WARN] {ex.Message}");
                }
            };

            btnRun.Click += async (_, _) =>
            {
                var dataDir = getDataDir?.Invoke() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(dataDir) || !Directory.Exists(dataDir))
                {
                    setStatus?.Invoke(Loc.T("exp.status.no_game"));
                    AppendLog(Loc.T("exp.status.no_game"));
                    return;
                }

                var outDir = outBox.Text?.Trim() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(outDir))
                {
                    outDir = ResolveDefaultOutputDir(dataDir);
                    outBox.Text = outDir;
                }

                int filterIndex = filterBox.SelectedIndex;
                bool doDecompileCs = chkDecompileCs.IsChecked == true;
                bool doSaveDummy = chkSaveDummyDll.IsChecked == true;
                bool doAutoLoad = chkAutoLoadCode.IsChecked == true;
                bool doTextures = chkTextures.IsChecked == true;
                bool doTextAssets = chkTextAssets.IsChecked == true;
                bool doRawAssets = chkRawAssets.IsChecked == true;

                btnRun.IsEnabled = false;
                logBox.Text = string.Empty;
                setStatus?.Invoke(Loc.T("exp.status.running"));

                var sw = Stopwatch.StartNew();
                int exportedScriptsCount = 0;
                int exportedAssetsCount = 0;
                var dllsToLoadInTree = new List<string>();
                string? tempDummyDirToCleanup = null;

                try
                {
                    AppendLog(string.Format(Loc.T("exp.log.start"), dataDir));
                    AppendLog(string.Format(Loc.T("exp.log.out_dir"), outDir));

                    await Task.Run(() =>
                    {
                        Directory.CreateDirectory(outDir);

                        var gameRoot = Directory.GetParent(dataDir)?.FullName ?? dataDir;
                        var managedDir = Path.Combine(dataDir, "Managed");

                        var gaPath = Path.Combine(gameRoot, "GameAssembly.dll");
                        if (!File.Exists(gaPath))
                        {
                            gaPath = Directory.GetFiles(gameRoot, "GameAssembly.dll", SearchOption.AllDirectories)
                                .FirstOrDefault() ?? string.Empty;
                        }

                        var metaPath = Path.Combine(dataDir, "il2cpp_data", "Metadata", "global-metadata.dat");
                        if (!File.Exists(metaPath))
                        {
                            metaPath = Directory.GetFiles(dataDir, "global-metadata.dat", SearchOption.AllDirectories)
                                .FirstOrDefault() ?? string.Empty;
                        }

                        bool isIl2Cpp = File.Exists(gaPath) && File.Exists(metaPath);

                        if (filterIndex != 3)
                        {
                            List<string> sourceDlls = new();

                            if (isIl2Cpp)
                            {
                                EnsureCpp2IlSetup(AppendLog);

                                string detectedVer = DetectUnityVersionString(gameRoot, dataDir, metaPath, getVer);
                                if (string.IsNullOrWhiteSpace(detectedVer))
                                    detectedVer = "2022.3.49";

                                AssetRipper.Primitives.UnityVersion uVersion = AssetRipper.Primitives.UnityVersion.Parse(detectedVer);
                                AppendLog(string.Format(Loc.T("exp.log.il2cpp_dump"), uVersion));

                                string targetDummyDir;
                                if (doSaveDummy)
                                {
                                    targetDummyDir = Path.Combine(outDir, "DummyDll");
                                }
                                else
                                {
                                    targetDummyDir = Path.Combine(Path.GetTempPath(), "SpyTool2_Export_" + Guid.NewGuid().ToString("N"));
                                    tempDummyDirToCleanup = targetDummyDir;
                                }

                                Directory.CreateDirectory(targetDummyDir);

                                Cpp2IlApi.InitializeLibCpp2Il(gaPath, metaPath, uVersion, false);
                                var ctx = Cpp2IlApi.CurrentAppContext
                                    ?? throw new InvalidOperationException("Cpp2IlApi.CurrentAppContext вернул null.");

                                new AttributeInjectorProcessingLayer().Process(ctx);
                                new AsmResolverDllOutputFormatEmpty().DoOutput(ctx, targetDummyDir);

                                sourceDlls = Directory.GetFiles(targetDummyDir, "*.dll", SearchOption.TopDirectoryOnly)
                                    .OrderBy(Path.GetFileName)
                                    .ToList();
                            }
                            else if (Directory.Exists(managedDir))
                            {
                                sourceDlls = Directory.GetFiles(managedDir, "*.dll", SearchOption.TopDirectoryOnly)
                                    .OrderBy(Path.GetFileName)
                                    .ToList();

                                AppendLog(string.Format(Loc.T("exp.log.mono_found"), sourceDlls.Count));

                                if (doSaveDummy && sourceDlls.Count > 0)
                                {
                                    var dummyCopyDir = Path.Combine(outDir, "DummyDll");
                                    Directory.CreateDirectory(dummyCopyDir);
                                    foreach (var dll in sourceDlls)
                                    {
                                        var dest = Path.Combine(dummyCopyDir, Path.GetFileName(dll));
                                        File.Copy(dll, dest, true);
                                    }
                                }
                            }

                            var selectedDlls = FilterAssemblies(sourceDlls, filterIndex);
                            var namesPreview = selectedDlls.Count > 0
                                ? string.Join(", ", selectedDlls.Select(Path.GetFileName))
                                : "-";

                            AppendLog(string.Format(Loc.T("exp.log.filtered_dlls"), selectedDlls.Count, namesPreview));

                            if (doAutoLoad)
                            {
                                if (doSaveDummy)
                                {
                                    var savedDummyDir = Path.Combine(outDir, "DummyDll");
                                    foreach (var dll in selectedDlls)
                                    {
                                        var savedPath = Path.Combine(savedDummyDir, Path.GetFileName(dll));
                                        dllsToLoadInTree.Add(File.Exists(savedPath) ? savedPath : dll);
                                    }
                                }
                                else
                                {
                                    dllsToLoadInTree.AddRange(selectedDlls);
                                }
                            }

                            if (doDecompileCs && selectedDlls.Count > 0)
                            {
                                var scriptsRoot = Path.Combine(outDir, "Assets", "Scripts");
                                Directory.CreateDirectory(scriptsRoot);

                                foreach (var dllPath in selectedDlls)
                                {
                                    string asmFileName = Path.GetFileName(dllPath);
                                    string asmFolderName = SanitizeFileName(Path.GetFileNameWithoutExtension(dllPath));
                                    AppendLog(string.Format(Loc.T("exp.log.decompiling"), asmFileName));

                                    string targetFolder = asmFolderName.Equals("Assembly-CSharp-firstpass", StringComparison.OrdinalIgnoreCase)
                                        ? Path.Combine(outDir, "Assets", "Plugins")
                                        : Path.Combine(scriptsRoot, asmFolderName);
                                    int csCount = DecompileAssemblyToFolder(dllPath, targetFolder, AppendLog);
                                    exportedScriptsCount += csCount;
                                    AppendLog(string.Format(Loc.T("exp.log.decompiled_ok"), asmFileName, csCount));
                                }
                            }
                        }

                        if (doTextures || doTextAssets || doRawAssets)
                        {
                            AppendLog(string.Format(Loc.T("exp.log.assets_scan"), dataDir));
                            var (texCount, txtCount, rawCount) = ExportAllAssets(
                                dataDir,
                                outDir,
                                doTextures,
                                doTextAssets,
                                doRawAssets,
                                AppendLog);

                            exportedAssetsCount = texCount + txtCount + rawCount;
                            AppendLog(string.Format(Loc.T("exp.log.assets_done"), texCount, txtCount, rawCount));
                        }
                    });

                    if (doAutoLoad && dllsToLoadInTree.Count > 0)
                    {
                        AppendLog(Loc.T("exp.log.autoload"));
                        onLoadDllsToCodeTab?.Invoke(dllsToLoadInTree);
                    }

                    sw.Stop();
                    AppendLog(string.Format(Loc.T("exp.log.done"), sw.Elapsed.TotalSeconds));
                    setStatus?.Invoke(string.Format(Loc.T("exp.status.done"), exportedScriptsCount, exportedAssetsCount));
                }
                catch (Exception ex)
                {
                    AppendLog($"[ERR] {ex}");
                    setStatus?.Invoke(string.Format(Loc.T("exp.status.err"), ex.Message));
                }
                finally
                {
                    if (!doAutoLoad && !string.IsNullOrEmpty(tempDummyDirToCleanup) && Directory.Exists(tempDummyDirToCleanup))
                    {
                        try { Directory.Delete(tempDummyDirToCleanup, true); } catch { }
                    }

                    btnRun.IsEnabled = true;
                }
            };

            var lblOut = new TextBlock
            {
                Text = Loc.T("exp.label.out"),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 10, 0)
            };

            var outRow = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto"),
                Margin = new Thickness(0, 0, 0, 10)
            };
            Grid.SetColumn(lblOut, 0);
            Grid.SetColumn(outBox, 1);
            outBox.Margin = new Thickness(0, 0, 8, 0);
            Grid.SetColumn(btnBrowseOut, 2);
            btnBrowseOut.Margin = new Thickness(0, 0, 8, 0);
            Grid.SetColumn(btnOpenOut, 3);

            outRow.Children.Add(lblOut);
            outRow.Children.Add(outBox);
            outRow.Children.Add(btnBrowseOut);
            outRow.Children.Add(btnOpenOut);

            var lblScriptsGroup = new TextBlock
            {
                Text = Loc.T("exp.group.scripts"),
                FontWeight = FontWeight.SemiBold,
                Margin = new Thickness(0, 0, 0, 6)
            };

            var lblFilter = new TextBlock
            {
                Text = Loc.T("exp.label.filter"),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0)
            };

            var filterRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 0, 0, 8)
            };
            filterRow.Children.Add(lblFilter);
            filterRow.Children.Add(filterBox);

            var scriptsChecks = new WrapPanel
            {
                Orientation = Orientation.Horizontal
            };
            chkDecompileCs.Margin = new Thickness(0, 0, 16, 4);
            chkSaveDummyDll.Margin = new Thickness(0, 0, 16, 4);
            chkAutoLoadCode.Margin = new Thickness(0, 0, 16, 4);
            scriptsChecks.Children.Add(chkDecompileCs);
            scriptsChecks.Children.Add(chkSaveDummyDll);
            scriptsChecks.Children.Add(chkAutoLoadCode);

            var scriptsGroupBox = new Border
            {
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(Color.FromArgb(60, 128, 128, 128)),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(10),
                Margin = new Thickness(0, 0, 0, 8),
                Child = new StackPanel
                {
                    Children = { lblScriptsGroup, filterRow, scriptsChecks }
                }
            };

            var lblAssetsGroup = new TextBlock
            {
                Text = Loc.T("exp.group.assets"),
                FontWeight = FontWeight.SemiBold,
                Margin = new Thickness(0, 0, 0, 6)
            };

            var assetsChecks = new WrapPanel
            {
                Orientation = Orientation.Horizontal
            };
            chkTextures.Margin = new Thickness(0, 0, 16, 4);
            chkTextAssets.Margin = new Thickness(0, 0, 16, 4);
            chkRawAssets.Margin = new Thickness(0, 0, 16, 4);
            assetsChecks.Children.Add(chkTextures);
            assetsChecks.Children.Add(chkTextAssets);
            assetsChecks.Children.Add(chkRawAssets);

            var assetsGroupBox = new Border
            {
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(Color.FromArgb(60, 128, 128, 128)),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(10),
                Margin = new Thickness(0, 0, 0, 10),
                Child = new StackPanel
                {
                    Children = { lblAssetsGroup, assetsChecks }
                }
            };

            var actionRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 10,
                Margin = new Thickness(0, 0, 0, 8)
            };
            actionRow.Children.Add(btnRun);

            var topContainer = new StackPanel();
            topContainer.Children.Add(outRow);
            topContainer.Children.Add(scriptsGroupBox);
            topContainer.Children.Add(assetsGroupBox);
            topContainer.Children.Add(actionRow);

            var root = new DockPanel
            {
                Margin = new Thickness(12),
                LastChildFill = true
            };
            DockPanel.SetDock(topContainer, Dock.Top);
            root.Children.Add(topContainer);
            root.Children.Add(logBox);

            void UpdateLocalization()
            {
                lblOut.Text = Loc.T("exp.label.out");
                outBox.Watermark = Loc.T("exp.watermark.out");
                btnBrowseOut.Content = Loc.T("exp.btn.browse");
                btnOpenOut.Content = Loc.T("exp.btn.open_out");
                btnRun.Content = Loc.T("exp.btn.run");

                lblScriptsGroup.Text = Loc.T("exp.group.scripts");
                lblFilter.Text = Loc.T("exp.label.filter");

                int prevFilterIdx = filterBox.SelectedIndex;
                filterBox.ItemsSource = GetFilterItems();
                filterBox.SelectedIndex = Math.Clamp(prevFilterIdx, 0, 3);

                chkDecompileCs.Content = Loc.T("exp.chk.decompile_cs");
                chkSaveDummyDll.Content = Loc.T("exp.chk.save_dummydll");
                chkAutoLoadCode.Content = Loc.T("exp.chk.autoload_code");

                lblAssetsGroup.Text = Loc.T("exp.group.assets");
                chkTextures.Content = Loc.T("exp.chk.textures");
                chkTextAssets.Content = Loc.T("exp.chk.text_assets");
                chkRawAssets.Content = Loc.T("exp.chk.raw_assets");
            }

            Loc.LanguageChanged += UpdateLocalization;
            root.DetachedFromVisualTree += (_, _) => Loc.LanguageChanged -= UpdateLocalization;

            return root;
        }

        private static string[] GetFilterItems() => new[]
        {
            Loc.T("exp.filter.assembly_csharp"),
            Loc.T("exp.filter.game_only"),
            Loc.T("exp.filter.all"),
            Loc.T("exp.filter.none")
        };

        private static List<string> FilterAssemblies(List<string> allDlls, int filterIndex)
        {
            if (allDlls.Count == 0 || filterIndex == 3)
                return new List<string>();

            if (filterIndex == 2)
                return allDlls;

            if (filterIndex == 0)
            {
                var primary = allDlls
                    .Where(p =>
                    {
                        var name = Path.GetFileName(p);
                        return name.Equals("Assembly-CSharp.dll", StringComparison.OrdinalIgnoreCase)
                            || name.Equals("Assembly-CSharp-firstpass.dll", StringComparison.OrdinalIgnoreCase);
                    })
                    .ToList();

                if (primary.Count > 0)
                    return primary;

                return allDlls
                    .Where(p => Path.GetFileName(p).StartsWith("Assembly-", StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }

            return allDlls
                .Where(p =>
                {
                    var name = Path.GetFileNameWithoutExtension(p);
                    if (name.Equals("GameAssembly", StringComparison.OrdinalIgnoreCase))
                        return false;

                    return !ExcludedSystemPrefixes.Any(prefix =>
                        name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
                })
                .ToList();
        }

        private static int DecompileAssemblyToFolder(string dllPath, string targetAsmFolder, Action<string> appendLog)
        {
            int count = 0;
            try
            {
                var settings = new DecompilerSettings(DecompilerLang.CSharp4)
                {
                    ThrowOnAssemblyResolveErrors = false
                };

                var decompiler = new CSharpDecompiler(dllPath, settings);
                string assemblyName = Path.GetFileNameWithoutExtension(dllPath);
                var topTypes = decompiler.TypeSystem.MainModule.TypeDefinitions
                    .Where(t => !t.Name.StartsWith("<") && t.DeclaringType == null)
                    .ToList();

                var generatedFiles = new List<(string Path, string Code, string Guid)>();
                var realDeclaredNamespaces = new HashSet<string>(StringComparer.Ordinal);
                bool hasUnsafeCode = false;

                foreach (var typeDef in topTypes)
                {
                    try
                    {
                        string code = decompiler.DecompileAsString(typeDef.MetadataToken);
                        string rawClassName = StripGenericArity(typeDef.Name);
                        string namespaceName = typeDef.Namespace ?? string.Empty;

                        if (rawClassName == "Action")
                        {
                            string delegateDefs =
                                "public delegate void Action<T1, T2, T3, T4, T5>(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5);\r\n" +
                                "public delegate void Action<T1, T2, T3, T4, T5, T6>(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6);\r\n";

                            if (string.IsNullOrWhiteSpace(namespaceName))
                            {
                                code = delegateDefs;
                            }
                            else
                            {
                                code = $"namespace {namespaceName}\r\n{{\r\n" +
                                       "    public delegate void Action<T1, T2, T3, T4, T5>(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5);\r\n" +
                                       "    public delegate void Action<T1, T2, T3, T4, T5, T6>(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6);\r\n" +
                                       "}\r\n";
                            }
                        }
                        else if (typeDef.Name == "MonoBehaviour" &&
                                 namespaceName.Equals("Photon", StringComparison.Ordinal))
                        {
                            code = Regex.Replace(
                                code,
                                @"\bpublic\s+class\s+MonoBehaviour\s*:\s*MonoBehaviour\b",
                                "public class MonoBehaviour : UnityEngine.MonoBehaviour");

                            if (!Regex.IsMatch(code, @"\bnamespace\s+Photon\b"))
                            {
                                code = "using UnityEngine;\r\n\r\nnamespace Photon\r\n{\r\n" +
                                       Regex.Replace(code, @"^\s*using\s+UnityEngine;\s*\r?\n?", "", RegexOptions.Multiline) +
                                       "\r\n}\r\n";
                            }
                        }

                        code = CleanCSharp7LocalRefs(code);

                        if (Regex.IsMatch(code, @"\bunsafe\b"))
                            hasUnsafeCode = true;

                        foreach (Match nsMatch in Regex.Matches(code, @"^\s*namespace\s+([A-Za-z0-9_.]+)", RegexOptions.Multiline))
                        {
                            string nsName = nsMatch.Groups[1].Value;
                            realDeclaredNamespaces.Add(nsName);
                            int dotIdx = nsName.IndexOf('.');
                            if (dotIdx > 0)
                                realDeclaredNamespaces.Add(nsName[..dotIdx]);
                        }

                        string nsFolder = string.IsNullOrWhiteSpace(namespaceName)
                            ? targetAsmFolder
                            : Path.Combine(targetAsmFolder, SanitizeRelativeNamespacePath(namespaceName));

                        Directory.CreateDirectory(nsFolder);

                        string cleanTypeName = SanitizeFileName(rawClassName);
                        string csPath = GetUniqueFilePath(Path.Combine(nsFolder, cleanTypeName + ".cs"));
                        string scriptGuid = ComputeUnityScriptGuid(assemblyName, namespaceName, rawClassName);

                        generatedFiles.Add((csPath, code, scriptGuid));
                        count++;
                    }
                    catch { }
                }

                bool hasCustomAction5 = topTypes.Any(t =>
                    StripGenericArity(t.Name) == "Action" && t.TypeParameterCount == 5);

                string[] builtInPrefixes =
                {
                    "System", "UnityEngine", "UnityEditor", "Mono", "Microsoft", "Photon", "ExitGames", "ProtoBuf"
                };

                var utf8NoBom = new UTF8Encoding(false);

                foreach (var (csPath, rawCode, scriptGuid) in generatedFiles)
                {
                    string cleanedCode = Regex.Replace(
                        rawCode,
                        @"^\s*using\s+([A-Za-z0-9_.]+)\s*;\s*\r?\n?",
                        m =>
                        {
                            string usedNs = m.Groups[1].Value;
                            if (realDeclaredNamespaces.Contains(usedNs))
                                return m.Value;

                            if (builtInPrefixes.Any(p => usedNs.Equals(p, StringComparison.Ordinal) ||
                                                         usedNs.StartsWith(p + ".", StringComparison.Ordinal)))
                                return m.Value;

                            return string.Empty;
                        },
                        RegexOptions.Multiline);

                    foreach (var dirPrefix in new[] { "doru.", "cspServer." })
                    {
                        if (!realDeclaredNamespaces.Contains(dirPrefix.TrimEnd('.')))
                            cleanedCode = cleanedCode.Replace(dirPrefix, string.Empty);
                    }

                    cleanedCode = Regex.Replace(cleanedCode, @"\bExporterFunc\b(?!<)", "ExporterFunc<object>");
                    cleanedCode = Regex.Replace(cleanedCode, @"\bImporterFunc\b(?!<)", "ImporterFunc<object, object>");

                    if (!hasCustomAction5)
                    {
                        cleanedCode = Regex.Replace(
                            cleanedCode,
                            @"(?<!System\.)\bAction<([^<>]+,[^<>]+,[^<>]+,[^<>]+,[^<>]+)>",
                            "System.Action<$1>");
                    }

                    File.WriteAllText(csPath, cleanedCode, utf8NoBom);
                    WriteMonoScriptMetaFile(csPath, scriptGuid, utf8NoBom);
                }

                if (hasUnsafeCode)
                {
                    DirectoryInfo? dir = new DirectoryInfo(targetAsmFolder);
                    while (dir != null && !dir.Name.Equals("Assets", StringComparison.OrdinalIgnoreCase))
                        dir = dir.Parent;

                    if (dir != null)
                    {
                        foreach (string rspName in new[] { "smcs.rsp", "gmcs.rsp", "mcs.rsp" })
                        {
                            string rspPath = Path.Combine(dir.FullName, rspName);
                            if (!File.Exists(rspPath))
                                File.WriteAllText(rspPath, "-unsafe\r\n", utf8NoBom);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                appendLog($"[WARN] {Path.GetFileName(dllPath)}: {ex.Message}");
            }

            return count;
        }

        private static string ComputeUnityScriptGuid(string assemblyName, string namespaceName, string className)
        {
            string input = "s\0\0\0" + assemblyName + "\0" + namespaceName + "\0" + className;
            byte[] inputBytes = Encoding.UTF8.GetBytes(input);
            byte[] hashBytes = MD5.HashData(inputBytes);

            var sb = new StringBuilder(32);
            foreach (byte b in hashBytes)
            {
                sb.Append($"{(b & 0x0F):x1}{(b >> 4):x1}");
            }
            return sb.ToString();
        }

        private static void WriteMonoScriptMetaFile(string csPath, string guid, Encoding utf8NoBom)
        {
            string metaContent =
                "fileFormatVersion: 2\n" +
                $"guid: {guid}\n" +
                "MonoImporter:\n" +
                "  serializedVersion: 2\n" +
                "  defaultReferences: []\n" +
                "  executionOrder: 0\n" +
                "  icon: {instanceID: 0}\n" +
                "  userData: \n" +
                "  assetBundleName: \n" +
                "  assetBundleVariant: \n";

            File.WriteAllText(csPath + ".meta", metaContent, utf8NoBom);
        }

        private static string CleanCSharp7LocalRefs(string code)
        {
            var refMatches = Regex.Matches(
                code,
                @"^\s*ref\s+[A-Za-z0-9_<>\[\].,\s]+\s+([A-Za-z_][A-Za-z0-9_]*)\s*=\s*ref\s+([^;]+);\s*\r?\n?",
                RegexOptions.Multiline);

            if (refMatches.Count == 0)
                return code;

            string result = code;
            foreach (Match m in refMatches)
            {
                string localVar = m.Groups[1].Value;
                string targetExpr = m.Groups[2].Value.Trim();

                result = result.Replace(m.Value, string.Empty);
                result = Regex.Replace(result, $@"\b{Regex.Escape(localVar)}\b", targetExpr);
            }

            return result;
        }

        private static (int Textures, int TextAssets, int RawAssets) ExportAllAssets(
            string dataDir,
            string outDir,
            bool exportTextures,
            bool exportTextAssets,
            bool exportRawAssets,
            Action<string> appendLog)
        {
            int texCount = 0;
            int txtCount = 0;
            int rawCount = 0;

            var texDir = Path.Combine(outDir, "Assets", "Texture2D");
            var txtDir = Path.Combine(outDir, "Assets", "TextAsset");
            var rawDir = Path.Combine(outDir, "Assets", "Raw");

            if (exportTextures) Directory.CreateDirectory(texDir);
            if (exportTextAssets) Directory.CreateDirectory(txtDir);
            if (exportRawAssets) Directory.CreateDirectory(rawDir);

            var manager = new AssetsManager();
            try
            {
                var asm = typeof(ProjectExporterTab).Assembly;
                string? resName = asm.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith("classdata.tpk"));
                if (resName != null)
                {
                    using var stream = asm.GetManifestResourceStream(resName)!;
                    manager.LoadClassPackage(stream);
                }

                var candidateFiles = Directory.GetFiles(dataDir, "*", SearchOption.TopDirectoryOnly)
                    .Where(f =>
                    {
                        string ext = Path.GetExtension(f).ToLowerInvariant();
                        string nm = Path.GetFileName(f).ToLowerInvariant();
                        return ext is ".assets" or ".unity3d" or ".bundle"
                               || nm is "maindata" or "globalgamemanagers"
                               || (string.IsNullOrEmpty(ext) && nm.StartsWith("level"));
                    })
                    .OrderBy(Path.GetFileName)
                    .ToList();

                foreach (var filePath in candidateFiles)
                {
                    string ext = Path.GetExtension(filePath).ToLowerInvariant();
                    string fileName = Path.GetFileName(filePath);
                    bool isBundle = ext is ".unity3d" or ".bundle" || fileName.Equals("data.unity3d", StringComparison.OrdinalIgnoreCase);

                    var instances = new List<AssetsFileInstance>();
                    try
                    {
                        if (isBundle)
                        {
                            var bun = manager.LoadBundleFile(filePath, true);
                            for (int i = 0; i < bun.file.BlockAndDirInfo.DirectoryInfos.Count; i++)
                            {
                                var di = bun.file.BlockAndDirInfo.DirectoryInfos[i];
                                if (di.Name.EndsWith(".resS", StringComparison.OrdinalIgnoreCase)
                                    || di.Name.EndsWith(".resource", StringComparison.OrdinalIgnoreCase))
                                    continue;

                                try
                                {
                                    var inst = manager.LoadAssetsFileFromBundle(bun, i, true);
                                    manager.LoadClassDatabaseFromPackage(inst.file.Metadata.UnityVersion);
                                    instances.Add(inst);
                                }
                                catch { }
                            }
                        }
                        else
                        {
                            var inst = manager.LoadAssetsFile(filePath, true);
                            try { manager.LoadClassDatabaseFromPackage(inst.file.Metadata.UnityVersion); } catch { }
                            instances.Add(inst);
                        }
                    }
                    catch
                    {
                        continue;
                    }

                    foreach (var inst in instances)
                    {
                        foreach (var info in inst.file.AssetInfos)
                        {
                            var classId = (AssetClassID)info.TypeId;

                            if (classId == AssetClassID.Texture2D && !exportTextures)
                                continue;
                            if (classId == AssetClassID.TextAsset && !exportTextAssets)
                                continue;
                            if (classId != AssetClassID.Texture2D && classId != AssetClassID.TextAsset && !exportRawAssets)
                                continue;

                            try
                            {
                                var baseField = manager.GetBaseField(inst, info);
                                var nameField = baseField.Get("m_Name");
                                string assetName = (!nameField.IsDummy && !string.IsNullOrWhiteSpace(nameField.AsString))
                                    ? SanitizeFileName(nameField.AsString)
                                    : $"{classId}_{info.PathId}";

                                if (classId == AssetClassID.Texture2D && exportTextures)
                                {
                                    if (TryExportTexture2D(baseField, inst, texDir, assetName, info.PathId))
                                        texCount++;
                                }
                                else if (classId == AssetClassID.TextAsset && exportTextAssets)
                                {
                                    var scriptField = baseField.Get("m_Script");
                                    if (!scriptField.IsDummy)
                                    {
                                        byte[] rawBytes = scriptField.AsByteArray ?? Encoding.UTF8.GetBytes(scriptField.AsString ?? string.Empty);
                                        string textContent = Encoding.UTF8.GetString(rawBytes);
                                        string trimmed = textContent.TrimStart();
                                        string outExt = (trimmed.StartsWith("{") || trimmed.StartsWith("[")) ? ".json" : ".txt";

                                        string targetPath = GetUniqueFilePath(Path.Combine(txtDir, $"{assetName}_{info.PathId}{outExt}"));
                                        File.WriteAllBytes(targetPath, rawBytes);
                                        txtCount++;
                                    }
                                }
                                else if (exportRawAssets)
                                {
                                    byte[] rawBytes = baseField.WriteToByteArray();
                                    string targetPath = GetUniqueFilePath(Path.Combine(rawDir, $"{classId}_{assetName}_{info.PathId}.bin"));
                                    File.WriteAllBytes(targetPath, rawBytes);
                                    rawCount++;
                                }
                            }
                            catch { }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                appendLog($"[WARN] Assets export error: {ex.Message}");
            }
            finally
            {
                try { manager.UnloadAllAssetsFiles(); } catch { }
            }

            return (texCount, txtCount, rawCount);
        }

        private static bool TryExportTexture2D(
            AssetTypeValueField baseField,
            AssetsFileInstance inst,
            string texDir,
            string assetName,
            long pathId)
        {
            var texFile = TextureFile.ReadTextureFile(baseField);
            if (texFile.m_Width <= 0 || texFile.m_Height <= 0)
                return false;

            byte[]? bgraData = texFile.FillPictureData(inst);
            if (bgraData == null || bgraData.Length == 0)
                return false;

            int width = texFile.m_Width;
            int height = texFile.m_Height;
            int expectedSize = width * height * 4;
            if (bgraData.Length < expectedSize)
                return false;

            byte[] flippedBgra = new byte[expectedSize];
            int stride = width * 4;
            for (int y = 0; y < height; y++)
            {
                int srcOffset = (height - 1 - y) * stride;
                int dstOffset = y * stride;
                Buffer.BlockCopy(bgraData, srcOffset, flippedBgra, dstOffset, stride);
            }

            string targetPath = GetUniqueFilePath(Path.Combine(texDir, $"{assetName}_{pathId}.png"));

            using var bmp = new WriteableBitmap(
                new PixelSize(width, height),
                new Vector(96, 96),
                PixelFormat.Bgra8888,
                AlphaFormat.Unpremul);

            using (var fb = bmp.Lock())
            {
                Marshal.Copy(flippedBgra, 0, fb.Address, expectedSize);
            }

            bmp.Save(targetPath);
            return true;
        }

        private static string DetectUnityVersionString(
            string gameRoot,
            string dataDir,
            string metaPath,
            Func<string> getVer)
        {
            if (!string.IsNullOrWhiteSpace(gameRoot) && Directory.Exists(gameRoot))
            {
                var unityPlayerPath = Path.Combine(gameRoot, "UnityPlayer.dll");
                if (File.Exists(unityPlayerPath))
                {
                    try
                    {
                        var fvi = FileVersionInfo.GetVersionInfo(unityPlayerPath);
                        var prodVer = fvi.ProductVersion ?? fvi.FileVersion;
                        if (!string.IsNullOrWhiteSpace(prodVer))
                        {
                            var match = Regex.Match(prodVer, @"(\d+\.\d+\.\d+(?:[abfp]\d+)?)");
                            if (match.Success)
                                return match.Groups.OfType<Group>().Skip(1).First().Value;
                        }
                    }
                    catch { }
                }
            }

            try
            {
                if (!string.IsNullOrWhiteSpace(dataDir) && Directory.Exists(dataDir))
                {
                    var detected = Cpp2IlApi.DetermineUnityVersion(gameRoot, dataDir);
                    if (detected.Major > 0)
                        return detected.ToString();
                }
            }
            catch { }

            if (!string.IsNullOrWhiteSpace(metaPath) && File.Exists(metaPath))
            {
                try
                {
                    using var fs = File.OpenRead(metaPath);
                    if (fs.Length >= 8)
                    {
                        using var br = new BinaryReader(fs);
                        uint magic = br.ReadUInt32();
                        int metaVersion = br.ReadInt32();
                        if (magic == 0xFAB11BAF)
                        {
                            if (metaVersion >= 31) return "6000.0.0";
                            if (metaVersion >= 29) return "2022.3.49";
                            if (metaVersion >= 27) return "2020.3.0";
                            if (metaVersion >= 24) return "2019.4.0";
                        }
                    }
                }
                catch { }
            }

            var rawVer = getVer?.Invoke() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(rawVer) && !rawVer.Contains("IL2CPP", StringComparison.OrdinalIgnoreCase))
            {
                var match = Regex.Match(rawVer, @"(\d+\.\d+\.\d+(?:[abfp]\d+)?)");
                if (match.Success)
                    return match.Groups.OfType<Group>().Skip(1).First().Value;
            }

            return string.Empty;
        }

        private static string StripGenericArity(string typeName)
        {
            int idx = typeName.IndexOf('`');
            return idx > 0 ? typeName[..idx] : typeName;
        }

        private static string SanitizeRelativeNamespacePath(string ns)
        {
            var parts = ns.Split('.', StringSplitOptions.RemoveEmptyEntries)
                .Select(SanitizeFileName);
            return Path.Combine(parts.ToArray());
        }

        private static string SanitizeFileName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return "Unnamed";

            var invalid = Path.GetInvalidFileNameChars();
            var sb = new StringBuilder(name.Length);
            foreach (char c in name)
            {
                sb.Append(invalid.Contains(c) ? '_' : c);
            }
            return sb.ToString().Trim();
        }

        private static string GetUniqueFilePath(string desiredPath)
        {
            if (!File.Exists(desiredPath))
                return desiredPath;

            string dir = Path.GetDirectoryName(desiredPath) ?? string.Empty;
            string stem = Path.GetFileNameWithoutExtension(desiredPath);
            string ext = Path.GetExtension(desiredPath);

            int idx = 1;
            while (true)
            {
                string candidate = Path.Combine(dir, $"{stem}_{idx}{ext}");
                if (!File.Exists(candidate))
                    return candidate;
                idx++;
            }
        }
    }
}
