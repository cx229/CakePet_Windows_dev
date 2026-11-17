using System.IO;
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
        catch(Exception e) when(e is IOException or NotSupportedException or ArgumentException or InvalidOperationException or System.Runtime.InteropServices.COMException){return false;}
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
