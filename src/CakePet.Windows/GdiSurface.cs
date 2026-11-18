using System.IO;
using System.ComponentModel;
using System.Runtime.InteropServices;
using CakePet.Core;

namespace CakePet.Windows;

internal static class Gdi
{
    internal const int Pargb32=0x000E200B;
    [StructLayout(LayoutKind.Sequential)]internal struct StartupInput{public uint Version;public nint Debug;public int NoBackgroundThread,NoExternalCodecs;}
    internal static void Check(int status){if(status!=0)throw new InvalidOperationException("GDI+ error "+status);}
    [DllImport("gdiplus.dll")]internal static extern int GdiplusStartup(out nuint token,ref StartupInput input,nint output);
    [DllImport("gdiplus.dll")]internal static extern void GdiplusShutdown(nuint token);
    [DllImport("gdiplus.dll",CharSet=CharSet.Unicode,ExactSpelling=true)]internal static extern int GdipCreateBitmapFromFile(string filename,out nint image);
    [DllImport("gdiplus.dll")]internal static extern int GdipGetImageWidth(nint image,out uint width);
    [DllImport("gdiplus.dll")]internal static extern int GdipGetImageHeight(nint image,out uint height);
    [DllImport("gdiplus.dll")]internal static extern int GdipCreateBitmapFromScan0(int width,int height,int stride,int format,nint pixels,out nint image);
    [DllImport("gdiplus.dll")]internal static extern int GdipDisposeImage(nint image);
    [DllImport("gdiplus.dll")]internal static extern int GdipGetImageGraphicsContext(nint image,out nint graphics);
    [DllImport("gdiplus.dll")]internal static extern int GdipDeleteGraphics(nint graphics);
    [DllImport("gdiplus.dll")]internal static extern int GdipGraphicsClear(nint graphics,uint argb);
    [DllImport("gdiplus.dll")]internal static extern int GdipSetInterpolationMode(nint graphics,int mode);
    [DllImport("gdiplus.dll")]internal static extern int GdipSetCompositingMode(nint graphics,int mode);
    [DllImport("gdiplus.dll")]internal static extern int GdipDrawImageRect(nint graphics,nint image,float x,float y,float width,float height);
    [DllImport("gdiplus.dll")]internal static extern int GdipImageRotateFlip(nint image,int type);
    [DllImport("gdiplus.dll")]internal static extern int GdipResetWorldTransform(nint graphics);
    [DllImport("gdiplus.dll")]internal static extern int GdipTranslateWorldTransform(nint graphics,float x,float y,int order);
    [DllImport("gdiplus.dll")]internal static extern int GdipScaleWorldTransform(nint graphics,float x,float y,int order);
    [DllImport("gdiplus.dll")]internal static extern int GdipRotateWorldTransform(nint graphics,float angle,int order);
    [DllImport("gdiplus.dll")]internal static extern int GdipBitmapGetPixel(nint bitmap,int x,int y,out uint argb);
    [DllImport("gdiplus.dll")]internal static extern int GdipFlush(nint graphics,int intention);
}
internal sealed class GdiImageCache:IDisposable
{
    internal sealed record Image(nint Handle,int Width,int Height,long Bytes,LinkedListNode<(string,string)> Recent);
    private readonly RolePackage pack;
    private readonly Dictionary<(string,string),Image> images=new();
    private readonly LinkedList<(string,string)> recent=new();
    private readonly Dictionary<Frame,FrameGeometry> geometries=new();
    private nuint token;
    public long CachedBytes {get;private set;}
    public void Clear(){foreach(var image in images.Values)Gdi.GdipDisposeImage(image.Handle);images.Clear();recent.Clear();geometries.Clear();CachedBytes=0;}
    public GdiImageCache(RolePackage pack)
    {
        this.pack=pack;var input=new Gdi.StartupInput{Version=1};
        Gdi.Check(Gdi.GdiplusStartup(out token,ref input,0));
    }
    public Image Get(Frame frame)
    {
        var key=(frame.Resource,frame.Transform);
        if(images.TryGetValue(key,out var cached)){recent.Remove(cached.Recent);recent.AddLast(cached.Recent);return cached;}
        nint source=0,bitmap=0,graphics=0;
        try
        {
            Gdi.Check(Gdi.GdipCreateBitmapFromFile(pack.ResolveAsset(frame.Resource),out source));
            Gdi.Check(Gdi.GdipGetImageWidth(source,out var w));Gdi.Check(Gdi.GdipGetImageHeight(source,out var h));
            if(w==0||h==0)throw new InvalidDataException(frame.Resource);
            var limit=pack.DecodeTarget;
            var scale=limit>0?Math.Min(1,(double)limit/Math.Max(w,h)):1;
            int width=Math.Max(1,(int)Math.Round(w*scale)),height=Math.Max(1,(int)Math.Round(h*scale));
            Gdi.Check(Gdi.GdipCreateBitmapFromScan0(width,height,0,Gdi.Pargb32,0,out bitmap));
            Gdi.Check(Gdi.GdipGetImageGraphicsContext(bitmap,out graphics));
            Gdi.Check(Gdi.GdipSetCompositingMode(graphics,1));Gdi.Check(Gdi.GdipSetInterpolationMode(graphics,6));
            Gdi.Check(Gdi.GdipGraphicsClear(graphics,0));Gdi.Check(Gdi.GdipDrawImageRect(graphics,source,0,0,width,height));
            Gdi.GdipDeleteGraphics(graphics);graphics=0;
            int transform=frame.Transform switch{"rotate90"=>1,"rotate180"=>2,"rotate270"=>3,"flipX"=>4,"flipY"=>6,_=>0};
            if(transform!=0)Gdi.Check(Gdi.GdipImageRotateFlip(bitmap,transform));
            // RotateFlip changes the actual decoded dimensions for quarter turns.
            Gdi.Check(Gdi.GdipGetImageWidth(bitmap,out var actualW));Gdi.Check(Gdi.GdipGetImageHeight(bitmap,out var actualH));
            width=checked((int)actualW);height=checked((int)actualH);
            var entry=new Image(bitmap,width,height,(long)width*height*4,recent.AddLast(key));images.Add(key,entry);bitmap=0;
            CachedBytes+=entry.Bytes;
            while(CachedBytes>32L*1024*1024&&images.Count>1&&recent.First is {} node)
            {
                var victim=images[node.Value];images.Remove(node.Value);recent.RemoveFirst();CachedBytes-=victim.Bytes;Gdi.GdipDisposeImage(victim.Handle);
            }
            return entry;
        }
        finally{if(graphics!=0)Gdi.GdipDeleteGraphics(graphics);if(bitmap!=0)Gdi.GdipDisposeImage(bitmap);if(source!=0)Gdi.GdipDisposeImage(source);}
    }
    public bool OpaqueAt(Frame frame,double u,double v)
    {
        if(!double.IsFinite(u)||!double.IsFinite(v)||u<0||v<0||u>=1||v>=1)return false;
        var image=Get(frame);Gdi.Check(Gdi.GdipBitmapGetPixel(image.Handle,(int)(u*image.Width),(int)(v*image.Height),out var argb));return (argb>>24)>8;
    }
    public FrameGeometry Geometry(Frame frame)
    {
        if(geometries.TryGetValue(frame,out var result))return result;
        var image=Get(frame);return geometries[frame]=FrameGeometry.Calculate(image.Width,image.Height,pack.AnchorRes,frame);
    }
    public void Dispose()
    {
        Clear();
        if(token!=0){Gdi.GdiplusShutdown(token);token=0;}
    }
}
internal sealed class GdiSurface:IDisposable
{
    private readonly int side,width,height;
    private nint dc,dib,old,bitmap,graphics,bits;
    public nint Dc=>dc;
    public GdiSurface(int side):this(side,side){}
    public GdiSurface(int width,int height)
    {
        this.width=width;this.height=height;side=width;
        try
        {
            dc=CreateCompatibleDC(0);if(dc==0)throw new Win32Exception(Marshal.GetLastWin32Error());
            var info=new BitmapInfo{Size=40,Width=width,Height=-height,Planes=1,BitCount=32};
            dib=CreateDIBSection(dc,ref info,0,out bits,0,0);if(dib==0||bits==0)throw new Win32Exception(Marshal.GetLastWin32Error());
            old=SelectObject(dc,dib);if(old==0||old==(nint)(-1))throw new Win32Exception(Marshal.GetLastWin32Error());
            Gdi.Check(Gdi.GdipCreateBitmapFromScan0(width,height,width*4,Gdi.Pargb32,bits,out bitmap));
            Gdi.Check(Gdi.GdipGetImageGraphicsContext(bitmap,out graphics));
            Gdi.Check(Gdi.GdipSetInterpolationMode(graphics,6));
        }
        catch{Dispose();throw;}
    }
    public void Draw(nint image,FrameGeometry g,int anchorRes,int facing,Edge edge,bool canvasHit=false)
    {
        Gdi.Check(Gdi.GdipResetWorldTransform(graphics));Gdi.Check(Gdi.GdipGraphicsClear(graphics,canvasHit?0x01000000u:0u));
        var half=side*.5f;
        Gdi.Check(Gdi.GdipTranslateWorldTransform(graphics,half,half,0));
        var angle=edge switch{Edge.Left=>90,Edge.Right=>-90,Edge.Top=>180,_=>0};
        if(angle!=0)Gdi.Check(Gdi.GdipRotateWorldTransform(graphics,angle,0));
        if(facing!=1)Gdi.Check(Gdi.GdipScaleWorldTransform(graphics,facing,1,0));
        Gdi.Check(Gdi.GdipTranslateWorldTransform(graphics,-half,-half,0));
        var scale=(float)side/anchorRes;
        Gdi.Check(Gdi.GdipDrawImageRect(graphics,image,g.Left*scale,g.Top*scale,g.Width*scale,g.Height*scale));
        Gdi.Check(Gdi.GdipFlush(graphics,1));
    }
    public void DrawRect(nint image,float x,float y,float w,float h)
    {
        Gdi.Check(Gdi.GdipResetWorldTransform(graphics));Gdi.Check(Gdi.GdipGraphicsClear(graphics,0));
        Gdi.Check(Gdi.GdipDrawImageRect(graphics,image,x,y,w,h));Gdi.Check(Gdi.GdipFlush(graphics,1));
    }
    public byte[] CopyPixels(){var bytes=new byte[checked(width*height*4)];Marshal.Copy(bits,bytes,0,bytes.Length);return bytes;}
    public void Dispose()
    {
        if(graphics!=0){Gdi.GdipDeleteGraphics(graphics);graphics=0;}
        if(bitmap!=0){Gdi.GdipDisposeImage(bitmap);bitmap=0;}
        if(dc!=0&&old!=0&&old!=(nint)(-1))SelectObject(dc,old);
        if(dib!=0){DeleteObject(dib);dib=0;}
        if(dc!=0){DeleteDC(dc);dc=0;}
    }
    [StructLayout(LayoutKind.Sequential)]private struct BitmapInfo
    {
        public uint Size;public int Width,Height;public ushort Planes,BitCount;
        public uint Compression,ImageSize;public int XResolution,YResolution;public uint Used,Important;
        public uint Color;
    }
    [DllImport("gdi32.dll",SetLastError=true)]private static extern nint CreateCompatibleDC(nint dc);
    [DllImport("gdi32.dll",SetLastError=true)]private static extern nint CreateDIBSection(nint dc,ref BitmapInfo info,uint usage,out nint bits,nint section,uint offset);
    [DllImport("gdi32.dll",SetLastError=true)]private static extern nint SelectObject(nint dc,nint obj);
    [DllImport("gdi32.dll")]private static extern bool DeleteObject(nint obj);
    [DllImport("gdi32.dll")]private static extern bool DeleteDC(nint dc);
}
