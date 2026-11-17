namespace CakePet.Core;

public readonly record struct Point(double X,double Y)
{
    public static Point operator +(Point a,Point b)=>new(a.X+b.X,a.Y+b.Y);
    public static Point operator *(Point a,double scale)=>new(a.X*scale,a.Y*scale);
}
public readonly record struct Bounds(double Left,double Top,double Right,double Bottom);
[Flags] public enum Edge { None=0,Bottom=1,Left=2,Right=4,Top=8,All=15 }

public sealed class PhysicsBody
{
    public Point Position { get; private set; }
    public Point Velocity { get; private set; }
    public PhysicalState State { get; private set; }
    public Edge SnappedEdge { get; private set; }
    public double Gravity { get; set; }=1500;
    public double Bounce { get; set; }=.7;
    public double MaxSpeed { get; set; }=2000;
    public double LandingSpeed { get; set; }=200;
    public double SnapSpeed { get; set; }=800;
    public double SnapDistance { get; set; }=100;
    public double ThrowCoefficient { get; set; }=1;
    public bool GravityEnabled { get; set; }=true;
    public bool ThrowEnabled { get; set; }=true;
    public bool BounceEnabled { get; set; }=true;
    public bool SnapEnabled { get; set; }=true;
    public Edge GravityEdges { get; set; }=Edge.Bottom;
    public Edge BounceEdges { get; set; }=Edge.All;
    public Edge SnapEdges { get; set; }=Edge.All;
    public PhysicsBody(Point position)=>Position=position;
    private static Point Clamp(Point p,Bounds b)=>new(Math.Clamp(p.X,b.Left,b.Right),Math.Clamp(p.Y,b.Top,b.Bottom));
    private Point Limit(Point p)=>new(Math.Clamp(p.X,-MaxSpeed,MaxSpeed),Math.Clamp(p.Y,-MaxSpeed,MaxSpeed));
    public void Place(Point point,Bounds? bounds=null){Position=bounds is Bounds b?Clamp(point,b):point;Velocity=default;}
    public void Grab(){State=PhysicalState.Dragging;SnappedEdge=Edge.None;Velocity=default;}
    public void DragTo(Point point){if(State==PhysicalState.Dragging)Position=point;}
    public void Release(Point velocity,Bounds? bounds=null)
    {
        if(bounds is Bounds b)
        {
            Position=Clamp(Position,b);
            if(SnapEnabled&&Math.Sqrt(velocity.X*velocity.X+velocity.Y*velocity.Y)<=SnapSpeed)
            {
                var sides=new[]{(Edge.Bottom,b.Bottom-Position.Y),(Edge.Left,Position.X-b.Left),(Edge.Right,b.Right-Position.X),(Edge.Top,Position.Y-b.Top)};
                var nearest=sides.Where(x=>SnapEdges.HasFlag(x.Item1)&&x.Item2<=SnapDistance).OrderBy(x=>x.Item2).FirstOrDefault();
                if(nearest.Item1!=Edge.None)
                {
                    SnappedEdge=nearest.Item1;
                    Position=nearest.Item1 switch{Edge.Bottom=>Position with{Y=b.Bottom},Edge.Top=>Position with{Y=b.Top},Edge.Left=>Position with{X=b.Left},_=>Position with{X=b.Right}};
                    State=PhysicalState.Free;Velocity=default;return;
                }
            }
        }
        SnappedEdge=Edge.None;
        Velocity=GravityEnabled&&ThrowEnabled?Limit(velocity*ThrowCoefficient):default;
        State=GravityEnabled?PhysicalState.Throwing:PhysicalState.Free;
    }
    public bool MoveFrame(Point delta,Bounds? bounds=null)
    {
        if(State!=PhysicalState.Free||SnappedEdge!=Edge.None)return false;
        var next=Position+delta;
        Position=bounds is Bounds b?Clamp(next,b):next;
        return Position!=next;
    }
    private Point Acceleration=>!GravityEnabled?default:new Point(
        Gravity*((GravityEdges.HasFlag(Edge.Right)?1:0)-(GravityEdges.HasFlag(Edge.Left)?1:0)),
        Gravity*((GravityEdges.HasFlag(Edge.Bottom)?1:0)-(GravityEdges.HasFlag(Edge.Top)?1:0)));
    private Edge Primary=>Acceleration.Y>0?Edge.Bottom:Acceleration.Y<0?Edge.Top:Acceleration.X<0?Edge.Left:Acceleration.X>0?Edge.Right:Edge.None;
    private double Collide(double velocity,Edge edge)=>State==PhysicalState.Throwing&&BounceEnabled&&BounceEdges.HasFlag(edge)?-velocity*Math.Clamp(Bounce,0,1):0;
    public bool Advance(double seconds,Bounds bounds)
    {
        if(!double.IsFinite(seconds)||seconds<0)throw new ArgumentOutOfRangeException(nameof(seconds));
        if(State is not (PhysicalState.Free or PhysicalState.Throwing)||SnappedEdge!=Edge.None)return false;
        while(seconds>0)
        {
            var dt=Math.Min(seconds,1.0/120);seconds-=dt;
            Velocity=Limit(Velocity+Acceleration*dt);
            var next=Position+Velocity*dt;
            var clamped=Clamp(next,bounds);
            Edge xEdge=next.X<bounds.Left?Edge.Left:next.X>bounds.Right?Edge.Right:Edge.None;
            Edge yEdge=next.Y<bounds.Top?Edge.Top:next.Y>bounds.Bottom?Edge.Bottom:Edge.None;
            if(xEdge!=Edge.None)Velocity=Velocity with{X=Collide(Velocity.X,xEdge)};
            if(yEdge!=Edge.None)Velocity=Velocity with{Y=Collide(Velocity.Y,yEdge)};
            Position=clamped;
            if(State==PhysicalState.Throwing&&((Primary!=Edge.None&&
               ((xEdge==Primary&&Math.Abs(Velocity.X)<LandingSpeed)||(yEdge==Primary&&Math.Abs(Velocity.Y)<LandingSpeed)))||
               (Primary==Edge.None&&Math.Abs(Velocity.X)<10&&Math.Abs(Velocity.Y)<10)))
            {Velocity=default;State=PhysicalState.Free;return true;}
        }
        return false;
    }
}
