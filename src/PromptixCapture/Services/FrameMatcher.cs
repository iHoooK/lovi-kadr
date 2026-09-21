namespace PromptixCapture.Services;

public readonly record struct MatchResult(int Shift, double Error, bool Unchanged, bool Reliable);

// Pure managed matcher: arrays use one luminance byte per pixel. No native dependency.
public static class FrameMatcher
{
    public static MatchResult Match(byte[] previous,byte[] next,int width,int height)
    {
        if(width<16 || height<64 || previous.Length!=width*height || next.Length!=previous.Length)throw new ArgumentException("Неверный размер кадра.");
        int dx=Math.Max(1,width/56),dy=Math.Max(1,height/65),left=Math.Max(2,width/12),right=width-left;
        double Score(int shift)
        {
            long error=0;int count=0;
            // Exclude top/bottom strips from matching, but not from export.
            int top=Math.Max(8,height/10),bottom=Math.Min(height-shift-8,height-height/12);
            if(bottom-top<height/8)return double.MaxValue;
            for(int y=top;y<bottom;y+=dy)for(int x=left;x<right;x+=dx)
            {error+=Math.Abs(previous[(y+shift)*width+x]-next[y*width+x]);count++;}
            return count==0?double.MaxValue:(double)error/count;
        }
        double unchanged=Score(0);
        if(unchanged<.65)return new(0,unchanged,true,true);
        int best=0;double error=double.MaxValue;
        var scores=new List<(int Shift,double Error)>();
        for(int shift=1;shift<=height*3/4;shift++)
        {
            double score=Score(shift);scores.Add((shift,score));if(score<error){error=score;best=shift;}
        }
        double alternative=scores.Where(s=>Math.Abs(s.Shift-best)>8).Select(s=>s.Error).DefaultIfEmpty(double.MaxValue).Min();
        bool reliable=best>0 && error<9 && (alternative>error+1 || alternative>error*1.65+0.2);
        return new(best,error,false,reliable);
    }
}
