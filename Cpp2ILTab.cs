using AssetRipper.Primitives;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Cpp2IL.Core;
using Cpp2IL.Core.Api;
using Cpp2IL.Core.InstructionSets;
using Cpp2IL.Core.Logging;
using Cpp2IL.Core.Model.Contexts;
using Cpp2IL.Core.OutputFormats;
using Cpp2IL.Core.ProcessingLayers;
using LibCpp2IL;
using LibCpp2IL.PE;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace SpyTool2
{
    public static class Cpp2ILTab
    {
        private static Action<string>? _activeLogSink;
        private static bool _cpp2IlInitialized;
        private static readonly object _initLock = new();

        private static void EnsureCpp2IlSetup(Action<string> appendLog)
        {
            _activeLogSink = appendLog;

            lock (_initLock)
            {
                if (_cpp2IlInitialized)
                    return;

                // 1. Подключаем логгер Cpp2IL.Core к UI-логу
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

                Logger.VerboseLog += (message, source) =>
                {
                    // Заглушка, чтобы делегат VerboseLog не оставался null
                };

                // Страховка на случай дополнительных неинициализированных делегатов в Logger
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

                // 2. Регистрация наборов инструкций (PEBinary уже зарегистрирован по умолчанию в AssetRipper.Cpp2IL.Core 1.0.9)
                InstructionSetRegistry.RegisterInstructionSet<X86InstructionSet>(DefaultInstructionSets.X86_32);
                InstructionSetRegistry.RegisterInstructionSet<X86InstructionSet>(DefaultInstructionSets.X86_64);

                _cpp2IlInitialized = true;
            }
        }

        public static Control Build(
            Window owner,
            Func<string> getDataDir,
            Func<string> getVer,
            Action<IEnumerable<string>> onDlls,
            Action<string> setStatus)
        {
            var gaBox = new TextBox
            {
                Watermark = Loc.T("cpp2il.watermark.ga"),
                HorizontalAlignment = HorizontalAlignment.Stretch
            };

            var metaBox = new TextBox
            {
                Watermark = Loc.T("cpp2il.watermark.meta"),
                HorizontalAlignment = HorizontalAlignment.Stretch
            };

            var verBox = new TextBox
            {
                Watermark = Loc.T("cpp2il.watermark.ver"),
                Width = 240
            };

            var outBox = new TextBox
            {
                Watermark = Loc.T("cpp2il.watermark.out"),
                HorizontalAlignment = HorizontalAlignment.Stretch
            };

            var chkDummyDll = new CheckBox
            {
                Content = Loc.T("cpp2il.chk.dummy_dll"),
                IsChecked = true
            };

            var chkDumpCs = new CheckBox
            {
                Content = Loc.T("cpp2il.chk.dump_cs"),
                IsChecked = true
            };

            var chkScriptJson = new CheckBox
            {
                Content = Loc.T("cpp2il.chk.script_json"),
                IsChecked = true
            };

            var chkAutoLoad = new CheckBox
            {
                Content = Loc.T("cpp2il.chk.auto_load"),
                IsChecked = true
            };

            var logBox = new TextBox
            {
                IsReadOnly = true,
                AcceptsReturn = true,
                TextWrapping = TextWrapping.NoWrap,
                FontFamily = new FontFamily("Consolas, Cascadia Code, monospace"),
                FontSize = 12,
                MinHeight = 260,
                VerticalAlignment = VerticalAlignment.Stretch
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

            EnsureCpp2IlSetup(AppendLog);

            string DetectUnityVersionString(string gameRoot, string dataDir, string metaPath)
            {
                // 1. Читаем ProductVersion / FileVersion из UnityPlayer.dll
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
                                    return match.Groups[1].Value;
                            }
                        }
                        catch
                        {
                            // Переходим к следующему способу
                        }
                    }

                    // 2. Пробуем встроенный детектор Cpp2IL
                    try
                    {
                        EnsureCpp2IlSetup(AppendLog);
                        if (!string.IsNullOrWhiteSpace(dataDir) && Directory.Exists(dataDir))
                        {
                            var detected = Cpp2IlApi.DetermineUnityVersion(gameRoot, dataDir);
                            if (detected.Major > 0)
                                return detected.ToString();
                        }
                    }
                    catch
                    {
                        // Переходим к следующему способу
                    }
                }

                // 3. Определяем по версии метаданных в заголовке global-metadata.dat (смещение 4)
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
                                if (metaVersion >= 31)
                                    return "6000.0.0";
                                if (metaVersion >= 29)
                                    return "2022.3.49";
                                if (metaVersion >= 27)
                                    return "2020.3.0";
                                if (metaVersion >= 24)
                                    return "2019.4.0";
                            }
                        }
                    }
                    catch
                    {
                        // Игнорируем ошибки чтения файла
                    }
                }

                // 4. Fallback на getVer(), если там не строка статуса "IL2CPP v31..."
                var rawVer = getVer?.Invoke() ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(rawVer) && !rawVer.Contains("IL2CPP", StringComparison.OrdinalIgnoreCase))
                {
                    var match = Regex.Match(rawVer, @"(\d+\.\d+\.\d+(?:[abfp]\d+)?)");
                    if (match.Success)
                        return match.Groups[1].Value;
                }

                return string.Empty;
            }

            void AutoFillFromGame()
            {
                try
                {
                    EnsureCpp2IlSetup(AppendLog);

                    var dataDir = getDataDir?.Invoke() ?? string.Empty;

                    if (!string.IsNullOrWhiteSpace(dataDir) && Directory.Exists(dataDir))
                    {
                        var gameRoot = Directory.GetParent(dataDir)?.FullName ?? dataDir;

                        var gaCandidate = Path.Combine(gameRoot, "GameAssembly.dll");
                        if (!File.Exists(gaCandidate))
                        {
                            gaCandidate = Directory.GetFiles(gameRoot, "GameAssembly.dll", SearchOption.AllDirectories)
                                .FirstOrDefault() ?? string.Empty;
                        }
                        if (File.Exists(gaCandidate))
                            gaBox.Text = gaCandidate;

                        var metaCandidate = Path.Combine(dataDir, "il2cpp_data", "Metadata", "global-metadata.dat");
                        if (!File.Exists(metaCandidate))
                        {
                            metaCandidate = Directory.GetFiles(dataDir, "global-metadata.dat", SearchOption.AllDirectories)
                                .FirstOrDefault() ?? string.Empty;
                        }
                        if (File.Exists(metaCandidate))
                            metaBox.Text = metaCandidate;

                        outBox.Text = Path.Combine(gameRoot, "IL2CPP_Dump");

                        var detectedVer = DetectUnityVersionString(gameRoot, dataDir, metaCandidate);
                        if (!string.IsNullOrWhiteSpace(detectedVer))
                            verBox.Text = detectedVer;

                        var verDisplay = string.IsNullOrWhiteSpace(verBox.Text) ? Loc.T("cpp2il.ver.unknown") : verBox.Text;
                        AppendLog(string.Format(Loc.T("cpp2il.log.autofill_ok"), verDisplay));
                        setStatus?.Invoke(Loc.T("cpp2il.status.autofill_ok"));
                    }
                    else
                    {
                        AppendLog(Loc.T("cpp2il.log.no_datadir"));
                    }
                }
                catch (Exception ex)
                {
                    AppendLog(string.Format(Loc.T("cpp2il.log.autofill_err"), ex.Message));
                }
            }

            var btnAutoFill = new Button { Content = Loc.T("cpp2il.btn.auto_fill") };
            btnAutoFill.Click += (_, _) => AutoFillFromGame();

            var btnBrowseGa = new Button { Content = Loc.T("cpp2il.btn.browse") };
            btnBrowseGa.Click += async (_, _) =>
            {
                var f = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = Loc.T("cpp2il.dialog.ga_title"),
                    AllowMultiple = false,
                    FileTypeFilter = new[]
                    {
                        new FilePickerFileType("IL2CPP Binary") { Patterns = new[] { "*.dll", "*.so", "*.dylib", "*.*" } }
                    }
                });
                if (f.Count > 0)
                {
                    gaBox.Text = f[0].Path.LocalPath;
                    var dir = Path.GetDirectoryName(gaBox.Text);
                    if (!string.IsNullOrEmpty(dir))
                    {
                        if (string.IsNullOrWhiteSpace(outBox.Text))
                            outBox.Text = Path.Combine(dir, "IL2CPP_Dump");

                        if (string.IsNullOrWhiteSpace(verBox.Text))
                        {
                            var detected = DetectUnityVersionString(dir, string.Empty, metaBox.Text?.Trim() ?? string.Empty);
                            if (!string.IsNullOrWhiteSpace(detected))
                                verBox.Text = detected;
                        }
                    }
                }
            };

            var btnBrowseMeta = new Button { Content = Loc.T("cpp2il.btn.browse") };
            btnBrowseMeta.Click += async (_, _) =>
            {
                var f = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = Loc.T("cpp2il.dialog.meta_title"),
                    AllowMultiple = false,
                    FileTypeFilter = new[]
                    {
                        new FilePickerFileType("Metadata File") { Patterns = new[] { "*.dat", "*.*" } }
                    }
                });
                if (f.Count > 0)
                {
                    metaBox.Text = f[0].Path.LocalPath;
                    if (string.IsNullOrWhiteSpace(verBox.Text))
                    {
                        var gameRoot = !string.IsNullOrWhiteSpace(gaBox.Text) ? Path.GetDirectoryName(gaBox.Text) ?? string.Empty : string.Empty;
                        var detected = DetectUnityVersionString(gameRoot, string.Empty, metaBox.Text);
                        if (!string.IsNullOrWhiteSpace(detected))
                            verBox.Text = detected;
                    }
                }
            };

            var btnBrowseOut = new Button { Content = Loc.T("cpp2il.btn.browse") };
            btnBrowseOut.Click += async (_, _) =>
            {
                var d = await owner.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
                {
                    Title = Loc.T("cpp2il.dialog.out_title"),
                    AllowMultiple = false
                });
                if (d.Count > 0)
                    outBox.Text = d[0].Path.LocalPath;
            };

            var btnRun = new Button
            {
                Content = Loc.T("cpp2il.btn.run"),
                FontWeight = FontWeight.SemiBold
            };

            var btnOpenOut = new Button
            {
                Content = Loc.T("cpp2il.btn.open_out")
            };

            btnOpenOut.Click += (_, _) =>
            {
                var targetDir = outBox.Text?.Trim();
                if (string.IsNullOrWhiteSpace(targetDir) && !string.IsNullOrWhiteSpace(gaBox.Text))
                {
                    var baseDir = Path.GetDirectoryName(gaBox.Text);
                    if (!string.IsNullOrEmpty(baseDir))
                        targetDir = Path.Combine(baseDir, "IL2CPP_Dump");
                }

                if (!string.IsNullOrWhiteSpace(targetDir) && Directory.Exists(targetDir))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = targetDir,
                        UseShellExecute = true
                    });
                }
                else
                {
                    AppendLog(Loc.T("cpp2il.log.out_not_found"));
                }
            };

            btnRun.Click += async (_, _) =>
            {
                var gaPath = gaBox.Text?.Trim() ?? string.Empty;
                var metaPath = metaBox.Text?.Trim() ?? string.Empty;
                var verText = verBox.Text?.Trim() ?? string.Empty;
                var outDir = outBox.Text?.Trim() ?? string.Empty;

                bool doDummy = chkDummyDll.IsChecked == true;
                bool doDumpCs = chkDumpCs.IsChecked == true;
                bool doScriptJson = chkScriptJson.IsChecked == true;
                bool doAutoLoad = chkAutoLoad.IsChecked == true;

                if (!File.Exists(gaPath))
                {
                    AppendLog(Loc.T("cpp2il.log.ga_not_found"));
                    setStatus?.Invoke(Loc.T("cpp2il.status.ga_not_found"));
                    return;
                }

                if (!File.Exists(metaPath))
                {
                    AppendLog(Loc.T("cpp2il.log.meta_not_found"));
                    setStatus?.Invoke(Loc.T("cpp2il.status.meta_not_found"));
                    return;
                }

                if (string.IsNullOrWhiteSpace(outDir))
                {
                    var gameDir = Path.GetDirectoryName(gaPath) ?? AppContext.BaseDirectory;
                    outDir = Path.Combine(gameDir, "IL2CPP_Dump");
                    outBox.Text = outDir;
                }

                if (string.IsNullOrWhiteSpace(verText))
                {
                    var gameDir = Path.GetDirectoryName(gaPath) ?? string.Empty;
                    var dataDir = getDataDir?.Invoke() ?? string.Empty;
                    verText = DetectUnityVersionString(gameDir, dataDir, metaPath);
                    if (!string.IsNullOrWhiteSpace(verText))
                        verBox.Text = verText;
                }

                UnityVersion uVersion;
                try
                {
                    if (string.IsNullOrWhiteSpace(verText))
                        throw new FormatException(Loc.T("cpp2il.err.ver_empty"));

                    uVersion = UnityVersion.Parse(verText);
                }
                catch (Exception ex)
                {
                    AppendLog(string.Format(Loc.T("cpp2il.log.ver_parse_err"), verText, ex.Message));
                    setStatus?.Invoke(Loc.T("cpp2il.status.ver_invalid"));
                    return;
                }

                btnRun.IsEnabled = false;
                logBox.Text = string.Empty;
                setStatus?.Invoke(Loc.T("cpp2il.status.running"));

                List<string>? generatedDlls = null;

                try
                {
                    EnsureCpp2IlSetup(AppendLog);

                    await Task.Run(() =>
                    {
                        Directory.CreateDirectory(outDir);

                        AppendLog(string.Format(Loc.T("cpp2il.log.init"), uVersion));
                        Cpp2IlApi.InitializeLibCpp2Il(gaPath, metaPath, uVersion, false);

                        var ctx = Cpp2IlApi.CurrentAppContext
                            ?? throw new InvalidOperationException("Cpp2IlApi.CurrentAppContext вернул null после инициализации.");

                        AppendLog(string.Format(Loc.T("cpp2il.log.meta_loaded"), ctx.MetadataVersion, ctx.Assemblies.Count));

                        if (doDummy)
                        {
                            var dummyDir = Path.Combine(outDir, "DummyDll");
                            Directory.CreateDirectory(dummyDir);

                            AppendLog(Loc.T("cpp2il.log.inject_attrs"));
                            new AttributeInjectorProcessingLayer().Process(ctx);

                            AppendLog(Loc.T("cpp2il.log.gen_dummy"));
                            new AsmResolverDllOutputFormatEmpty().DoOutput(ctx, dummyDir);

                            generatedDlls = Directory.GetFiles(dummyDir, "*.dll", SearchOption.TopDirectoryOnly)
                                .OrderBy(Path.GetFileName)
                                .ToList();

                            AppendLog(string.Format(Loc.T("cpp2il.log.dummy_saved"), generatedDlls.Count, dummyDir));
                        }

                        if (doDumpCs)
                        {
                            var dumpCsPath = Path.Combine(outDir, "dump.cs");
                            AppendLog(Loc.T("cpp2il.log.gen_dumpcs"));
                            GenerateDumpCsFromContext(dumpCsPath, ctx);
                            AppendLog(string.Format(Loc.T("cpp2il.log.dumpcs_saved"), dumpCsPath));
                        }

                        if (doScriptJson)
                        {
                            var scriptJsonPath = Path.Combine(outDir, "script.json");
                            AppendLog(Loc.T("cpp2il.log.gen_scriptjson"));
                            GenerateScriptJsonFromContext(scriptJsonPath, ctx);
                            AppendLog(string.Format(Loc.T("cpp2il.log.scriptjson_saved"), scriptJsonPath));
                        }
                    });

                    if (doDummy && doAutoLoad && generatedDlls != null && generatedDlls.Count > 0)
                    {
                        AppendLog(Loc.T("cpp2il.log.autoload"));
                        onDlls?.Invoke(generatedDlls);
                    }

                    AppendLog(Loc.T("cpp2il.log.done"));
                    setStatus?.Invoke(Loc.T("cpp2il.status.done"));
                }
                catch (Exception ex)
                {
                    AppendLog(string.Format(Loc.T("cpp2il.log.crit_err"), ex));
                    setStatus?.Invoke(string.Format(Loc.T("cpp2il.status.crit_err"), ex.Message));
                }
                finally
                {
                    btnRun.IsEnabled = true;
                }
            };

            var grid = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
                RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto"),
                Margin = new Thickness(0, 0, 0, 8)
            };

            var lblGa = AddFormRow(grid, 0, Loc.T("cpp2il.label.ga"), gaBox, btnBrowseGa);
            var lblMeta = AddFormRow(grid, 1, Loc.T("cpp2il.label.meta"), metaBox, btnBrowseMeta);
            var lblVer = AddFormRow(grid, 2, Loc.T("cpp2il.label.ver"), verBox, btnAutoFill);
            var lblOut = AddFormRow(grid, 3, Loc.T("cpp2il.label.out"), outBox, btnBrowseOut);

            var optionsPanel = new WrapPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 4, 0, 8)
            };
            chkDummyDll.Margin = new Thickness(0, 0, 16, 4);
            chkDumpCs.Margin = new Thickness(0, 0, 16, 4);
            chkScriptJson.Margin = new Thickness(0, 0, 16, 4);
            chkAutoLoad.Margin = new Thickness(0, 0, 16, 4);

            optionsPanel.Children.Add(chkDummyDll);
            optionsPanel.Children.Add(chkDumpCs);
            optionsPanel.Children.Add(chkScriptJson);
            optionsPanel.Children.Add(chkAutoLoad);

            var actionsPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 10,
                Margin = new Thickness(0, 0, 0, 8)
            };
            actionsPanel.Children.Add(btnRun);
            actionsPanel.Children.Add(btnOpenOut);

            var rootPanel = new DockPanel
            {
                Margin = new Thickness(12),
                LastChildFill = true
            };

            var topStack = new StackPanel { Spacing = 4 };
            topStack.Children.Add(grid);
            topStack.Children.Add(optionsPanel);
            topStack.Children.Add(actionsPanel);

            DockPanel.SetDock(topStack, Dock.Top);
            rootPanel.Children.Add(topStack);
            rootPanel.Children.Add(logBox);

            void UpdateLocalization()
            {
                lblGa.Text = Loc.T("cpp2il.label.ga");
                lblMeta.Text = Loc.T("cpp2il.label.meta");
                lblVer.Text = Loc.T("cpp2il.label.ver");
                lblOut.Text = Loc.T("cpp2il.label.out");

                gaBox.Watermark = Loc.T("cpp2il.watermark.ga");
                metaBox.Watermark = Loc.T("cpp2il.watermark.meta");
                verBox.Watermark = Loc.T("cpp2il.watermark.ver");
                outBox.Watermark = Loc.T("cpp2il.watermark.out");

                chkDummyDll.Content = Loc.T("cpp2il.chk.dummy_dll");
                chkDumpCs.Content = Loc.T("cpp2il.chk.dump_cs");
                chkScriptJson.Content = Loc.T("cpp2il.chk.script_json");
                chkAutoLoad.Content = Loc.T("cpp2il.chk.auto_load");

                btnAutoFill.Content = Loc.T("cpp2il.btn.auto_fill");
                btnBrowseGa.Content = Loc.T("cpp2il.btn.browse");
                btnBrowseMeta.Content = Loc.T("cpp2il.btn.browse");
                btnBrowseOut.Content = Loc.T("cpp2il.btn.browse");
                btnRun.Content = Loc.T("cpp2il.btn.run");
                btnOpenOut.Content = Loc.T("cpp2il.btn.open_out");
            }

            Loc.LanguageChanged += UpdateLocalization;
            rootPanel.DetachedFromVisualTree += (_, _) => Loc.LanguageChanged -= UpdateLocalization;

            // Автоматически заполняем пути при открытии вкладки, если игра уже выбрана
            if (!string.IsNullOrWhiteSpace(getDataDir?.Invoke()))
                AutoFillFromGame();

            return rootPanel;
        }

        private static TextBlock AddFormRow(Grid grid, int row, string labelText, Control inputControl, Control actionButton)
        {
            var label = new TextBlock
            {
                Text = labelText,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 10, 6)
            };

            inputControl.Margin = new Thickness(0, 0, 8, 6);
            actionButton.Margin = new Thickness(0, 0, 0, 6);

            Grid.SetRow(label, row);
            Grid.SetColumn(label, 0);

            Grid.SetRow(inputControl, row);
            Grid.SetColumn(inputControl, 1);

            Grid.SetRow(actionButton, row);
            Grid.SetColumn(actionButton, 2);

            grid.Children.Add(label);
            grid.Children.Add(inputControl);
            grid.Children.Add(actionButton);

            return label;
        }

        private static ulong GetBinaryImageBase(Il2CppBinary binary)
        {
            var binType = binary.GetType();
            if (binType.Name.Equals("PEBinary", StringComparison.OrdinalIgnoreCase))
            {
                var prop = binType.GetProperty("ImageBase", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (prop != null && prop.GetValue(binary) is ulong peBase)
                    return peBase;
            }

            return 0;
        }

        private static ulong ToRva(ulong vaOrRva, ulong imageBase)
        {
            if (vaOrRva == 0)
                return 0;

            if (imageBase > 0 && vaOrRva >= imageBase)
                return vaOrRva - imageBase;

            return vaOrRva;
        }

        private static string GetTypeKind(TypeAnalysisContext typeCtx)
        {
            if (typeCtx.IsEnumType)
                return "enum";
            if (typeCtx.IsValueType)
                return "struct";
            if (typeCtx.IsInterface)
                return "interface";
            return "class";
        }

        private static string GetTypeVisibility(TypeAttributes attrs)
        {
            return (attrs & TypeAttributes.VisibilityMask) switch
            {
                TypeAttributes.Public or TypeAttributes.NestedPublic => "public",
                TypeAttributes.NestedPrivate => "private",
                TypeAttributes.NestedFamily => "protected",
                TypeAttributes.NestedFamORAssem => "protected internal",
                TypeAttributes.NestedFamANDAssem => "private protected",
                _ => "internal"
            };
        }

        private static string GetFieldVisibility(FieldAttributes attrs)
        {
            return (attrs & FieldAttributes.FieldAccessMask) switch
            {
                FieldAttributes.Public => "public",
                FieldAttributes.Private => "private",
                FieldAttributes.Family => "protected",
                FieldAttributes.FamORAssem => "protected internal",
                FieldAttributes.FamANDAssem => "private protected",
                _ => "internal"
            };
        }

        private static string GetMethodVisibility(MethodAttributes attrs)
        {
            return (attrs & MethodAttributes.MemberAccessMask) switch
            {
                MethodAttributes.Public => "public",
                MethodAttributes.Private => "private",
                MethodAttributes.Family => "protected",
                MethodAttributes.FamORAssem => "protected internal",
                MethodAttributes.FamANDAssem => "private protected",
                _ => "internal"
            };
        }

        private static IEnumerable<TypeAnalysisContext> EnumerateAllTypes(AssemblyAnalysisContext asmCtx)
        {
            foreach (var topType in asmCtx.TopLevelTypes)
            {
                yield return topType;
                foreach (var nested in EnumerateNestedTypes(topType))
                {
                    yield return nested;
                }
            }
        }

        private static IEnumerable<TypeAnalysisContext> EnumerateNestedTypes(TypeAnalysisContext parent)
        {
            foreach (var nested in parent.NestedTypes)
            {
                yield return nested;
                foreach (var sub in EnumerateNestedTypes(nested))
                {
                    yield return sub;
                }
            }
        }

        private static void GenerateDumpCsFromContext(string outputPath, ApplicationAnalysisContext ctx)
        {
            ulong imageBase = GetBinaryImageBase(ctx.Binary);

            using var writer = new StreamWriter(outputPath, false, new UTF8Encoding(false));
            writer.WriteLine("// Generated by SpyTool2 (AssetRipper.Cpp2IL.Core 1.0.9)");
            writer.WriteLine($"// Metadata Version: {ctx.MetadataVersion}");
            writer.WriteLine($"// Binary: {ctx.Binary.GetType().Name} | RawLength: 0x{ctx.Binary.RawLength:X} | ImageBase: 0x{imageBase:X}");
            writer.WriteLine();

            foreach (var asm in ctx.Assemblies)
            {
                writer.WriteLine($"// Image: {asm.CleanAssemblyName}.dll (TopLevelTypes: {asm.TopLevelTypes.Count()})\n");
            }
            writer.WriteLine();

            foreach (var asm in ctx.Assemblies)
            {
                string asmName = asm.CleanAssemblyName;

                foreach (var typeCtx in EnumerateAllTypes(asm))
                {
                    if (typeCtx.Name == "<Module>")
                        continue;

                    writer.WriteLine($"// Assembly: {asmName}");
                    if (!string.IsNullOrEmpty(typeCtx.Namespace))
                        writer.WriteLine($"// Namespace: {typeCtx.Namespace}");

                    var typeHeader = new StringBuilder();
                    var tAttrs = typeCtx.Attributes;
                    typeHeader.Append(GetTypeVisibility(tAttrs));
                    typeHeader.Append(' ');

                    string kind = GetTypeKind(typeCtx);
                    if (kind == "class")
                    {
                        if ((tAttrs & TypeAttributes.Abstract) != 0 && (tAttrs & TypeAttributes.Sealed) != 0)
                            typeHeader.Append("static ");
                        else if ((tAttrs & TypeAttributes.Abstract) != 0)
                            typeHeader.Append("abstract ");
                        else if ((tAttrs & TypeAttributes.Sealed) != 0)
                            typeHeader.Append("sealed ");
                    }

                    typeHeader.Append(kind);
                    typeHeader.Append(' ');
                    typeHeader.Append(typeCtx.Name);

                    if (typeCtx.GenericParameters.Count > 0)
                    {
                        typeHeader.Append('<');
                        typeHeader.Append(string.Join(", ", typeCtx.GenericParameters.Select(gp => gp.Name)));
                        typeHeader.Append('>');
                    }

                    var inheritance = new List<string>();
                    if (typeCtx.BaseType != null &&
                        typeCtx.BaseType.FullName != "System.Object" &&
                        typeCtx.BaseType.FullName != "System.ValueType" &&
                        typeCtx.BaseType.FullName != "System.Enum")
                    {
                        inheritance.Add(typeCtx.BaseType.FullName);
                    }

                    foreach (var iface in typeCtx.InterfaceContexts)
                    {
                        inheritance.Add(iface.FullName);
                    }

                    if (inheritance.Count > 0)
                    {
                        typeHeader.Append(" : ");
                        typeHeader.Append(string.Join(", ", inheritance));
                    }

                    writer.WriteLine(typeHeader.ToString());
                    writer.WriteLine("{");

                    // Поля
                    if (typeCtx.Fields.Count > 0)
                    {
                        writer.WriteLine("    // Fields");
                        foreach (var field in typeCtx.Fields)
                        {
                            var fAttrs = field.Attributes;
                            var sbField = new StringBuilder("    ");
                            sbField.Append(GetFieldVisibility(fAttrs));
                            sbField.Append(' ');

                            if ((fAttrs & FieldAttributes.Literal) != 0)
                                sbField.Append("const ");
                            else if ((fAttrs & FieldAttributes.Static) != 0)
                                sbField.Append("static ");

                            if ((fAttrs & FieldAttributes.InitOnly) != 0)
                                sbField.Append("readonly ");

                            sbField.Append(field.FieldType.FullName);
                            sbField.Append(' ');
                            sbField.Append(field.Name);

                            object? defaultVal = field.BackingData?.DefaultValue;
                            if ((fAttrs & FieldAttributes.Literal) != 0 && defaultVal != null)
                            {
                                sbField.Append(" = ");
                                sbField.Append(FormatDefaultValue(defaultVal));
                            }

                            sbField.Append($"; // 0x{field.Offset:X}");
                            writer.WriteLine(sbField.ToString());
                        }
                        writer.WriteLine();
                    }

                    // Свойства
                    if (typeCtx.Properties.Count > 0)
                    {
                        writer.WriteLine("    // Properties");
                        foreach (var prop in typeCtx.Properties)
                        {
                            var accessors = new List<string>();
                            if (prop.Getter != null) accessors.Add("get;");
                            if (prop.Setter != null) accessors.Add("set;");
                            writer.WriteLine($"    public {prop.PropertyType.FullName} {prop.Name} {{ {string.Join(" ", accessors)} }}");
                        }
                        writer.WriteLine();
                    }

                    // Методы
                    if (typeCtx.Methods.Count > 0)
                    {
                        writer.WriteLine("    // Methods");
                        foreach (var method in typeCtx.Methods)
                        {
                            ulong ptr = method.UnderlyingPointer;
                            ulong rva = ToRva(ptr, imageBase);
                            long rawOffset = 0;
                            if (ptr != 0)
                            {
                                ctx.Binary.TryMapVirtualAddressToRaw(ptr, out rawOffset);
                            }

                            writer.WriteLine($"    // RVA: 0x{rva:X} | VA: 0x{ptr:X} | Offset: 0x{rawOffset:X}");

                            var mAttrs = method.Attributes;
                            var sbMethod = new StringBuilder("    ");
                            sbMethod.Append(GetMethodVisibility(mAttrs));
                            sbMethod.Append(' ');

                            if ((mAttrs & MethodAttributes.Static) != 0)
                                sbMethod.Append("static ");
                            else if ((mAttrs & MethodAttributes.Abstract) != 0)
                                sbMethod.Append("abstract ");
                            else if ((mAttrs & MethodAttributes.Virtual) != 0)
                                sbMethod.Append("virtual ");

                            sbMethod.Append(method.ReturnType.FullName);
                            sbMethod.Append(' ');
                            sbMethod.Append(method.Name);

                            if (method.GenericParameters.Count > 0)
                            {
                                sbMethod.Append('<');
                                sbMethod.Append(string.Join(", ", method.GenericParameters.Select(gp => gp.Name)));
                                sbMethod.Append('>');
                            }

                            sbMethod.Append('(');
                            if (method.Parameters.Count > 0)
                            {
                                var paramStrings = method.Parameters.Select(p => $"{p.ParameterType.FullName} {p.Name}");
                                sbMethod.Append(string.Join(", ", paramStrings));
                            }
                            sbMethod.Append(") { }");

                            writer.WriteLine(sbMethod.ToString());
                        }
                    }

                    writer.WriteLine("}");
                    writer.WriteLine();
                }
            }
        }

        private static string FormatDefaultValue(object? val)
        {
            if (val == null) return "null";
            if (val is string s) return $"\"{s.Replace("\\", "\\\\").Replace("\"", "\\\"")}\"";
            if (val is char c) return $"'{c}'";
            if (val is bool b) return b ? "true" : "false";
            return val.ToString() ?? "null";
        }

        private static void GenerateScriptJsonFromContext(string outputPath, ApplicationAnalysisContext ctx)
        {
            ulong imageBase = GetBinaryImageBase(ctx.Binary);
            var scriptMethods = new List<ScriptMethodEntry>();

            foreach (var asm in ctx.Assemblies)
            {
                foreach (var typeCtx in EnumerateAllTypes(asm))
                {
                    if (typeCtx.Methods.Count == 0)
                        continue;

                    string fullTypeName = typeCtx.FullName;

                    foreach (var method in typeCtx.Methods)
                    {
                        ulong ptr = method.UnderlyingPointer;
                        if (ptr == 0)
                            continue;

                        ulong rva = ToRva(ptr, imageBase);
                        string idaName = $"{fullTypeName}$${method.Name}";

                        var sigBuilder = new StringBuilder();
                        sigBuilder.Append(method.ReturnType.FullName);
                        sigBuilder.Append(' ');
                        sigBuilder.Append(idaName);
                        sigBuilder.Append('(');

                        if (method.Parameters.Count > 0)
                        {
                            sigBuilder.Append(string.Join(", ", method.Parameters.Select(p => $"{p.ParameterType.FullName} {p.Name}")));
                        }

                        sigBuilder.Append(')');

                        scriptMethods.Add(new ScriptMethodEntry
                        {
                            Address = rva,
                            Name = idaName,
                            Signature = sigBuilder.ToString()
                        });
                    }
                }
            }

            var payload = new ScriptJsonPayload
            {
                ScriptMethod = scriptMethods
            };

            var jsonOptions = new JsonSerializerOptions
            {
                WriteIndented = true
            };

            string json = JsonSerializer.Serialize(payload, jsonOptions);
            File.WriteAllText(outputPath, json, new UTF8Encoding(false));
        }

        private sealed class ScriptJsonPayload
        {
            public List<ScriptMethodEntry> ScriptMethod { get; set; } = new();
        }

        private sealed class ScriptMethodEntry
        {
            public ulong Address { get; set; }
            public string Name { get; set; } = string.Empty;
            public string Signature { get; set; } = string.Empty;
        }
    }
}
