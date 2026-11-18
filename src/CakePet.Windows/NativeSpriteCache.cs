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
    public long DecodedBytes=>images.CachedBytes;
    public long RenderedBytes=>bytes+foodBytes;
    public long SurfaceBytes=>surfaces.Keys.Sum(s=>(long)s*s*4);
    public long Bytes=>DecodedBytes+RenderedBytes+SurfaceBytes;
    public FrameGeometry Geometry(Frame frame)=>images.Geometry(frame);
    public bool OpaqueAt(Frame frame,double u,double v)=>images.OpaqueAt(frame,u,v);
    private readonly Dictionary<string,FoodVisual> foodVisuals=new(StringComparer.Ordinal);
    private readonly Dictionary<FoodKey,(byte[] Pixels,LinkedListNode<FoodKey> Node)> foodFrames=new();
    private readonly LinkedList<FoodKey> foodRecent=new();
    private long foodBytes;
    private readonly record struct FoodKey(string Start,Frame Frame,int Width,int Height);
    internal sealed record FoodVisual(string Start,int Canvas,int Left,int Top,int Width,int Height,double LogicalWidth,double LogicalHeight);
    public FoodVisual GetFoodVisual(string start)
    {
        if(foodVisuals.TryGetValue(start,out var hit))return hit;
        int side=Math.Clamp(pack.DecodeTarget>0?pack.DecodeTarget:pack.AnchorRes,1,2048);
        var all=new HashSet<Frame>();var visited=new HashSet<string>();string? next=start;
        while(next!=null&&visited.Add(next)&&pack.Modes.TryGetValue(next,out var mode))
        {foreach(var frame in mode.Frames)all.Add(frame);next=mode.Loop==LoopKind.Infinite?null:mode.Next;}
        if(all.Count==0)throw new System.IO.InvalidDataException("Food mode has no images.");
        int left=side,top=side,right=-1,bottom=-1;
        using(var surface=new GdiSurface(side))foreach(var frame in all)
        {
            var image=images.Get(frame);var g=images.Geometry(frame);float scale=(float)side/pack.AnchorRes;
            surface.DrawRect(image.Handle,g.Left*scale,g.Top*scale,g.Width*scale,g.Height*scale);
            var pixels=surface.CopyPixels();
            for(int y=0;y<side;y++)for(int x=0;x<side;x++)if(pixels[(y*side+x)*4+3]>8){left=Math.Min(left,x);top=Math.Min(top,y);right=Math.Max(right,x);bottom=Math.Max(bottom,y);}
        }
        if(right<left){left=top=0;right=bottom=side-1;}
        int w=right-left+1,h=bottom-top+1;
        return foodVisuals[start]=new(start,side,left,top,w,h,w*(double)pack.AnchorRes/side,h*(double)pack.AnchorRes/side);
    }
    public byte[] GetFoodPixels(FoodVisual visual,Frame frame,int w,int h)
    {
        var key=new FoodKey(visual.Start,frame,w,h);
        if(foodFrames.TryGetValue(key,out var hit)){foodRecent.Remove(hit.Node);foodRecent.AddLast(hit.Node);return hit.Pixels;}
        var image=images.Get(frame);var g=images.Geometry(frame);float scale=(float)visual.Canvas/pack.AnchorRes;
        using var surface=new GdiSurface(w,h);float sx=(float)w/visual.Width,sy=(float)h/visual.Height;
        surface.DrawRect(image.Handle,(g.Left*scale-visual.Left)*sx,(g.Top*scale-visual.Top)*sy,g.Width*scale*sx,g.Height*scale*sy);
        var pixels=surface.CopyPixels();foodFrames.Add(key,(pixels,foodRecent.AddLast(key)));foodBytes+=pixels.LongLength;
        while(foodBytes>8L*1024*1024&&foodFrames.Count>1&&foodRecent.First is {} oldest){foodBytes-=foodFrames[oldest.Value].Pixels.LongLength;foodFrames.Remove(oldest.Value);foodRecent.RemoveFirst();}
        return pixels;
    }
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
    public void Clear(){foodVisuals.Clear();foodFrames.Clear();foodRecent.Clear();foodBytes=0;frames.Clear();recent.Clear();bytes=0;foreach(var surface in surfaces.Values)surface.Dispose();surfaces.Clear();images.Clear();}
    public void Dispose(){Clear();images.Dispose();}
}
