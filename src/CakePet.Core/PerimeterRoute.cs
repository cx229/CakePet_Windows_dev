namespace CakePet.Core;

// Clockwise distance measured along the rectangle, not Euclidean distance.
public readonly record struct PerimeterRoute(double Distance,int Direction)
{
    public static PerimeterRoute Calculate(Bounds b,Point position,Edge side,Point target)
    {
        var w=b.Right-b.Left;var h=b.Bottom-b.Top;var length=2*(w+h);
        if(length<=0)return new(0,1);
        target=new Point(Math.Clamp(target.X,b.Left,b.Right),Math.Clamp(target.Y,b.Top,b.Bottom));
        var nearest=new[]{(Edge.Top,target.Y-b.Top),(Edge.Right,b.Right-target.X),(Edge.Bottom,b.Bottom-target.Y),(Edge.Left,target.X-b.Left)}
            .OrderBy(x=>x.Item2).First().Item1;
        double Arc(Point p,Edge e)=>e switch {
            Edge.Top=>p.X-b.Left,Edge.Right=>w+p.Y-b.Top,Edge.Bottom=>w+h+b.Right-p.X,_=>2*w+h+b.Bottom-p.Y};
        var cw=(Arc(target,nearest)-Arc(position,side)+length)%length;
        var ccw=length-cw;
        // Local +x travels counter-clockwise for every surface.
        return cw<=ccw?new(cw,-1):new(ccw,1);
    }
}
