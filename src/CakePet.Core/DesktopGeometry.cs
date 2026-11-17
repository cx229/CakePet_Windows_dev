namespace CakePet.Core;

// All desktop coordinates are physical pixels, including negative monitor origins.
public sealed record DesktopScreen(string Id,Bounds Full,Bounds Work,bool Primary);
public static class DesktopGeometry
{
    public static DesktopScreen Nearest(IReadOnlyList<DesktopScreen> screens,Point p)
    {
        if(screens.Count==0)throw new ArgumentException("No screens.");
        return screens.OrderBy(s=>Distance(s.Full,p)).ThenByDescending(s=>s.Primary).First();
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
