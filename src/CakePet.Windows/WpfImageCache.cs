using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CakePet.Core;

namespace CakePet.Windows;

internal sealed class WpfImageCache(RolePackage pack)
{
    private const long ImageBudget=32L*1024*1024;
    private const long FoodBudget=16L*1024*1024;
    private readonly Dictionary<(string,string),(BitmapSource Bitmap,long Bytes,LinkedListNode<(string,string)> Node)> cache=new();
    private readonly LinkedList<(string,string)> recent=new();
    private long imageBytes,foodBytes;
    private readonly LinkedList<string> recentFood=new();
    public void Clear(){cache.Clear();recent.Clear();imageBytes=0;foodVisuals.Clear();recentFood.Clear();foodBytes=0;alpha=new();}
    private static long Bytes(BitmapSource b)=>(long)b.PixelWidth*b.PixelHeight*4;
    private static long Bytes(FoodVisual v)=>v.Frames.Values.Sum(Bytes);
    private void TrimImages()
    {
        while(imageBytes>ImageBudget&&cache.Count>1&&recent.First is {} node){var entry=cache[node.Value];cache.Remove(node.Value);recent.RemoveFirst();imageBytes-=entry.Bytes;}
    }
    public static bool IsReadable(string path)
    {
        try
        {
            using var stream=File.OpenRead(path);
            var image=new BitmapImage();image.BeginInit();image.CacheOption=BitmapCacheOption.OnLoad;image.StreamSource=stream;image.DecodePixelWidth=64;image.EndInit();
            return image.PixelWidth>0&&image.PixelHeight>0;
        }
        catch(Exception e) when(e is IOException or NotSupportedException or ArgumentException or InvalidOperationException or FormatException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException){return false;}
    }
    private readonly Dictionary<string,FoodVisual> foodVisuals=new(StringComparer.Ordinal);
    internal sealed record FoodVisual(IReadOnlyDictionary<Frame,BitmapSource> Frames,int Width,int Height,double LogicalWidth,double LogicalHeight);
    public FoodVisual GetFoodVisual(string start)
    {
        if(foodVisuals.TryGetValue(start,out var cached)){recentFood.Remove(start);recentFood.AddLast(start);return cached;}
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
        var cropped=source.ToDictionary(x=>x.Key,x=>{
            // Copy only the cropped pixels; CroppedBitmap alone retains its full-size source.
            var stride=checked(rect.Width*4);var bytes=new byte[checked(stride*rect.Height)];x.Value.CopyPixels(rect,bytes,stride,0);
            var b=BitmapSource.Create(rect.Width,rect.Height,96,96,PixelFormats.Pbgra32,null,bytes,stride);b.Freeze();return b;
        });
        var result=new FoodVisual(cropped,rect.Width,rect.Height,rect.Width*(double)side/canvasSide,rect.Height*(double)side/canvasSide);
        foodVisuals[start]=result;recentFood.AddLast(start);foodBytes+=Bytes(result);
        while(foodBytes>FoodBudget&&foodVisuals.Count>1&&recentFood.First is {} node){foodBytes-=Bytes(foodVisuals[node.Value]);foodVisuals.Remove(node.Value);recentFood.RemoveFirst();}
        return result;
    }
    private readonly Dictionary<Frame,FrameGeometry> geometries=new();
    private System.Runtime.CompilerServices.ConditionalWeakTable<BitmapSource,byte[]> alpha=new();
    public FrameGeometry Geometry(Frame frame)
    {
        if(geometries.TryGetValue(frame,out var g))return g;
        var bitmap=Get(frame);var (w,h)=frame.Transform is "rotate90" or "rotate270"?(bitmap.PixelHeight,bitmap.PixelWidth):(bitmap.PixelWidth,bitmap.PixelHeight);
        return geometries[frame]=FrameGeometry.Calculate(w,h,pack.AnchorRes,frame);
    }
    public bool OpaqueAt(Frame frame,double u,double v)
    {
        if(u<0||v<0||u>=1||v>=1)return false;
        var bitmap=Get(frame);
        if(!alpha.TryGetValue(bitmap,out var mask))
        {
            var converted=new FormatConvertedBitmap(bitmap,PixelFormats.Bgra32,null,0);
            var bytes=new byte[checked(bitmap.PixelWidth*bitmap.PixelHeight*4)];converted.CopyPixels(bytes,bitmap.PixelWidth*4,0);
            mask=new byte[bitmap.PixelWidth*bitmap.PixelHeight];for(int i=0;i<mask.Length;i++)mask[i]=bytes[4*i+3];alpha.Add(bitmap,mask);
        }
        return mask[(int)(v*bitmap.PixelHeight)*bitmap.PixelWidth+(int)(u*bitmap.PixelWidth)]>8;
    }
    public BitmapSource Get(Frame frame)
    {
        var key=(frame.Resource,frame.Transform);
        if(cache.TryGetValue(key,out var cached)){recent.Remove(cached.Node);recent.AddLast(cached.Node);return cached.Bitmap;}
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
        var bytes=Bytes(result)*(transform==null?1:2);cache.Add(key,(result,bytes,recent.AddLast(key)));imageBytes+=bytes;TrimImages();return result;
    }
}
