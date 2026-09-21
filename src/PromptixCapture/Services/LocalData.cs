using System.Text.Json;
using PromptixCapture.Models;

namespace PromptixCapture.Services;

public static class LocalData
{
    public static string Folder { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LoviKadr");
    public static JsonSerializerOptions Json { get; } = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    public static void Write<T>(string name, T value)
    {
        Directory.CreateDirectory(Folder);
        var path = Path.Combine(Folder, name);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temp, JsonSerializer.Serialize(value, Json)); File.Move(temp, path, true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    public static T Clone<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, Json), Json)!;
}

public sealed class SettingsService
{
    public AppSettings Current { get; private set; } = AppSettings.CreateDefault();
    public void Load()
    {
        var path = Path.Combine(LocalData.Folder, "settings.json");
        if (!File.Exists(path)) return;
        try
        {
            Current = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), LocalData.Json) ?? throw new JsonException();
            Validate(Current);
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or IOException)
        {
            AppLog.Error("Settings load", ex);
            File.Copy(path, path + ".broken-" + DateTime.Now.ToString("yyyyMMdd-HHmmssfff"), false);
            Current = AppSettings.CreateDefault();
        }
    }
    public void Save(AppSettings settings)
    {
        Validate(settings);
        LocalData.Write("settings.json", settings);
        Current = settings;
    }
    public static void Validate(AppSettings s)
    {
        if (s.SchemaVersion != 1 || s.General is null || s.Hotkeys is null || s.Screenshot is null || s.Scrolling is null || s.Video is null)
            throw new ArgumentException("Неверная версия или структура настроек.");
        if (!Enum.IsDefined(s.Screenshot.Format) || !Enum.IsDefined(s.Screenshot.DefaultTool) || !Enum.IsDefined(s.Video.Quality) ||
            s.General.LeftClickAction is not ("QuickPanel" or "Screenshot" or "Settings"))
            throw new ArgumentException("Неизвестный формат, инструмент или действие в настройках.");
        if (s.Screenshot.JpegQuality is < 40 or > 100 || s.Video.FramesPerSecond is not (30 or 60) ||
            s.Video.CountdownSeconds is not (0 or 3 or 5) || s.Scrolling.SettleDelayMs is < 150 or > 2000 ||
            s.Scrolling.WheelDelta is < 120 or > 960 || s.Scrolling.MaxOutputHeight is < 1000 or > 100000 ||
            s.Scrolling.UnchangedFramesToStop is < 2 or > 6)
            throw new ArgumentException("Один из параметров находится за допустимыми границами.");
        foreach (var path in new[] { s.Screenshot.Folder, s.Video.Folder })
            if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path)) throw new ArgumentException("Нужен абсолютный путь папки.");
        FileNames.Format(s.Screenshot.FileNameTemplate, DateTime.Now, 1920, 1080);
        FileNames.Format(s.Video.FileNameTemplate, DateTime.Now, 1920, 1080);
    }
}

public static class AppLog
{
    private static readonly object Gate = new();
    public static string PathName => Path.Combine(LocalData.Folder, "logs", "lovi-kadr.log");
    // Do not log exception messages/paths: these may contain private text or usernames.
    public static void Error(string operation, Exception ex)
    {
        lock (Gate)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(PathName)!);
                if (File.Exists(PathName) && new FileInfo(PathName).Length > 2_000_000) File.Move(PathName, PathName + ".previous", true);
                File.AppendAllText(PathName, $"{DateTime.UtcNow:O} {operation}: {ex.GetType().Name}, HRESULT={ex.HResult:X8}\n");
            }
            catch { /* Logging must not make a recoverable failure fatal. */ }
        }
    }
}

public sealed class HistoryService
{
    public List<HistoryItem> Items { get; private set; } = new();
    public void Load()
    {
        try
        {
            var path = Path.Combine(LocalData.Folder, "history.json");
            if (File.Exists(path)) Items = JsonSerializer.Deserialize<List<HistoryItem>>(File.ReadAllText(path), LocalData.Json) ?? new();
        }
        catch (Exception ex) { AppLog.Error("History load", ex); }
    }
    public void Add(HistoryItem item) { Items.Insert(0, item); Items = Items.Take(500).ToList(); Save(); }
    public void Remove(HistoryItem item) { Items.Remove(item); Save(); }
    public void Cleanup() { Items.RemoveAll(i => !i.Exists); Save(); }
    private void Save() => LocalData.Write("history.json", Items);
}
