namespace CakePet.Core;

public readonly record struct Point(double X,double Y)
{
    public static Point operator +(Point a,Point b)=>new(a.X+b.X,a.Y+b.Y);
    public static Point operator *(Point a,double scale)=>new(a.X*scale,a.Y*scale);
}
public readonly record struct Bounds(double Left,double Top,double Right,double Bottom);
public sealed class PhysicsBody
{
    public Point Position { get; private set; }
    public Point Velocity { get; private set; }
    public PhysicalState State { get; private set; }
    public double Gravity { get; set; }=1800;
    public double Bounce { get; set; }=.35;
    public PhysicsBody(Point position)=>Position=position;
    public void Grab(){State=PhysicalState.Dragging;Velocity=default;}
    public void DragTo(Point point){if(State==PhysicalState.Dragging)Position=point;}
    public void Release(Point velocity){Velocity=velocity;State=PhysicalState.Throwing;}
    public void MoveFrame(Point delta){if(State==PhysicalState.Free)Position+=delta;}
    public bool Advance(double seconds,Bounds bounds)
    {
        if(!double.IsFinite(seconds)||seconds<0)throw new ArgumentOutOfRangeException(nameof(seconds));
        if(State!=PhysicalState.Throwing)return false;
        var landed=false;
        while(seconds>0)
        {
            var dt=Math.Min(seconds,1.0/120);seconds-=dt;
            Velocity+=new Point(0,Gravity*dt);Position+=Velocity*dt;
            var x=Math.Clamp(Position.X,bounds.Left,bounds.Right);
            var y=Math.Clamp(Position.Y,bounds.Top,bounds.Bottom);
            if(x!=Position.X)Velocity=new Point(-Velocity.X*Bounce,Velocity.Y);
            if(y!=Position.Y)Velocity=new Point(Velocity.X,-Velocity.Y*Bounce);
            Position=new Point(x,y);
            if(y==bounds.Bottom&&Math.Abs(Velocity.Y)<70)
            {Velocity=default;State=PhysicalState.Free;landed=true;break;}
        }
        return landed;
    }
}
