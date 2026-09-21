using System.Text.Json.Serialization;

namespace PromptixCapture.Models;

public sealed class AppSettings
{
    public int SchemaVersion { get; set; } = 1;
    public GeneralSettings General { get; set; } = new();
    public HotkeySettings Hotkeys { get; set; } = new();
    public ScreenshotSettings Screenshot { get; set; } = new();
    public ScrollingSettings Scrolling { get; set; } = new();
    public VideoSettings Video { get; set; } = new();

    public static AppSettings CreateDefault() => new();
}

public sealed class GeneralSettings
{
    public bool StartWithWindows { get; set; }
    public bool StartMinimized { get; set; } = true;
    public bool ShowNotifications { get; set; } = true;
    public bool PlaySounds { get; set; } = true;
    public bool QuietMode { get; set; }
    public string Theme { get; set; } = "System";
    public string LeftClickAction { get; set; } = "QuickPanel";
    public string Language { get; set; } = "ru-RU";
}

public sealed class HotkeySettings
{
    public string Region { get; set; } = "PrintScreen";
    public string ActiveWindow { get; set; } = "Alt+PrintScreen";
    public string CurrentMonitor { get; set; } = "Shift+PrintScreen";
    public string VirtualDesktop { get; set; } = "Ctrl+PrintScreen";
    public string ScrollingScreenshot { get; set; } = "Ctrl+Alt+PrintScreen";
    public string VideoStartStop { get; set; } = "Ctrl+Shift+PrintScreen";
    public string VideoPauseResume { get; set; } = "Shift+Pause";
}

public sealed class ScreenshotSettings
{
    public string Folder { get; set; } = "";
    public string FileNameTemplate { get; set; } = "Screenshot_{yyyy-MM-dd}_{HH-mm-ss}";
    [JsonConverter(typeof(JsonStringEnumConverter<ImageFileFormat>))]
    public ImageFileFormat Format { get; set; } = ImageFileFormat.Png;
    public int JpegQuality { get; set; } = 90;
    public bool CopyToClipboard { get; set; } = true;
    public bool AutoSave { get; set; }
    public bool OpenEditor { get; set; }
    public bool RememberLastRegion { get; set; } = true;
    public bool IncludeCursor { get; set; }
    [JsonConverter(typeof(JsonStringEnumConverter<AnnotationTool>))]
    public AnnotationTool DefaultTool { get; set; } = AnnotationTool.Arrow;
    public SerializableRectangle? LastRegion { get; set; }
}

public sealed class ScrollingSettings
{
    public int SettleDelayMs { get; set; } = 420;
    public int WheelDelta { get; set; } = 600;
    public int MaxOutputHeight { get; set; } = 40_000;
    public int UnchangedFramesToStop { get; set; } = 3;
    public bool OpenEditor { get; set; } = true;
}

public sealed class VideoSettings
{
    public string Folder { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "LoviKadr");
    public string FileNameTemplate { get; set; } = "Video_{yyyy-MM-dd}_{HH-mm-ss}";
    public int FramesPerSecond { get; set; } = 30;
    [JsonConverter(typeof(JsonStringEnumConverter<VideoQuality>))]
    public VideoQuality Quality { get; set; } = VideoQuality.Normal;
    public bool CaptureSystemAudio { get; set; } = true;
    public bool CaptureMicrophone { get; set; } = true;
    public string? MicrophoneDeviceName { get; set; }
    public bool IncludeCursor { get; set; } = true;
    public int CountdownSeconds { get; set; } = 3;
    public bool OpenFolderAfterRecording { get; set; }
}

public sealed class SerializableRectangle
{
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }

    public System.Drawing.Rectangle ToRectangle() => new(X, Y, Width, Height);

    public static SerializableRectangle FromRectangle(System.Drawing.Rectangle rectangle) => new()
    {
        X = rectangle.X,
        Y = rectangle.Y,
        Width = rectangle.Width,
        Height = rectangle.Height
    };
}
