using System.Drawing;

namespace PromptixCapture.Services;

public readonly record struct ScrollFrameMatch(MatchResult Match,Rectangle Viewport,bool SignificantMotion);

// Find the part of a selected frame that actually scrolls, including scrollable
// controls nested inside an otherwise stationary application window.
public static class ScrollingFrameMatcher
{
    public static ScrollFrameMatch Match(byte[] previous,byte[] next,int width,int height,int focusX)
    {
        var whole=new Rectangle(0,0,width,height);
        var full=FrameMatcher.Match(previous,next,width,height);
        if(full.Reliable && !full.Unchanged)return new(full,whole,true);

        int bandWidth=Math.Min(width,Math.Max(96,width/4));
        int bandLeft=Math.Clamp(focusX-bandWidth/2,0,width-bandWidth);
        var focused=MotionBounds(previous,next,width,height,bandLeft,bandLeft+bandWidth);
        if(focused is null)return new(full,whole,false);

        var global=MotionBounds(previous,next,width,height,0,width);
        var spread=global is { } bounds && bounds.Width>=width*3/5
            ? new Rectangle(0,bounds.Top,width,bounds.Height)
            : global;
        foreach(var moving in new[]{spread,focused})
        {
            if(moving is not { } area || area.Width<64 || area.Height<128)continue;
            var cropPrevious=Crop(previous,width,area);
            var cropNext=Crop(next,width,area);
            var partial=FrameMatcher.Match(cropPrevious,cropNext,area.Width,area.Height);
            if(partial.Reliable && !partial.Unchanged)return new(partial,area,true);
        }
        return new(full,whole,true);
    }

    public static byte[] Crop(byte[] pixels,int sourceWidth,Rectangle area)
    {
        var result=new byte[checked(area.Width*area.Height)];
        for(int y=0;y<area.Height;y++)
            Buffer.BlockCopy(pixels,checked((area.Top+y)*sourceWidth+area.Left),result,y*area.Width,area.Width);
        return result;
    }

    private static Rectangle? MotionBounds(byte[] previous,byte[] next,int width,int height,int left,int right)
    {
        int sx=Math.Max(1,(right-left)/320),sy=Math.Max(1,height/320);
        int[] columns=new int[width],rows=new int[height];
        for(int y=0;y<height;y+=sy)
        for(int x=left;x<right;x+=sx)
        {
            int at=y*width+x;
            if(Math.Abs(previous[at]-next[at])<=12)continue;
            columns[x]++;rows[y]++;
        }
        int columnThreshold=Math.Max(4,(height+sy-1)/sy/30);
        int rowThreshold=Math.Max(4,(right-left+sx-1)/sx/30);
        int x0=right,x1=left,y0=height,y1=0;
        for(int x=left;x<right;x+=sx)if(columns[x]>=columnThreshold){x0=Math.Min(x0,x);x1=Math.Max(x1,x);}
        for(int y=0;y<height;y+=sy)if(rows[y]>=rowThreshold){y0=Math.Min(y0,y);y1=Math.Max(y1,y);}
        if(x1<x0 || y1<y0)return null;
        const int padding=8;
        return Rectangle.FromLTRB(Math.Max(0,x0-padding),Math.Max(0,y0-padding),
            Math.Min(width,x1+sx+padding),Math.Min(height,y1+sy+padding));
    }
}
