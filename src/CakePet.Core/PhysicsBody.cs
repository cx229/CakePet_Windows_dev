namespace CakePet.Core;

public readonly record struct Point(double X,double Y)
{
    public double Length=>Math.Sqrt(X*X+Y*Y);
    public static Point operator +(Point a,Point b)=>new(a.X+b.X,a.Y+b.Y);
    public static Point operator -(Point a,Point b)=>new(a.X-b.X,a.Y-b.Y);
    public static Point operator *(Point a,double scale)=>new(a.X*scale,a.Y*scale);
}
public readonly record struct Bounds(double Left,double Top,double Right,double Bottom);
[Flags] public enum Edge { None=0,Bottom=1,Left=2,Right=4,Top=8,All=15 }

public static class SurfaceMotion
{
    public static Point ToScreen(Point local,Edge edge)=>edge switch
    {Edge.Left=>new(-local.Y,local.X),Edge.Right=>new(local.Y,-local.X),Edge.Top=>new(-local.X,-local.Y),_=>local};
    public static Point OnEdge(Point p,Bounds b,Edge edge)=>edge switch
    {Edge.Bottom=>p with{Y=b.Bottom},Edge.Top=>p with{Y=b.Top},Edge.Left=>p with{X=b.Left},Edge.Right=>p with{X=b.Right},_=>p};
    public static Edge Touching(Point p,Bounds b,double tolerance=.5)
    {
        Edge result=Edge.None;
        if(Math.Abs(p.Y-b.Bottom)<=tolerance)result|=Edge.Bottom;
        if(Math.Abs(p.Y-b.Top)<=tolerance)result|=Edge.Top;
        if(Math.Abs(p.X-b.Left)<=tolerance)result|=Edge.Left;
        if(Math.Abs(p.X-b.Right)<=tolerance)result|=Edge.Right;
        return result;
    }
    public static Edge NextWall(Edge current,Point delta)=>current switch
    {
        Edge.Bottom or Edge.Top=>delta.X>0?Edge.Right:delta.X<0?Edge.Left:current,
        Edge.Left or Edge.Right=>delta.Y>0?Edge.Bottom:delta.Y<0?Edge.Top:current,
        _=>Edge.None
    };
    public static bool IsSingle(Edge e)=>e is Edge.Bottom or Edge.Top or Edge.Left or Edge.Right;
    public static readonly IReadOnlyList<Edge> Sides=Array.AsReadOnly(new[]{Edge.Bottom,Edge.Left,Edge.Right,Edge.Top});
}

