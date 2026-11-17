namespace CakePet.Core;

// Platform-independent role orchestration. The platform supplies time, bounds and input;
// it never chooses animation names or mutates role state directly.
public sealed class PetEngine : IDisposable
{
    private readonly Random random;
    public RoleRuntime Role { get; }
    public PhysicsBody Body { get; }
    public Bounds Bounds { get; private set; }
    public double DisplaySize { get; private set; }
    public double MaxDisplaySize { get; set; }=400;
    public Point Center=>Body.Position+new Point(DisplaySize/2,DisplaySize/2);
    public bool CanInteract=>Body.State is PhysicalState.Free or PhysicalState.Climbing;
    public bool FeedingEnabled { get; set; }=true;
    public bool GrowOnFeed { get; set; }
    public double FeedGrowth { get; set; }=8;
    public bool AutoForageEnabled { get; private set; }
    public double ForageMultiplier { get; set; }=1;
    public bool CanForage=>AutoForageEnabled&&ClimbEnabled&&!Role.StaticStandby&&CanInteract&&Body.SnappedEdge==Edge.None;
    public const string ForagePath="climb/forage/move";
    public bool Foraging=>Role.StateFunction==ForagePath&&Role.FunctionPath==ForagePath;
    private Point? forageTarget;
    public Func<bool>? ChooseForage { get; set; }
    public bool IsLuring=>Role.StateFunction is "feed/lure" or "snap/feed/lure";
    private string FeedPath(string leaf)=>(Body.SnappedEdge==Edge.None?"feed/":"snap/feed/")+leaf;
    public bool Contains(Point p)=>p.X>=Body.Position.X&&p.X<=Body.Position.X+DisplaySize&&p.Y>=Body.Position.Y&&p.Y<=Body.Position.Y+DisplaySize;
    public int Facing { get; private set; }=1;
    public bool ClimbEnabled { get; private set; }
    public bool LeapEnabled { get; private set; }
    public double LeapProbability { get; set; }=.10;
    public double LeapSpeed { get; set; }=840;
    public Edge RenderEdge=>Body.State==PhysicalState.Flying?Edge.Bottom:
        Body.SnappedEdge!=Edge.None?Body.SnappedEdge:
        Body.ClimbEdge!=Edge.None?Body.ClimbEdge:
        Body.GravitySurface==Edge.None?Edge.Bottom:Body.GravitySurface;
    public PetEngine(RoleRuntime role,Bounds bounds,double displaySize,Point position,Random? random=null)
    {
        if(!double.IsFinite(displaySize)||displaySize<=0)throw new ArgumentOutOfRangeException(nameof(displaySize));
        Role=role;Bounds=bounds;DisplaySize=displaySize;this.random=random??new Random();
        Body=new PhysicsBody(position){SnapEnabled=role.Package.Supports("snap/pool"),ThrowEnabled=role.Package.Supports("touch/roll"),MotionEnabled=!role.StaticStandby};
        Body.Constrain(bounds);
        Role.FrameEntered+=ApplyFrameMotion;
        Role.ChooseAutomaticFunction=()=> (CanForage&&ChooseForage?.Invoke()==true) || (CanStartLeap()&&this.random.NextDouble()<Math.Clamp(LeapProbability,0,1)&&TryStartLeap());
        ApplyFrameMotion(Role.Animation.Current);
    }
    public void UpdateBounds(Bounds bounds)
    {
        if(bounds==Bounds)return;Bounds=bounds;Body.Constrain(bounds);
    }
    public void SetClimbing(bool enabled)
    {
        ClimbEnabled=enabled;
        if(!enabled)StopForage();
        if(!enabled){Body.DetachClimb();Role.SetClimbing(false);}
        else TryAttachCurrent();
    }
    public void SetLeapEnabled(bool enabled)=>LeapEnabled=enabled&&Role.Package.Supports("climb/leap/leap");
    public void SetStaticStandby(bool enabled)
    {
        enabled=enabled&&Role.Package.Supports("standby/static_pool");
        if(Role.StaticStandby==enabled)return;
        if(enabled)StopForage();
        if(enabled&&Body.State==PhysicalState.Climbing){Body.DetachClimb();Role.SetClimbing(false);}
        if(enabled&&Body.State is PhysicalState.Throwing or PhysicalState.Flying)
        {Body.Reset(Body.Position,Bounds);Role.ResetToStandby();}
        Body.MotionEnabled=!enabled;
        Role.SetStaticStandby(enabled);
        if(!enabled)TryAttachCurrent();
    }
    public void BeginDrag(){forageTarget=null;Body.Grab();Role.SetEnvironment(false,PhysicalState.Dragging);}
    public void DragTo(Point p,double velocityX){Body.DragTo(p);Role.UpdateDragCondition(velocityX,DisplaySize);}
    public void EndDrag(Point velocity)
    {
        if(Body.State!=PhysicalState.Dragging)return;
        Body.Release(velocity,Bounds);Role.SetEnvironment(Body.SnappedEdge!=Edge.None,Body.State);
        TryAttachCurrent();
    }
    public void ReturnHome()
    {
        forageTarget=null;
        Body.Reset(new Point((Bounds.Left+Bounds.Right)/2,Bounds.Bottom),Bounds);
        Role.ResetToStandby();TryAttachCurrent();
    }
    public bool PatHead()=>Role.Trigger(Body.SnappedEdge==Edge.None?"pat_head/pat_head":"snap/pat_head/pat_head");
    public bool MenuAction()=>Role.Trigger("menu/pool");
    public void SetAutoForage(bool enabled)
    {AutoForageEnabled=enabled&&Role.Package.Supports(ForagePath);if(!AutoForageEnabled)StopForage();}
    public bool StartForage(Point target)
    {
        if(!CanForage)return false;
        forageTarget=target;TryAttachCurrent();
        if(Body.State!=PhysicalState.Climbing)return false;
        UpdateForageFacing();return Role.EnterSustained(ForagePath);
    }
    public void SetForageTarget(Point target){forageTarget=target;if(Foraging)UpdateForageFacing();}
    public void StopForage(bool resume=true)
    {forageTarget=null;if(Role.StateFunction==ForagePath)Role.EndSustained(resume);}
    public bool StartLure(Point target)
    {
        if(!CanInteract||(!Role.Snapped&&CanForage)||!Role.Package.Supports(FeedPath("lure")))return false;
        if(Role.FunctionPath=="snap/snap_head")return false;
        if(IsLuring){if(Role.FunctionPath==Role.StateFunction)Face(target);return true;}
        Face(target);return Role.EnterSustained(FeedPath("lure"));
    }
    public void EndLure(bool failed)
    {
        if(!IsLuring)return;
        if(failed&&CanInteract&&Role.FunctionPath==Role.StateFunction)Role.EndSustainedWithEvent(FeedPath("lure_fail"));
        else Role.EndSustained();
    }
    public bool Eat(Point target)
    {
        if(!CanInteract)return false;
        Face(target,true);forageTarget=null;
        var path=FeedPath("eat");
        if(IsLuring||Role.StateFunction==ForagePath)return Role.EndSustainedWithEvent(path);
        return Role.Trigger(path);
    }
    public void Grow(double amount)
    {
        if(!double.IsFinite(amount)||amount<=0)return;
        DisplaySize=Math.Min(Math.Max(DisplaySize,MaxDisplaySize),DisplaySize+amount);
    }
    private void Face(Point target,bool eating=false)
    {
        var forward=SurfaceMotion.ToScreen(new Point(1,0),RenderEdge);var delta=target-Center;
        var projection=forward.X*delta.X+forward.Y*delta.Y;
        if(Math.Abs(projection)>=12)Facing=(eating?projection>0:projection<0)?-1:1;
    }
    private void UpdateForageFacing()
    {
        if(forageTarget is not Point target||Body.ClimbEdge==Edge.None)return;
        var route=PerimeterRoute.Calculate(Bounds,Body.Position,Body.ClimbEdge,target-new Point(DisplaySize/2,DisplaySize/2));
        Facing=route.Direction;
    }
    private bool CanStartLeap()=>LeapEnabled&&ClimbEnabled&&!Role.StaticStandby&&Body.SnappedEdge==Edge.None&&
        Body.State is PhysicalState.Free or PhysicalState.Climbing&&Role.StateFunction==null;
    public bool TryStartLeap(Edge? requested=null)
    {
        if(!CanStartLeap())return false;
        var touched=SurfaceMotion.Touching(Body.Position,Bounds);
        var choices=SurfaceMotion.Sides.Where(x=>!touched.HasFlag(x)).ToArray();
        if(choices.Length==0)return false;
        var side=requested??choices[random.Next(choices.Length)];
        if(!choices.Contains(side))return false;
        var p=new Point(Bounds.Left+random.NextDouble()*(Bounds.Right-Bounds.Left),
            Bounds.Top+random.NextDouble()*(Bounds.Bottom-Bounds.Top));
        if(!Body.BeginLeap(side,p,Bounds,LeapSpeed))return false;
        if(Body.Velocity.X!=0)Facing=Body.Velocity.X<0?-1:1;
        Role.SetEnvironment(false,PhysicalState.Flying);return true;
    }
    private void TryAttachCurrent()
    {
        if(!ClimbEnabled||Role.StaticStandby||Body.State!=PhysicalState.Free||Body.SnappedEdge!=Edge.None)return;
        var touched=SurfaceMotion.Touching(Body.Position,Bounds);
        var side=SurfaceMotion.Sides.FirstOrDefault(x=>touched.HasFlag(x));
        if(Body.TryAttach(side,Bounds))Role.SetClimbing(true);
    }
    private void ApplyFrameMotion(Frame frame)
    {
        if(Role.StaticStandby||Body.SnappedEdge!=Edge.None||
           Body.State is not (PhysicalState.Free or PhysicalState.Climbing)||frame.Dx==0&&frame.Dy==0)return;
        var scale=DisplaySize/Role.Package.AnchorRes;
        var local=new Point(frame.Dx*scale*Facing,frame.Dy*scale);
        if(Foraging&&forageTarget is Point target&&Body.ClimbEdge!=Edge.None)
        {
            var route=PerimeterRoute.Calculate(Bounds,Body.Position,Body.ClimbEdge,target-new Point(DisplaySize/2,DisplaySize/2));
            Facing=route.Direction;
            var multiplier=Math.Max(0,ForageMultiplier);
            var travel=frame.Dx*scale*multiplier;
            if(travel>0)travel=Math.Min(travel,Math.Max(0,route.Distance-3));
            local=new Point(travel*Facing,frame.Dy*scale*multiplier);
        }
        var surface=RenderEdge;
        if(Body.State==PhysicalState.Climbing&&local.Y<0){Body.DetachClimb();Role.SetClimbing(false);}
        var delta=SurfaceMotion.ToScreen(local,surface);
        var next=Body.Position+delta;
        var hit=Body.MoveFrame(delta,Bounds);
        if(Body.State==PhysicalState.Climbing)
        {
            var side=Body.ClimbEdge;
            var atEnd=side is Edge.Top or Edge.Bottom
                ? (delta.X<0&&next.X<=Bounds.Left)||(delta.X>0&&next.X>=Bounds.Right)
                : (delta.Y<0&&next.Y<=Bounds.Top)||(delta.Y>0&&next.Y>=Bounds.Bottom);
            if(atEnd)Body.TryAttach(SurfaceMotion.NextWall(side,delta),Bounds);
        }
        else if(hit&&local.X!=0)Facing=-Facing;
    }
    public void Step(long milliseconds)
    {
        if(milliseconds<0)throw new ArgumentOutOfRangeException(nameof(milliseconds));
        var previous=Body.State;
        if(Body.Advance(milliseconds/1000.0,Bounds))
        {
            if(ClimbEnabled&&!Role.StaticStandby&&Body.TryAttach(Body.LastImpactEdge,Bounds)){}
            Role.CompletePhysicalMotion(previous==PhysicalState.Flying?"climb/leap/landing":"touch/jump_down",Body.State);
        }
        if(Foraging)UpdateForageFacing();
        Role.Advance(milliseconds);
        TryAttachCurrent();
    }
    public void Dispose()
    {
        Role.FrameEntered-=ApplyFrameMotion;Role.ChooseAutomaticFunction=null;ChooseForage=null;
    }
}
