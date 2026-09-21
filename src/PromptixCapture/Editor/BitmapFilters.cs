using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PromptixCapture.Models;

namespace PromptixCapture.Editor;

public static class BitmapFilters
{
    public static BitmapSource Apply(BitmapSource source, Int32Rect region, AnnotationTool tool, int size)
    {
        var crop=new CroppedBitmap(source,region);
        var converted=new FormatConvertedBitmap(crop,PixelFormats.Bgra32,null,0);
        int w=region.Width,h=region.Height,stride=w*4;
        var data=new byte[stride*h]; converted.CopyPixels(data,stride,0);
        var output=new byte[data.Length];
        size=Math.Clamp(size,2,32);
        if(tool==AnnotationTool.Pixelate)
        {
            for(int y=0;y<h;y+=size) for(int x=0;x<w;x+=size)
            {
                int r=Math.Min(w,x+size),b=Math.Min(h,y+size),count=(r-x)*(b-y);
                long blue=0,green=0,red=0;
                for(int yy=y;yy<b;yy++) for(int xx=x;xx<r;xx++) { int k=yy*stride+xx*4; blue+=data[k];green+=data[k+1];red+=data[k+2]; }
                for(int yy=y;yy<b;yy++) for(int xx=x;xx<r;xx++) {int k=yy*stride+xx*4;output[k]=(byte)(blue/count);output[k+1]=(byte)(green/count);output[k+2]=(byte)(red/count);output[k+3]=255;}
            }
        }
        else
        {
            var temp=new byte[data.Length];
            for(int y=0;y<h;y++) for(int c=0;c<3;c++)
            {
                int sum=0; for(int x=0;x<=Math.Min(w-1,size);x++) sum+=data[y*stride+x*4+c];
                for(int x=0;x<w;x++) {int left=Math.Max(0,x-size),right=Math.Min(w-1,x+size);temp[y*stride+x*4+c]=(byte)(sum/(right-left+1));if(x-size>=0)sum-=data[y*stride+(x-size)*4+c];if(x+size+1<w)sum+=data[y*stride+(x+size+1)*4+c];}
            }
            for(int x=0;x<w;x++) for(int c=0;c<3;c++)
            {
                int sum=0;for(int y=0;y<=Math.Min(h-1,size);y++)sum+=temp[y*stride+x*4+c];
                for(int y=0;y<h;y++){int top=Math.Max(0,y-size),bottom=Math.Min(h-1,y+size);output[y*stride+x*4+c]=(byte)(sum/(bottom-top+1));output[y*stride+x*4+3]=255;if(y-size>=0)sum-=temp[(y-size)*stride+x*4+c];if(y+size+1<h)sum+=temp[(y+size+1)*stride+x*4+c];}
            }
        }
        var result=BitmapSource.Create(w,h,96,96,PixelFormats.Bgra32,null,output,stride);result.Freeze();return result;
    }
}
