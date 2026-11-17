namespace CakePet.Core;

// Activity rectangles are real monitor work areas, not the enclosing virtual desktop.
// The anchor can reach an open seam; at closed edges the original window bounds apply.
public sealed record ScreenRegion(string Id,Bounds Full,Bounds Area,Bounds Limits);
public readonly record struct MotionProjection(Point Position,Bounds Bounds);
public sealed record ScreenTransfer(ScreenRegion Region,Point Position,Bounds Limits);
public static class ConnectedDesktop
{
    private const double Epsilon=.01;
    private static bool Equal(double a,double b)=>Math.Abs(a-b)<=Epsilon;
    private static bool Between(double p,double a,double b)=>p>=a-Epsilon&&p<=b+Epsilon;
    private static double Boundary(Bounds b,Edge e)=>e switch{Edge.Left=>b.Left,Edge.Right=>b.Right,Edge.Top=>b.Top,_=>b.Bottom};
    private static Edge Opposite(Edge e)=>e switch{Edge.Left=>Edge.Right,Edge.Right=>Edge.Left,Edge.Top=>Edge.Bottom,_=>Edge.Top};
    private static bool Horizontal(Edge e)=>e is Edge.Left or Edge.Right;
    private static bool SameSupport(ScreenRegion a,ScreenRegion b,Edge support)=>support==Edge.None||Equal(Boundary(a.Limits,support),Boundary(b.Limits,support));
    private static ScreenRegion? Neighbor(IReadOnlyList<ScreenRegion> regions,ScreenRegion source,Edge side,Point position,Edge support,bool wrap)
    {
        if(side==support||side==Opposite(support)&&support!=Edge.None)return null; // climbing crosses only along its supporting surface
        var horizontal=Horizontal(side);var coordinate=horizontal?position.Y:position.X;
        bool Fits(ScreenRegion r)=>r.Id!=source.Id&&SameSupport(source,r,support)&&(horizontal?Between(coordinate,r.Limits.Top,r.Limits.Bottom):Between(coordinate,r.Limits.Left,r.Limits.Right));
        var candidates=regions.Where(Fits).ToArray();
        var adjacent=candidates.FirstOrDefault(r=>Equal(Boundary(source.Area,side),Boundary(r.Area,Opposite(side)))&&
            (horizontal?Math.Min(source.Area.Bottom,r.Area.Bottom)>Math.Max(source.Area.Top,r.Area.Top):Math.Min(source.Area.Right,r.Area.Right)>Math.Max(source.Area.Left,r.Area.Left)));
        if(adjacent!=null)return adjacent;
        if(!wrap||!horizontal||regions.Count<2)return null;
        var left=regions.Min(r=>r.Full.Left);var right=regions.Max(r=>r.Full.Right);
        if(side==Edge.Left&&Equal(source.Full.Left,left))return candidates.FirstOrDefault(r=>Equal(r.Full.Right,right));
        if(side==Edge.Right&&Equal(source.Full.Right,right))return candidates.FirstOrDefault(r=>Equal(r.Full.Left,left));
        return null;
    }
    private static double Seam(ScreenRegion r,Edge side,Point anchor)=>Boundary(r.Area,side)-(Horizontal(side)?anchor.X:anchor.Y);
    public static Edge OpenEdges(IReadOnlyList<ScreenRegion> regions,ScreenRegion source,Point position,Edge support,bool wrap=true)
    {
        var result=Edge.None;foreach(var side in SurfaceMotion.Sides)if(Neighbor(regions,source,side,position,support,wrap)!=null)result|=side;return result;
    }
    public static Bounds Limits(IReadOnlyList<ScreenRegion> regions,ScreenRegion source,Point position,Point anchor,Edge support,bool wrap=true)
    {
        var open=OpenEdges(regions,source,position,support,wrap);var b=source.Limits;
        return new Bounds(open.HasFlag(Edge.Left)?Math.Min(b.Left,Seam(source,Edge.Left,anchor)):b.Left,
            open.HasFlag(Edge.Top)?Math.Min(b.Top,Seam(source,Edge.Top,anchor)):b.Top,
            open.HasFlag(Edge.Right)?Math.Max(b.Right,Seam(source,Edge.Right,anchor)):b.Right,
            open.HasFlag(Edge.Bottom)?Math.Max(b.Bottom,Seam(source,Edge.Bottom,anchor)):b.Bottom);
    }
    public static ScreenTransfer Project(IReadOnlyList<ScreenRegion> regions,ScreenRegion source,Point from,Point next,Point anchor,Edge support,bool wrap=true)
    {
        var delta=next-from;
        foreach(var side in SurfaceMotion.Sides)
        {
            var seam=Seam(source,side,anchor);var crossed=side switch{Edge.Left=>delta.X<0&&next.X<=seam,Edge.Right=>delta.X>0&&next.X>=seam,Edge.Top=>delta.Y<0&&next.Y<=seam,_=>delta.Y>0&&next.Y>=seam};
            if(!crossed)continue;
            var target=Neighbor(regions,source,side,next,support,wrap);if(target==null)continue;
            var targetSeam=Seam(target,Opposite(side),anchor);var moved=Horizontal(side)?next with{X=next.X+targetSeam-seam}:next with{Y=next.Y+targetSeam-seam};
            var limits=Limits(regions,target,moved,anchor,support,wrap);
            return new ScreenTransfer(target,DesktopGeometry.Clamp(moved,limits),limits);
        }
        return new ScreenTransfer(source,next,Limits(regions,source,next,anchor,support,wrap));
    }
}
