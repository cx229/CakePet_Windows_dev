using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CakePet.Core;
using Frame=CakePet.Core.Frame;

namespace CakePet.Windows;

// Role sprites use native GDI+ rasterization. Food still uses WPF bitmaps.
internal static class NativePetPixels
{
    internal static byte[] Sprite(WpfImageCache images,Frame frame,int side,int anchor,int facing,Edge edge,bool canvasHit)
    {
        return images.Native.Get(frame,side,facing,edge,canvasHit);

    }
    internal static byte[] Image(BitmapSource source,int w,int h)
    {
        var visual=new DrawingVisual();using(var dc=visual.RenderOpen())dc.DrawImage(source,new Rect(0,0,w,h));
        return Raster(visual,w,h);
    }
    private static byte[] Raster(DrawingVisual visual,int w,int h)
    {
        var target=new RenderTargetBitmap(w,h,96,96,PixelFormats.Pbgra32);target.Render(visual);
        var bytes=new byte[checked(w*h*4)];target.CopyPixels(bytes,w*4,0);return bytes;
    }
}
