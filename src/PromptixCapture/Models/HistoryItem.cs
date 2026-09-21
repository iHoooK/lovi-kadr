namespace PromptixCapture.Models;

public sealed class HistoryItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Path { get; set; } = string.Empty;
    public HistoryMediaType Type { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public int? Width { get; set; }
    public int? Height { get; set; }
    public double? DurationSeconds { get; set; }
    [System.Text.Json.Serialization.JsonIgnore]
    public bool Exists => File.Exists(Path);
    [System.Text.Json.Serialization.JsonIgnore]
    public string FileName => System.IO.Path.GetFileName(Path);
    [System.Text.Json.Serialization.JsonIgnore]
    public string Details => Type == HistoryMediaType.Video
        ? DurationSeconds is null ? "Видео" : $"Видео · {TimeSpan.FromSeconds(DurationSeconds.Value):mm\\:ss}"
        : Width is null || Height is null ? "Изображение" : $"{Width} × {Height}";
}