public sealed class PhysicsBody
{
    public Func<Point,Point,MotionProjection?>? ProjectMotion {get;set;}
    public Func<Point,Point,SpaceMotion>? SpaceMotionProject {get;set;}
    public bool UseDesktopSpace {get;set;}
    public void AttachSurface(Edge side,Point origin,bool snapped=false){if(!SurfaceMotion.IsSingle(side))return;Position=origin;Velocity=default;ClearPhysicalState();if(snapped)SnappedEdge=side;else{State=PhysicalState.Climbing;ClimbEdge=side;}}
    public Point Position { get; private set; }
    public Point Velocity { get; private set; }
    public PhysicalState State { get; private set; }
    public Edge SnappedEdge { get; private set; }
    public Edge ClimbEdge { get; private set; }
    public Edge LastImpactEdge { get; private set; }
    public Edge LeapTargetEdge { get; private set; }
    public Point? LeapTarget { get; private set; }
    public Edge LeapLaunchEdges {get;private set;}
    public Func<Point,Edge>? FlightContacts {get;set;}
    private bool leapLanded;
    private Edge leapFrameHit;
    public double Gravity { get; set; }=1500;
    public double Bounce { get; set; }=.7;
    public double MaxSpeed { get; set; }=2000;
    public double LandingSpeed { get; set; }=200;
    public double SnapSpeed { get; set; }=800;
    public double SnapDistance { get; set; }=100;
    public double ThrowCoefficient { get; set; }=1;
    public bool MotionEnabled { get; set; }=true;
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
    public void SetPosition(Point point)=>Position=point;
    public void Place(Point point,Bounds? bounds=null){Position=bounds is Bounds b?Clamp(point,b):point;Velocity=default;}
    private void ClearPhysicalState()
    {
        State=PhysicalState.Free;SnappedEdge=ClimbEdge=LastImpactEdge=LeapTargetEdge=Edge.None;LeapTarget=null;LeapLaunchEdges=Edge.None;leapLanded=false;leapFrameHit=Edge.None;
    }
    public void Reset(Point point,Bounds? bounds=null){Place(point,bounds);ClearPhysicalState();}
    public void Grab(){ClearPhysicalState();State=PhysicalState.Dragging;Velocity=default;}
    public void DragTo(Point point){if(State==PhysicalState.Dragging)Position=point;}
    public void Constrain(Bounds b)
    {
        if(UseDesktopSpace)return;
        if(State!=PhysicalState.Dragging)Position=Clamp(Position,b);
        if(SnappedEdge!=Edge.None)Position=SurfaceMotion.OnEdge(Position,b,SnappedEdge);
        if(ClimbEdge!=Edge.None)Position=SurfaceMotion.OnEdge(Position,b,ClimbEdge);
        
    }
    public void Release(Point velocity,Bounds? bounds=null,Bounds? activity=null,Point anchor=default,Point? pointer=null)
    {
        ClearPhysicalState();
        if(bounds is Bounds b)
        {
            var original=Position;
            Position=Clamp(Position,b);
            if(SnapEnabled&&(MotionEnabled?velocity.Length:0)<=SnapSpeed)
            {
                var p=activity.HasValue?(pointer??original+anchor):Position;var rect=activity??b;
                var sides=new[]{(Edge.Bottom,Math.Abs(rect.Bottom-p.Y)),(Edge.Left,Math.Abs(p.X-rect.Left)),(Edge.Right,Math.Abs(rect.Right-p.X)),(Edge.Top,Math.Abs(p.Y-rect.Top))};
                var nearest=sides.Where(x=>SnapEdges.HasFlag(x.Item1)&&x.Item2<=SnapDistance).OrderBy(x=>x.Item2).FirstOrDefault();
                if(nearest.Item1!=Edge.None)
                {
                    SnappedEdge=nearest.Item1;Position=SurfaceMotion.OnEdge(Position,b,SnappedEdge);
                    Velocity=default;return;
                }
            }
        }
        var throwing=MotionEnabled&&GravityEnabled&&ThrowEnabled;
        Velocity=throwing?Limit(velocity*ThrowCoefficient):default;
        State=throwing?PhysicalState.Throwing:PhysicalState.Free;
    }
    public bool TryAttach(Edge side,Bounds bounds,double tolerance=.5)
    {
        if(!MotionEnabled||SnappedEdge!=Edge.None||State is not (PhysicalState.Free or PhysicalState.Climbing)||
           !SurfaceMotion.IsSingle(side)||!SurfaceMotion.Touching(Position,bounds,tolerance).HasFlag(side))return false;
        State=PhysicalState.Climbing;ClimbEdge=side;Velocity=default;
        Position=SurfaceMotion.OnEdge(Clamp(Position,bounds),bounds,side);return true;
    }
    public void DetachClimb(){if(State==PhysicalState.Climbing){State=PhysicalState.Free;ClimbEdge=Edge.None;Velocity=default;}}
    public bool BeginLeap(Edge targetEdge,Point target,Bounds bounds,double speed)
    {
        if(!MotionEnabled||SnappedEdge!=Edge.None||State is not (PhysicalState.Free or PhysicalState.Climbing)||
           !SurfaceMotion.IsSingle(targetEdge)||SurfaceMotion.Touching(Position,bounds).HasFlag(targetEdge)||
           !double.IsFinite(speed)||speed<=0)return false;
        target=SurfaceMotion.OnEdge(Clamp(target,bounds),bounds,targetEdge);
        return BeginLeapDirection(targetEdge,target,target-Position,SurfaceMotion.Touching(Position,bounds),speed);
    }
    public bool BeginLeapDirection(Edge targetEdge,Point target,Point direction,Edge launchEdges,double speed)
    {
        if(!MotionEnabled||SnappedEdge!=Edge.None||State is not (PhysicalState.Free or PhysicalState.Climbing)||
           !SurfaceMotion.IsSingle(targetEdge)||launchEdges.HasFlag(targetEdge)||!double.IsFinite(speed)||speed<=0||direction.Length<.001)return false;
        ClearPhysicalState();State=PhysicalState.Flying;LeapTargetEdge=targetEdge;LeapTarget=target;LeapLaunchEdges=launchEdges;
        Velocity=direction*(speed/direction.Length);return true;
    }
    public bool MoveFrame(Point delta,Bounds? bounds=null)
    {
        if(!MotionEnabled||State is not (PhysicalState.Free or PhysicalState.Climbing or PhysicalState.Flying)||SnappedEdge!=Edge.None)return false;
        if(State==PhysicalState.Flying){if(leapLanded)return true;return MoveFlight(delta,bounds);}
        var next=Position+delta;
        if(SpaceMotionProject!=null){var motion=SpaceMotionProject(Position,next);Position=motion.Position;return motion.Hit!=Edge.None;}
        if(bounds.HasValue&&ProjectMotion?.Invoke(Position,next) is MotionProjection projection){next=projection.Position;bounds=projection.Bounds;}
        Position=bounds is Bounds b?Clamp(next,b):next;
        if(bounds is Bounds limits&&ClimbEdge!=Edge.None)Position=SurfaceMotion.OnEdge(Position,limits,ClimbEdge);
        return Position!=next;
    }
    private bool MoveFlight(Point delta,Bounds? bounds)
    {
        if(delta.Length<1e-9)return false;
        var next=Position+delta;Edge hit=Edge.None;
        if(SpaceMotionProject!=null){var motion=SpaceMotionProject(Position,next);Position=motion.Position;hit=motion.Hit;}
        else if(bounds is Bounds box)
        {
            Position=Clamp(next,box);
            if(delta.X<0&&next.X<=box.Left)hit|=Edge.Left;
            if(delta.X>0&&next.X>=box.Right)hit|=Edge.Right;
            if(delta.Y<0&&next.Y<=box.Top)hit|=Edge.Top;
            if(delta.Y>0&&next.Y>=box.Bottom)hit|=Edge.Bottom;
        }
        else Position=next;
        hit&=~LeapLaunchEdges;
        if(FlightContacts!=null)LeapLaunchEdges&=FlightContacts(Position);
        else if(bounds is Bounds current)LeapLaunchEdges&=SurfaceMotion.Touching(Position,current);
        if(hit==Edge.None)return false;
        leapFrameHit=hit.HasFlag(LeapTargetEdge)?LeapTargetEdge:SurfaceMotion.Sides.First(e=>hit.HasFlag(e));
        leapLanded=true;return true;
    }
    private Point Acceleration=>!MotionEnabled||!GravityEnabled?default:new Point(
        Gravity*((GravityEdges.HasFlag(Edge.Right)?1:0)-(GravityEdges.HasFlag(Edge.Left)?1:0)),
        Gravity*((GravityEdges.HasFlag(Edge.Bottom)?1:0)-(GravityEdges.HasFlag(Edge.Top)?1:0)));
    public Edge GravitySurface=>Acceleration.Y>0?Edge.Bottom:Acceleration.Y<0?Edge.Top:Acceleration.X<0?Edge.Left:Acceleration.X>0?Edge.Right:Edge.None;
    private double Collide(double velocity,Edge edge)=>State==PhysicalState.Throwing&&BounceEnabled&&BounceEdges.HasFlag(edge)?-velocity*Math.Clamp(Bounce,0,1):0;
    public bool Advance(double seconds,Bounds bounds)
    {
        if(!double.IsFinite(seconds)||seconds<0)throw new ArgumentOutOfRangeException(nameof(seconds));
        if(!MotionEnabled||SnappedEdge!=Edge.None)return false;
        if(State==PhysicalState.Flying)
        {
            if(!leapLanded)MoveFlight(Velocity*seconds,bounds);
            if(!leapLanded)return false;
            LastImpactEdge=leapFrameHit;Velocity=default;State=PhysicalState.Free;LeapTargetEdge=Edge.None;LeapTarget=null;LeapLaunchEdges=Edge.None;leapLanded=false;leapFrameHit=Edge.None;return true;
        }
        if(State is not (PhysicalState.Free or PhysicalState.Throwing))return false;
        while(seconds>0)
        {
            var dt=Math.Min(seconds,1.0/120);seconds-=dt;
            Velocity=Limit(Velocity+Acceleration*dt);
            var next=Position+Velocity*dt;
            if(ProjectMotion?.Invoke(Position,next) is MotionProjection projection){next=projection.Position;bounds=projection.Bounds;}
            if(SpaceMotionProject!=null){var motion=SpaceMotionProject(Position,next);Position=motion.Position;var horizontal=motion.Hit&(Edge.Left|Edge.Right);var vertical=motion.Hit&(Edge.Top|Edge.Bottom);if(horizontal!=Edge.None)Velocity=Velocity with{X=Collide(Velocity.X,horizontal.HasFlag(Edge.Left)?Edge.Left:Edge.Right)};if(vertical!=Edge.None)Velocity=Velocity with{Y=Collide(Velocity.Y,vertical.HasFlag(Edge.Top)?Edge.Top:Edge.Bottom)};var gravity=GravitySurface;if(State==PhysicalState.Throwing&&((gravity!=Edge.None&&motion.Hit.HasFlag(gravity)&&(gravity is Edge.Left or Edge.Right?Math.Abs(Velocity.X):Math.Abs(Velocity.Y))<LandingSpeed)||(gravity==Edge.None&&Velocity.Length<10))){Velocity=default;State=PhysicalState.Free;LastImpactEdge=gravity;return true;}continue;}
            var clamped=Clamp(next,bounds);
            Edge xEdge=next.X<bounds.Left?Edge.Left:next.X>bounds.Right?Edge.Right:Edge.None;
            Edge yEdge=next.Y<bounds.Top?Edge.Top:next.Y>bounds.Bottom?Edge.Bottom:Edge.None;
            if(xEdge!=Edge.None)Velocity=Velocity with{X=Collide(Velocity.X,xEdge)};
            if(yEdge!=Edge.None)Velocity=Velocity with{Y=Collide(Velocity.Y,yEdge)};
            Position=clamped;
            var primary=GravitySurface;
            if(State==PhysicalState.Throwing&&((primary!=Edge.None&&
               ((xEdge==primary&&Math.Abs(Velocity.X)<LandingSpeed)||(yEdge==primary&&Math.Abs(Velocity.Y)<LandingSpeed)))||
               (primary==Edge.None&&Math.Abs(Velocity.X)<10&&Math.Abs(Velocity.Y)<10)))
            {Velocity=default;State=PhysicalState.Free;LastImpactEdge=primary;return true;}
        }
        return false;
    }
}
