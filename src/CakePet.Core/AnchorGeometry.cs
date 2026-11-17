namespace CakePet.Core;

// Window origin remains the position truth. Anchor changes only change its legal range.
public static class AnchorGeometry
{
    public static Point Transform(Point p,double side,Edge edge,int facing)
    {
        var half=side/2;var local=new Point((p.X-half)*facing,p.Y-half);
        return SurfaceMotion.ToScreen(local,edge)+new Point(half,half);
    }
    public static Point Inverse(Point p,double side,Edge edge,int facing)
    {
        var half=side/2;var delta=p-new Point(half,half);
        var inverse=edge switch{Edge.Left=>new Point(delta.Y,-delta.X),Edge.Right=>new Point(-delta.Y,delta.X),Edge.Top=>new Point(-delta.X,-delta.Y),_=>delta};
        return new Point(inverse.X*facing+half,inverse.Y+half);
    }
    public static Bounds WindowBounds(Bounds area,double side,Point anchor,Edge foot)
    {
        var left=foot==Edge.Left?area.Left-anchor.X:area.Left;
        var top=foot==Edge.Top?area.Top-anchor.Y:area.Top;
        var right=foot==Edge.Right?area.Right-anchor.X:area.Right-side;
        var bottom=foot==Edge.Bottom?area.Bottom-anchor.Y:area.Bottom-side;
        return new Bounds(left,top,Math.Max(left,right),Math.Max(top,bottom));
    }
}
