using System.IO;
using System.Runtime.InteropServices;
using Drawing=System.Drawing;
namespace CakePet.Windows;
internal static class TrayIcon
{
    [DllImport("user32.dll")]private static extern bool DestroyIcon(nint handle);
    public static Drawing.Icon Load()
    {
        var path=Path.Combine(AppContext.BaseDirectory,"assets","tray.png");
        if(!File.Exists(path))path=Path.Combine(AppContext.BaseDirectory,"assets","default","安静","sit_clam-1.png");
        using var original=new Drawing.Bitmap(path);
        int left=original.Width,top=original.Height,right=-1,bottom=-1;
        for(int y=0;y<original.Height;y++)for(int x=0;x<original.Width;x++)if(original.GetPixel(x,y).A>8){left=Math.Min(left,x);top=Math.Min(top,y);right=Math.Max(right,x);bottom=Math.Max(bottom,y);}
        var crop=right<left?new Drawing.Rectangle(0,0,original.Width,original.Height):new Drawing.Rectangle(left,top,right-left+1,bottom-top+1);
        using var iconImage=new Drawing.Bitmap(64,64,Drawing.Imaging.PixelFormat.Format32bppArgb);
        using(var g=Drawing.Graphics.FromImage(iconImage))
        {
            g.Clear(Drawing.Color.Transparent);g.InterpolationMode=Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            var scale=60.0/Math.Max(crop.Width,crop.Height);var w=(float)(crop.Width*scale);var h=(float)(crop.Height*scale);
            g.DrawImage(original,new Drawing.RectangleF((64-w)/2,(64-h)/2,w,h),crop,Drawing.GraphicsUnit.Pixel);
        }
        var handle=iconImage.GetHicon();try{using var borrowed=Drawing.Icon.FromHandle(handle);return (Drawing.Icon)borrowed.Clone();}finally{DestroyIcon(handle);}
    }
}
