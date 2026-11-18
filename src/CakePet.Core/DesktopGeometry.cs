namespace CakePet.Core;

// All desktop coordinates are physical pixels, including negative monitor origins.
public sealed record DesktopScreen(string Id,Bounds Full,Bounds Work,bool Primary);
public static class DesktopGeometry
{
    public static DesktopScreen Nearest(IReadOnlyList<DesktopScreen> screens,Point p)
    {
        if(screens.Count==0)throw new ArgumentException("No screens.");
        var best=screens[0];var distance=Distance(best.Full,p);for(int i=1;i<screens.Count;i++){var candidate=screens[i];var next=Distance(candidate.Full,p);if(next<distance||next==distance&&candidate.Primary&&!best.Primary){best=candidate;distance=next;}}return best;
    }
    private static double Distance(Bounds b,Point p)
    {
        var x=p.X-Math.Clamp(p.X,b.Left,b.Right);var y=p.Y-Math.Clamp(p.Y,b.Top,b.Bottom);
        return x*x+y*y;
    }
    public static Bounds Fit(Bounds area,double width,double height)=>
        new(area.Left,area.Top,Math.Max(area.Left,area.Right-width),Math.Max(area.Top,area.Bottom-height));
    public static Point Clamp(Point p,Bounds b)=>new(Math.Clamp(p.X,b.Left,b.Right),Math.Clamp(p.Y,b.Top,b.Bottom));
}
