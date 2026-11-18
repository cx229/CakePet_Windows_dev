using CakePet.Core;
using Frame=CakePet.Core.Frame;
namespace CakePet.Windows;

// Shared per-package decoded images and finished sprite pixels.
// Each instance retains only its native window buffer, not a decoded image set.
internal sealed class NativeSpriteCache:IDisposable
{
    private readonly GdiImageCache images;
    private readonly RolePackage pack;
    private readonly Dictionary<Key,(byte[] Pixels,LinkedListNode<Key> Node)> frames=new();
    private readonly LinkedList<Key> recent=new();
    private readonly Dictionary<int,GdiSurface> surfaces=new();
    private long bytes;
    private const long Budget=8L*1024*1024;
    private readonly record struct Key(Frame Frame,int Size,int Facing,Edge Edge,bool CanvasHit);
    public NativeSpriteCache(RolePackage pack){this.pack=pack;images=new GdiImageCache(pack);}
    public long Bytes=>bytes+images.CachedBytes;
    public byte[] Get(Frame frame,int side,int facing,Edge edge,bool canvasHit)
    {
        var key=new Key(frame,side,facing,edge,canvasHit);
        if(frames.TryGetValue(key,out var hit)){recent.Remove(hit.Node);recent.AddLast(hit.Node);return hit.Pixels;}
        if(!surfaces.TryGetValue(side,out var surface))
        {
            if(surfaces.Count>=2){foreach(var old in surfaces.Values)old.Dispose();surfaces.Clear();}
            surfaces[side]=surface=new GdiSurface(side);
        }
        var image=images.Get(frame);
        surface.Draw(image.Handle,images.Geometry(frame),pack.AnchorRes,facing,edge,canvasHit);
        var pixels=surface.CopyPixels();frames.Add(key,(pixels,recent.AddLast(key)));bytes+=pixels.LongLength;
        while(bytes>Budget&&frames.Count>1&&recent.First is {} oldest){bytes-=frames[oldest.Value].Pixels.LongLength;frames.Remove(oldest.Value);recent.RemoveFirst();}
        return pixels;
    }
    public void Clear(){frames.Clear();recent.Clear();bytes=0;foreach(var surface in surfaces.Values)surface.Dispose();surfaces.Clear();images.Clear();}
    public void Dispose(){Clear();images.Dispose();}
}
