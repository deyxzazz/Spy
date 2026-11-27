using System;
using System.Collections.Generic;

namespace SpyTool2
{
    public static class Loc
    {
        public static string Current { get; private set; } = "RU";
        public static event Action? LanguageChanged;

        private static readonly Dictionary<string, (string RU, string EN)> _dict = new()
        {
            // Кнопки управления окном (Window chrome)
            ["window.minimize"] = ("Свернуть", "Minimize"),
            ["window.maximize"] = ("Развернуть", "Maximize"),
            ["window.restore"] = ("Восстановить", "Restore"),
            ["window.close"] = ("Закрыть", "Close"),

            // Темы оформления (Themes)
            ["theme.system"] = ("Системная", "System"),
            ["theme.dark"] = ("Тёмная", "Dark"),
            ["theme.light"] = ("Светлая", "Light"),
            ["theme.dracula"] = ("Dracula", "Dracula"),
            ["theme.nord"] = ("Nord", "Nord"),
            ["theme.monokai"] = ("Monokai", "Monokai"),
            ["theme.cyberpunk"] = ("Cyberpunk", "Cyberpunk"),
            ["theme.acid_neon"] = ("Acid Neon", "Acid Neon"),

            // Верхняя панель и общие элементы MainWindow
            ["main.game_path.placeholder"] = ("Путь к папке с игрой или .exe...", "Path to game folder or .exe..."),
            ["main.btn.browse"] = ("Обзор", "Browse"),
            ["main.btn.reset"] = ("Сброс", "Reset"),
            ["main.btn.save_all"] = ("Сохранить всё", "Save All"),
            ["main.lang.tooltip"] = ("Язык интерфейса", "Interface language"),
            ["engine.not_selected"] = ("Игра не выбрана", "No game selected"),
            ["engine.version_unknown"] = ("неизвестна", "unknown"),
            ["engine.info_format"] = ("Unity {0} • {1}", "Unity {0} • {1}"),
            ["common.all"] = ("Все", "All"),
            ["common.cancel"] = ("Отмена", "Cancel"),
            ["common.bytes"] = ("байт", "bytes"),

            // Вкладки MainWindow
            ["main.tab.code"] = ("Код", "Code"),
            ["main.tab.assets"] = ("Ассеты", "Assets"),
            ["main.tab.export"] = ("Экспорт", "Export"),
            ["main.tab.il2cpp_dumper"] = ("Дампер IL2CPP", "IL2CPP Dumper"),
            ["main.tab.about"] = ("О программе", "About"),

            // Вкладка Код (Code tab)
            ["main.tree_search.placeholder"] = ("Поиск классов и методов...", "Search classes and methods..."),
            ["main.btn.add_dll"] = ("+ DLL", "+ DLL"),
            ["main.btn.load_assemblies"] = ("Обновить дерево", "Reload Tree"),
            ["main.btn.find"] = ("Поиск", "Find"),
            ["main.btn.compile_dll"] = ("Скомпилировать DLL", "Compile DLL"),
            ["main.btn.export_cs"] = ("Экспорт .cs", "Export .cs"),
            ["main.menu.edit_method"] = ("Редактировать метод (C#)", "Edit Method (C#)"),
            ["main.menu.edit_class"] = ("Редактировать класс (C#)", "Edit Class (C#)"),
            ["main.menu.stub_method"] = ("Заглушить метод (NOP / Return)", "Stub Method (NOP / Return)"),
            ["dialog.select_game_folder.title"] = ("Выберите папку с игрой или *_Data", "Select game folder or *_Data"),
            ["dialog.select_assemblies.title"] = ("Выберите сборки (.dll)", "Select assemblies (.dll)"),
            ["dialog.select_assemblies.filter"] = ("Сборки .NET / Все файлы", ".NET Assemblies / All files"),
            ["dialog.editor.title"] = ("Редактор кода — {0}", "Code Editor — {0}"),
            ["dialog.editor.compile"] = ("Скомпилировать и внедрить", "Compile & Inject"),

            // Вкладка Ассеты (Assets tab)
            ["main.asset_file.placeholder"] = ("Выберите файл ресурсов...", "Select asset file..."),
            ["main.asset_search.watermark"] = ("Фильтр ассетов...", "Filter assets..."),
            ["main.btn.refresh"] = ("Обновить", "Refresh"),
            ["main.btn.extract_asset"] = ("Извлечь", "Extract"),
            ["main.btn.edit_text"] = ("Редактор текста", "Edit Text"),
            ["main.btn.replace_file"] = ("Заменить файлом", "Replace File"),
            ["main.btn.modified_count"] = ("Изменено: {0}", "Modified: {0}"),
            ["main.grid.path_id"] = ("Path ID", "Path ID"),
            ["main.grid.name"] = ("Имя", "Name"),
            ["main.grid.type"] = ("Тип", "Type"),
            ["main.grid.details"] = ("Детали", "Details"),

            // Вкладка Экспорт (Export tab)
            ["main.rip_output.placeholder"] = ("Папка для экспорта проекта Unity...", "Output folder for Unity project export..."),
            ["main.btn.run_ripper"] = ("Экспортировать проект", "Export Project"),

            // Статусы MainWindow
            ["status.ready"] = ("Готов к работе", "Ready"),
            ["status.error"] = ("Ошибка: {0}", "Error: {0}"),
            ["status.scan_error"] = ("Ошибка сканирования: {0}", "Scan error: {0}"),
            ["status.no_modified_assets"] = ("Нет изменённых ассетов.", "No modified assets."),
            ["status.modified_item"] = ("Изменённый ассет {0}/{1}: {2}", "Modified asset {0}/{1}: {2}"),
            ["status.select_asset"] = ("Сначала выберите ассет в таблице.", "Select an asset in the table first."),
            ["status.select_text_asset"] = ("Выберите TextAsset для редактирования.", "Select a TextAsset to edit."),
            ["status.unavailable_for_type"] = ("Недоступно для типа {0}.", "Unavailable for type {0}."),
            ["status.no_replacement_for_type"] = ("Замена не поддерживается для типа {0}.", "Replacement is not supported for type {0}."),
            ["status.project_cleared"] = ("Проект очищен.", "Project cleared."),
            ["status.select_game_first"] = ("Сначала выберите папку с игрой.", "Select a game folder first."),
            ["status.modified_files"] = ("Изменённые файлы (резервные копии .bak): {0}", "Modified files (.bak backups): {0}"),
            ["status.all_synced"] = ("Все файлы синхронизированы, изменений нет.", "All files synced, no modifications."),
            ["status.assemblies_not_found"] = ("Сборки или метаданные не найдены.", "No assemblies or metadata found."),
            ["status.dll_error"] = ("Ошибка {0}: {1}", "Error in {0}: {1}"),
            ["status.modules_loaded"] = ("Загружено модулей: {0} ({1})", "Loaded modules: {0} ({1})"),
            ["status.modules_not_loaded"] = ("Модули не загружены.", "No modules loaded."),
            ["status.rmb_hint"] = ("Нажмите ПКМ по методу или классу в дереве -> «Редактировать метод (C#)»", "Right-click a method or class in the tree -> 'Edit Method (C#)'"),
            ["status.assets_loaded"] = ("Загружено из {0}: {1} ассетов", "Loaded from {0}: {1} assets"),
            ["status.assets_loaded_legacy"] = ("Ассетов (Unity 3/4 Native): {0}", "Assets (Unity 3/4 Native): {0}"),
            ["status.read_error"] = ("Ошибка чтения {0}: {1}", "Read error in {0}: {1}"),
            ["status.ripper_missing"] = ("Положите AssetRipper в {0} и выберите игру.", "Place AssetRipper in {0} and select a game."),

            // Вкладка Cpp2IL (Cpp2ILTab)
            ["cpp2il.label.ga"] = ("GameAssembly:", "GameAssembly:"),
            ["cpp2il.label.meta"] = ("Metadata (.dat):", "Metadata (.dat):"),
            ["cpp2il.label.ver"] = ("Версия Unity:", "Unity Version:"),
            ["cpp2il.label.out"] = ("Папка вывода:", "Output Folder:"),

            ["cpp2il.watermark.ga"] = ("Путь к GameAssembly.dll (или libil2cpp.so)", "Path to GameAssembly.dll (or libil2cpp.so)"),
            ["cpp2il.watermark.meta"] = ("Путь к global-metadata.dat", "Path to global-metadata.dat"),
            ["cpp2il.watermark.ver"] = ("Версия Unity (например: 2022.3.49f1 или 6000.0.0)", "Unity version (e.g. 2022.3.49f1 or 6000.0.0)"),
            ["cpp2il.watermark.out"] = ("Папка вывода (по умолчанию IL2CPP_Dump рядом с игрой)", "Output folder (default: IL2CPP_Dump next to game)"),

            ["cpp2il.chk.dummy_dll"] = ("Генерировать DummyDll/*.dll", "Generate DummyDll/*.dll"),
            ["cpp2il.chk.dump_cs"] = ("Генерировать dump.cs (классы, поля, методы с RVA)", "Generate dump.cs (classes, fields, methods with RVA)"),
            ["cpp2il.chk.script_json"] = ("Генерировать script.json (для IDA / Ghidra)", "Generate script.json (for IDA / Ghidra)"),
            ["cpp2il.chk.auto_load"] = ("Сразу загрузить DummyDll в дерево Spy", "Auto-load DummyDll into Spy tree"),

            ["cpp2il.btn.auto_fill"] = ("Подставить из текущей игры", "Auto-Fill from Current Game"),
            ["cpp2il.btn.browse"] = ("Обзор...", "Browse..."),
            ["cpp2il.btn.run"] = ("Запустить дамп IL2CPP", "Run IL2CPP Dump"),
            ["cpp2il.btn.open_out"] = ("Открыть папку дампа", "Open Dump Folder"),

            ["cpp2il.dialog.ga_title"] = ("Выберите GameAssembly.dll", "Select GameAssembly.dll"),
            ["cpp2il.dialog.meta_title"] = ("Выберите global-metadata.dat", "Select global-metadata.dat"),
            ["cpp2il.dialog.out_title"] = ("Выберите папку вывода для дампа", "Select output folder for dump"),

            ["cpp2il.ver.unknown"] = ("не определена", "not detected"),
            ["cpp2il.log.autofill_ok"] = ("Пути подставлены. Версия Unity: {0}", "Paths auto-filled. Unity version: {0}"),
            ["cpp2il.status.autofill_ok"] = ("Cpp2IL: пути подставлены из текущей игры.", "Cpp2IL: paths auto-filled from current game."),
            ["cpp2il.log.no_datadir"] = ("Каталог данных игры не задан или не существует.", "Game data directory is not set or does not exist."),
            ["cpp2il.log.autofill_err"] = ("Ошибка автоподстановки: {0}", "Auto-fill error: {0}"),
            ["cpp2il.log.out_not_found"] = ("Папка вывода ещё не создана или путь не указан.", "Output folder has not been created yet or path is empty."),
            ["cpp2il.log.ga_not_found"] = ("Ошибка: файл GameAssembly.dll не найден.", "Error: GameAssembly.dll file not found."),
            ["cpp2il.status.ga_not_found"] = ("Cpp2IL: не найден GameAssembly.dll", "Cpp2IL: GameAssembly.dll not found"),
            ["cpp2il.log.meta_not_found"] = ("Ошибка: файл global-metadata.dat не найден.", "Error: global-metadata.dat file not found."),
            ["cpp2il.status.meta_not_found"] = ("Cpp2IL: не найден global-metadata.dat", "Cpp2IL: global-metadata.dat not found"),
            ["cpp2il.err.ver_empty"] = ("Версия Unity не указана.", "Unity version is not specified."),
            ["cpp2il.log.ver_parse_err"] = ("Ошибка парсинга версии Unity ('{0}'): {1}", "Failed to parse Unity version ('{0}'): {1}"),
            ["cpp2il.status.ver_invalid"] = ("Cpp2IL: некорректная версия Unity", "Cpp2IL: invalid Unity version"),
            ["cpp2il.status.running"] = ("Cpp2IL: выполняется дамп...", "Cpp2IL: running dump..."),
            ["cpp2il.log.init"] = ("Инициализация LibCpp2IL (Unity {0})...", "Initializing LibCpp2IL (Unity {0})..."),
            ["cpp2il.log.meta_loaded"] = ("Метаданные загружены (v{0}). Сборок: {1}", "Metadata loaded (v{0}). Assemblies: {1}"),
            ["cpp2il.log.inject_attrs"] = ("Инъекция атрибутов (AttributeInjectorProcessingLayer)...", "Injecting attributes (AttributeInjectorProcessingLayer)..."),
            ["cpp2il.log.gen_dummy"] = ("Генерация DummyDll сборок (AsmResolverDllOutputFormatEmpty)...", "Generating DummyDll assemblies (AsmResolverDllOutputFormatEmpty)..."),
            ["cpp2il.log.dummy_saved"] = ("DummyDll сохранены ({0} файлов) в {1}", "DummyDll saved ({0} files) to {1}"),
            ["cpp2il.log.gen_dumpcs"] = ("Генерация dump.cs из ApplicationAnalysisContext...", "Generating dump.cs from ApplicationAnalysisContext..."),
            ["cpp2il.log.dumpcs_saved"] = ("Файл dump.cs успешно записан: {0}", "dump.cs successfully written: {0}"),
            ["cpp2il.log.gen_scriptjson"] = ("Генерация script.json из ApplicationAnalysisContext...", "Generating script.json from ApplicationAnalysisContext..."),
            ["cpp2il.log.scriptjson_saved"] = ("Файл script.json успешно записан: {0}", "script.json successfully written: {0}"),
            ["cpp2il.log.autoload"] = ("Загрузка сгенерированных DummyDll в дерево Spy...", "Loading generated DummyDll into Spy tree..."),
            ["cpp2il.log.done"] = ("Дамп IL2CPP успешно завершён!", "IL2CPP dump completed successfully!"),
            ["cpp2il.status.done"] = ("Cpp2IL: дамп успешно завершён.", "Cpp2IL: dump completed successfully."),
            ["cpp2il.log.crit_err"] = ("КРИТИЧЕСКАЯ ОШИБКА: {0}", "CRITICAL ERROR: {0}"),
            ["cpp2il.status.crit_err"] = ("Cpp2IL ошибка: {0}", "Cpp2IL error: {0}"),
            // Вкладка Экспорт (Project Exporter Tab)
            ["exp.label.out"] = ("Папка экспорта:", "Output folder:"),
            ["exp.watermark.out"] = ("Папка для экспорта проекта Unity...", "Output folder for Unity project export..."),
            ["exp.btn.browse"] = ("Обзор...", "Browse..."),
            ["exp.btn.open_out"] = ("Открыть папку", "Open Folder"),
            ["exp.btn.run"] = ("Экспортировать проект", "Export Project"),
            ["exp.group.scripts"] = ("Экспорт скриптов (C#)", "Scripts Export (C#)"),
            ["exp.label.filter"] = ("Фильтр сборок:", "Assembly filter:"),
            ["exp.filter.assembly_csharp"] = ("Только Assembly-CSharp (быстро)", "Assembly-CSharp only (fast)"),
            ["exp.filter.game_only"] = ("Все сборки игры (без Unity/System)", "Game assemblies only (no Unity/System)"),
            ["exp.filter.all"] = ("Все сборки (*.dll)", "All assemblies (*.dll)"),
            ["exp.filter.none"] = ("Не экспортировать скрипты", "Do not export scripts"),
            ["exp.chk.decompile_cs"] = ("Декомпилировать в .cs (папка Scripts)", "Decompile to .cs (Scripts folder)"),
            ["exp.chk.save_dummydll"] = ("Сохранить DummyDll (.dll)", "Save DummyDll (.dll)"),
            ["exp.chk.autoload_code"] = ("Сразу загрузить во вкладку «Код»", "Auto-load into Code tab"),
            ["exp.group.assets"] = ("Экспорт ассетов (Assets)", "Assets Export"),
            ["exp.chk.textures"] = ("Текстуры (Texture2D → .png)", "Textures (Texture2D → .png)"),
            ["exp.chk.text_assets"] = ("Текстовые ассеты (TextAsset → .txt/.json)", "TextAssets (TextAsset → .txt/.json)"),
            ["exp.chk.raw_assets"] = ("Дамп остальных ассетов (.bin)", "Dump other assets (.bin)"),
            ["exp.status.no_game"] = ("Сначала выберите папку с игрой!", "Select a game folder first!"),
            ["exp.status.running"] = ("Выполняется экспорт проекта...", "Exporting project..."),
            ["exp.status.done"] = ("Экспорт завершён! Скриптов: {0}, ассетов: {1}", "Export complete! Scripts: {0}, assets: {1}"),
            ["exp.status.err"] = ("Ошибка экспорта: {0}", "Export error: {0}"),
            ["exp.log.start"] = ("Начат экспорт проекта из: {0}", "Starting project export from: {0}"),
            ["exp.log.out_dir"] = ("Папка назначения: {0}", "Output directory: {0}"),
            ["exp.log.il2cpp_dump"] = ("Обнаружен IL2CPP ({0}). Генерация DummyDll через Cpp2IL...", "IL2CPP detected ({0}). Generating DummyDll via Cpp2IL..."),
            ["exp.log.mono_found"] = ("Обнаружен Mono. Найдено сборок в Managed: {0}", "Mono detected. Found assemblies in Managed: {0}"),
            ["exp.log.filtered_dlls"] = ("Выбрано сборок по фильтру: {0} ({1})", "Assemblies matched filter: {0} ({1})"),
            ["exp.log.decompiling"] = ("Декомпиляция {0} в C#...", "Decompiling {0} to C#..."),
            ["exp.log.decompiled_ok"] = ("Готово: {0} → экспортировано {1} .cs файлов.", "Done: {0} → exported {1} .cs files."),
            ["exp.log.assets_scan"] = ("Сканирование файлов ресурсов ({0})...", "Scanning asset files ({0})..."),
            ["exp.log.assets_done"] = ("Экспорт ассетов завершён: текстур — {0}, текстов — {1}, прочих — {2}.", "Assets export complete: textures — {0}, text — {1}, raw — {2}."),
            ["exp.log.autoload"] = ("Загрузка сборок во вкладку «Код»...", "Loading assemblies into Code tab..."),
            ["exp.log.done"] = ("=== Экспорт полностью завершён за {0:F1} сек. ===", "=== Export completed in {0:F1}s ==="),
            ["exp.log.out_not_found"] = ("Папка экспорта ещё не создана.", "Output directory does not exist yet."),
            // Совместимость со старыми ключами
            ["PathPlaceholder"] = ("Выберите папку или файл...", "Select a folder or file..."),
            ["Browse"] = ("Обзор", "Browse"),
            ["Reset"] = ("Сброс", "Reset"),
            ["SaveAll"] = ("Сохранить всё", "Save All"),
            ["Theme"] = ("Тема", "Theme"),
            ["Lang"] = ("Язык", "Lang"),
            ["TabCode"] = ("Код", "Code"),
            ["TabAssets"] = ("Ассеты", "Assets"),
            ["TabExport"] = ("Экспорт", "Export"),
            ["TabDumper"] = ("Дампер", "Dumper"),
            ["TabCredits"] = ("О программе", "Credits"),
            ["SearchPlaceholder"] = ("Поиск по элементам...", "Search items..."),
            ["Ready"] = ("Готов к работе", "Ready"),
            ["LoadedModules"] = ("Загружено модулей: {0}", "Loaded modules: {0}"),
            ["RunDump"] = ("Запустить дамп", "Run Dump"),
            ["AutoFill"] = ("Автозаполнение", "Auto-Fill"),
            ["OpenFolder"] = ("Открыть папку", "Open Folder"),
            ["GenDummyDll"] = ("Создать DummyDll", "Generate DummyDll"),
            ["GenDumpCs"] = ("Создать dump.cs", "Generate dump.cs"),
            ["GenScriptJson"] = ("Создать script.json", "Generate script.json"),
            ["AutoLoadDll"] = ("Автозагрузка DLL", "Auto-Load DLL"),
            ["CreditsTitle"] = ("SpyTool2 — Инструмент анализа и дампа", "SpyTool2 — Analysis & Dumping Tool"),
            ["CreditsDesc"] = ("Утилита для просмотра кода, извлечения ассетов и генерации дампов.", "Utility for code inspection, asset extraction, and dump generation."),
            ["CreditsVersion"] = ("Версия 1.0", "Version 1.0")
        };
        public static void SetLanguage(string lang)
        {
            if (Current == lang) return;
            Current = lang;
            LanguageChanged?.Invoke();
        }
        public static string T(string key)
        {
            if (_dict.TryGetValue(key, out var val))
            {
                return Current == "EN" ? val.EN : val.RU;
            }
            return key;
        }
    }
}