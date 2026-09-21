namespace PromptixCapture.Models;

public enum CaptureMode
{
    Region,
    ActiveWindow,
    CurrentMonitor,
    VirtualDesktop,
    LastRegion
}

public enum CapturePurpose
{
    Screenshot,
    ScrollingScreenshot,
    Video
}

public enum ImageFileFormat
{
    Png,
    Jpeg
}

public enum AnnotationTool
{
    Select,
    Pen,
    Highlighter,
    Line,
    Arrow,
    Rectangle,
    Ellipse,
    Text,
    Number,
    Blur,
    Pixelate,
    Redact
}

public enum VideoQuality
{
    Economy,
    Normal,
    High
}

public enum VideoRecorderState
{
    Idle,
    Starting,
    Recording,
    Paused,
    Stopping,
    Failed
}

public enum HistoryMediaType
{
    Screenshot,
    ScrollingScreenshot,
    Video
}

public enum ScrollingCaptureState
{
    Ready,
    Capturing,
    Paused,
    Stitching,
    Completed,
    Failed,
    Cancelled
}
