namespace PromptixCapture.Services;

internal readonly record struct VideoRegionSize(int Width,int Height)
{
    // The video encoder keeps its initial frame size. Resizing the selection
    // at the same aspect ratio lets one screen source cover every output pixel.
    public static VideoRegionSize Scale(int baseWidth,int baseHeight,double requestedScale,double maxWidth,double maxHeight)
    {
        if(baseWidth<64||baseHeight<64||maxWidth<64||maxHeight<64)
            throw new ArgumentOutOfRangeException(nameof(maxWidth));
        var minimum=Math.Max(64d/baseWidth,64d/baseHeight);
        var maximum=Math.Min(maxWidth/baseWidth,maxHeight/baseHeight);
        if(maximum<minimum)throw new ArgumentOutOfRangeException(nameof(maxWidth));
        var scale=Math.Clamp(requestedScale,minimum,maximum);
        return new VideoRegionSize(Math.Max(64,(int)Math.Floor(baseWidth*scale/2)*2),
            Math.Max(64,(int)Math.Floor(baseHeight*scale/2)*2));
    }
}
