using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace SpyTool2;

public sealed class AppStateData
{
    public int ThemeIndex { get; set; } = 0;
    public string Language { get; set; } = "RU";
    public string LastGamePath { get; set; } = "";
    public string LastDllName { get; set; } = "";
    public string LastTypeName { get; set; } = "";
    public string LastMethodName { get; set; } = "";
    public double WindowWidth { get; set; } = 1380;
    public double WindowHeight { get; set; } = 820;
    public bool IsMaximized { get; set; } = false;
    public List<string> RecentGames { get; set; } = new();
}


public static class AppState
{
    private static readonly string FilePath = Path.Combine(AppContext.BaseDirectory, "spy_state.json");
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public static AppStateData Current { get; private set; } = Load();

    public static AppStateData Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var data = JsonSerializer.Deserialize<AppStateData>(File.ReadAllText(FilePath), JsonOpts);
                if (data != null)
                {
                    data.RecentGames = data.RecentGames
                        .Where(p => !string.IsNullOrWhiteSpace(p) && Directory.Exists(p))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .Take(8)
                        .ToList();
                    Current = data;
                    return data;
                }
            }
        }
        catch { }
        Current = new AppStateData();
        return Current;
    }

    public static void Save()
    {
        try { File.WriteAllText(FilePath, JsonSerializer.Serialize(Current, JsonOpts)); }
        catch { }
    }

    public static void AddRecentGame(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path)) return;
        Current.LastGamePath = path;
        Current.RecentGames.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        Current.RecentGames.Insert(0, path);
        if (Current.RecentGames.Count > 8)
            Current.RecentGames.RemoveRange(8, Current.RecentGames.Count - 8);
        Save();
    }
}
