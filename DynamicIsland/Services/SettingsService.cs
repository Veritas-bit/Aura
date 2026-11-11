using System.IO;
using System.Text.Json;
using DynamicIsland.Models;

namespace DynamicIsland.Services;

/// <summary>Загрузка/сохранение настроек в %APPDATA%\AURA\settings.json</summary>
public static class SettingsService
{
    public static AppSettings Current { get; private set; } = new();
    public static event Action? Changed;

    public static string DataDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AURA");

    private static string FilePath => Path.Combine(DataDir, "settings.json");

    public static void Load()
    {
        MigrateOldDataDir();
        try
        {
            if (File.Exists(FilePath))
            {
                var json = File.ReadAllText(FilePath);
                Current = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
                MigrateStreamIcons();
            }
        }
        catch
        {
            Current = new AppSettings();
        }
    }

    /// <summary>Переезд с %APPDATA%\DynamicIsland на %APPDATA%\AURA (копирование один раз).</summary>
    private static void MigrateOldDataDir()
    {
        try
        {
            var oldDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DynamicIsland");
            if (!Directory.Exists(oldDir) || Directory.Exists(DataDir)) return;
            Directory.CreateDirectory(DataDir);
            foreach (var file in Directory.GetFiles(oldDir))
                File.Copy(file, Path.Combine(DataDir, Path.GetFileName(file)), overwrite: false);
        }
        catch { }
    }

    // Старые эмодзи-пресеты → векторные иконки
    private static void MigrateStreamIcons()
    {
        var map = new Dictionary<string, string>
        {
            ["🎬"] = "ic:radio_tower", ["▶️"] = "ic:play", ["🟣"] = "ic:zap",
            ["💬"] = "ic:send", ["🧑‍💻"] = "ic:cpu"
        };
        bool changed = false;
        foreach (var a in Current.StreamActions)
            if (map.TryGetValue(a.Icon, out var key)) { a.Icon = key; changed = true; }
        if (changed) Save();
    }

    public static void Save()
    {
        try
        {
            Directory.CreateDirectory(DataDir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(Current,
                new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* нет прав на запись — молча пропускаем */ }
    }

    /// <summary>Сообщить всем подписчикам, что настройки изменились (и сохранить).</summary>
    public static void NotifyChanged()
    {
        Save();
        Changed?.Invoke();
    }

    public static void Reset()
    {
        Current = new AppSettings();
        NotifyChanged();
    }
}
