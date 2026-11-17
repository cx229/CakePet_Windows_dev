using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CakePet.Core;

namespace CakePet.Windows;

internal sealed class WpfImageCache(RolePackage pack)
{
    private readonly Dictionary<(string,string),BitmapSource> cache=new();
    public static bool IsReadable(string path)
    {
        try
        {
            using var stream=File.OpenRead(path);
            var frame=BitmapDecoder.Create(stream,BitmapCreateOptions.PreservePixelFormat,BitmapCacheOption.OnLoad).Frames[0];
            return frame.PixelWidth>0&&frame.PixelHeight>0;
        }
        catch(Exception e) when(e is IOException or NotSupportedException or ArgumentException or InvalidOperationException or FormatException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException){return false;}
    }
    private readonly Dictionary<string,FoodVisual> foodVisuals=new(StringComparer.Ordinal);
    internal sealed record FoodVisual(IReadOnlyDictionary<Frame,BitmapSource> Frames,int Width,int Height);
    public FoodVisual GetFoodVisual(string start)
    {
        if(foodVisuals.TryGetValue(start,out var cached))return cached;
        var side=pack.AnchorRes;var canvasSide=Math.Clamp(pack.DecodeTarget>0?pack.DecodeTarget:side,1,2048);
        var source=new Dictionary<Frame,BitmapSource>();var visited=new HashSet<string>();string? next=start;
        while(next!=null&&visited.Add(next)&&pack.Modes.TryGetValue(next,out var mode))
        {
            foreach(var frame in mode.Frames)
            {
                if(source.ContainsKey(frame))continue;
                var bitmap=Get(frame);
                var (w,h)=frame.Transform is "rotate90" or "rotate270"?(bitmap.PixelHeight,bitmap.PixelWidth):(bitmap.PixelWidth,bitmap.PixelHeight);
                var g=FrameGeometry.Calculate(w,h,canvasSide,frame);
                var visual=new DrawingVisual();using(var dc=visual.RenderOpen())dc.DrawImage(bitmap,new Rect(g.Left,g.Top,g.Width,g.Height));
                var canvas=new RenderTargetBitmap(canvasSide,canvasSide,96,96,PixelFormats.Pbgra32);canvas.Render(visual);canvas.Freeze();source[frame]=canvas;
            }
            next=mode.Loop==LoopKind.Infinite?null:mode.Next;
        }
        if(source.Count==0)throw new InvalidDataException("Food mode has no images.");
        var left=canvasSide;var top=canvasSide;var right=-1;var bottom=-1;
        foreach(var bitmap in source.Values)
        {
            var bytes=new byte[checked(canvasSide*canvasSide*4)];bitmap.CopyPixels(bytes,canvasSide*4,0);
            for(int y=0;y<canvasSide;y++)for(int x=0;x<canvasSide;x++)if(bytes[(y*canvasSide+x)*4+3]>8)
            {left=Math.Min(left,x);top=Math.Min(top,y);right=Math.Max(right,x);bottom=Math.Max(bottom,y);}
        }
        var rect=right<left?new Int32Rect(0,0,canvasSide,canvasSide):new Int32Rect(left,top,right-left+1,bottom-top+1);
        var cropped=source.ToDictionary(x=>x.Key,x=>{var b=new CroppedBitmap(x.Value,rect);b.Freeze();return (BitmapSource)b;});
        return foodVisuals[start]=new FoodVisual(cropped,rect.Width,rect.Height);
    }
    public BitmapSource Get(Frame frame)
    {
        var key=(frame.Resource,frame.Transform);
        if(cache.TryGetValue(key,out var cached))return cached;
        var path=pack.ResolveAsset(frame.Resource);
        int w,h;
        using(var input=File.OpenRead(path))
        {
            var metadata=BitmapDecoder.Create(input,BitmapCreateOptions.DelayCreation,BitmapCacheOption.OnDemand).Frames[0];
            w=metadata.PixelWidth;h=metadata.PixelHeight;
        }
        using var stream=File.OpenRead(path);
        var image=new BitmapImage();
        image.BeginInit();
        image.CacheOption=BitmapCacheOption.OnLoad;image.StreamSource=stream;
        if(pack.DecodeTarget>0){if(w>=h)image.DecodePixelWidth=pack.DecodeTarget;else image.DecodePixelHeight=pack.DecodeTarget;}
        image.EndInit();image.Freeze();
        Transform? transform=frame.Transform switch {
            "flipX"=>new ScaleTransform(-1,1),"flipY"=>new ScaleTransform(1,-1),
            "rotate90"=>new RotateTransform(90),"rotate180"=>new RotateTransform(180),"rotate270"=>new RotateTransform(270),_=>null};
        BitmapSource result=image;
        if(transform!=null){result=new TransformedBitmap(image,transform);result.Freeze();}
        cache.Add(key,result);return result;
    }
}
