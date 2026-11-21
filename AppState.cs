using System;
using System.IO;
using System.Text.Json;

namespace SpyTool2;

public class AppStateData
{
    public string LastGamePath { get; set; } = "";
    public string LastDllName { get; set; } = "";
    public string LastTypeName { get; set; } = "";
    public string LastMethodName { get; set; } = "";
    public int ThemeIndex { get; set; } = 1; // 1 = Тёмная по умолчанию
}

public static class AppState
{
    private static readonly string StatePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "SpyTool2",
        "state.json");

    public static AppStateData Current { get; private set; } = Load();

    private static AppStateData Load()
    {
        try
        {
            if (File.Exists(StatePath))
                return JsonSerializer.Deserialize<AppStateData>(File.ReadAllText(StatePath)) ?? new AppStateData();
        }
        catch { }
        return new AppStateData();
    }

    public static void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(StatePath)!);
            File.WriteAllText(StatePath, JsonSerializer.Serialize(Current));
        }
        catch { }
    }
}
