using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CakePet.Core;
using Frame=CakePet.Core.Frame;

namespace CakePet.Windows;

// Transitional adapter: render only on frame/size/orientation changes. The HWND
// is native; image decoding and rasterization still use the existing WPF cache.
internal static class NativePetPixels
{
    internal static byte[] Sprite(WpfImageCache images,Frame frame,int side,int anchor,int facing,Edge edge,bool canvasHit)
    {
        var g=images.Geometry(frame);var scale=(double)side/anchor;
        var visual=new DrawingVisual();
        using(var dc=visual.RenderOpen())
        {
            if(canvasHit)dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(1,0,0,0)),null,new Rect(0,0,side,side));
            dc.PushTransform(new TranslateTransform(side/2.0,side/2.0));
            dc.PushTransform(new RotateTransform(edge switch{Edge.Left=>90,Edge.Right=>-90,Edge.Top=>180,_=>0}));
            dc.PushTransform(new ScaleTransform(facing,1));
            dc.PushTransform(new TranslateTransform(-side/2.0,-side/2.0));
            dc.DrawImage(images.Get(frame),new Rect(g.Left*scale,g.Top*scale,g.Width*scale,g.Height*scale));
            dc.Pop();dc.Pop();dc.Pop();dc.Pop();
        }
        return Raster(visual,side,side);
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
