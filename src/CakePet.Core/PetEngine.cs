namespace CakePet.Core;

// Platform-independent role orchestration. The platform supplies time, bounds and input;
// it never chooses animation names or mutates role state directly.
public sealed class PetEngine : IDisposable
{
    private readonly Random random;
    public RoleRuntime Role { get; }
    public PhysicsBody Body { get; }
    public Bounds Bounds { get; private set; }
    public Bounds? ActivityArea {get;private set;}
    private Func<Frame,FrameGeometry>? geometryProvider;
    public Func<Bounds,Bounds>? ConnectedLimits {get;set;}
    public Func<Edge>? ConnectedEdges {get;set;}
    public Func<Point,Point,MotionProjection?>? ConnectedMotion {get;set;}
    public void AdoptMotionArea(Bounds area,Bounds limits){ActivityArea=area;Bounds=limits;}
    public DesktopSpace? Space {get;private set;}
    public bool HorizontalWrap {get;private set;}
    private int? surfaceSegment;
    private bool startingLeap;
    private Point? flightAnchorOffset;
    private Point? flightLaunchAnchor;
    public Point? FlightLaunchAnchor=>flightLaunchAnchor;
    public Edge LastFlightImpact {get;private set;}
    private Point? surfacePosition;
    private Point AnchorWorld=>Body.Position+AnchorOffset;
    public void SetDesktopSpace(DesktopSpace? space,bool wrap)
    {
        if(ReferenceEquals(Space,space)&&HorizontalWrap==wrap)return;
        Space=space;HorizontalWrap=wrap;surfaceSegment=null;surfacePosition=null;Body.UseDesktopSpace=space!=null;Body.SpaceMotionProject=space==null?null:ProjectSpace;Body.FlightContacts=space==null?null:q=>LaunchContactAt(q+AnchorOffset);
        if(space!=null&&Body.State!=PhysicalState.Dragging)ReconcileSpace();
    }
    private Edge LaunchContactAt(Point point)=>Space!=null&&flightLaunchAnchor is Point start?Space.LaunchContacts(start,point,HorizontalWrap):Edge.None;
    private SpaceMotion ProjectSpace(Point from,Point next)
    {
        var anchor=AnchorOffset;var flying=Body.State==PhysicalState.Flying;
        var result=Space!.Move(from+anchor,next-from,HorizontalWrap,stopAtHit:flying,ignoreStopAt:flying?q=>LaunchContactAt(q)&Body.LeapLaunchEdges:null);
        return result with{Position=result.Position-anchor};
    }
    private void ReconcileSpace()
    {
        if(Space==null||Body.State==PhysicalState.Dragging)return;
        if(Body.State==PhysicalState.Flying){surfaceSegment=null;surfacePosition=null;return;}
        if(Body.ClimbEdge!=Edge.None||Body.SnappedEdge!=Edge.None)
        {
            var side=Body.SnappedEdge!=Edge.None?Body.SnappedEdge:Body.ClimbEdge;
            if(surfaceSegment is int i&&surfacePosition is Point p&&i<Space.Edges.Count&&Space.Edges[i].Side==side&&Space.IsWalkable(i,HorizontalWrap)){Body.SetPosition(p-AnchorOffset);return;}
            var contact=Space.Nearest(AnchorWorld,side,wrap:HorizontalWrap);
            if(contact!=null&&contact.Value.Distance<=Math.Max(2,DisplaySize)){surfaceSegment=contact.Value.Segment;surfacePosition=contact.Value.Position;Body.SetPosition(contact.Value.Position-AnchorOffset);return;}
            if(Body.SnappedEdge!=Edge.None){Body.Reset(Space.Clamp(AnchorWorld)-AnchorOffset);Role.ResetToStandby();}else{Body.DetachClimb();Role.SetClimbing(false);}
        }
        surfaceSegment=null;surfacePosition=null;Body.SetPosition(Space.Clamp(AnchorWorld)-AnchorOffset);
    }
    private BoundaryContact? CurrentContact()
    {
        if(Space==null)return null;var side=Body.ClimbEdge!=Edge.None?Body.ClimbEdge:Body.SnappedEdge!=Edge.None?Body.SnappedEdge:RenderEdge;
        if(surfaceSegment is int i&&surfacePosition is Point p&&i<Space.Edges.Count&&Space.Edges[i].Side==side&&Space.IsWalkable(i,HorizontalWrap)&&(Body.ClimbEdge!=Edge.None||Body.SnappedEdge!=Edge.None))return new(i,p,0);
        return Space.Nearest(AnchorWorld,side,wrap:HorizontalWrap)??Space.Nearest(AnchorWorld,wrap:HorizontalWrap);
    }
    private bool AttachSpace(BoundaryContact contact,bool snap=false)
    {
        if(Space==null||!Space.IsWalkable(contact.Segment,HorizontalWrap))return false;var edge=Space.Edges[contact.Segment].Side;
        Body.AttachSurface(edge,Body.Position,snap);surfaceSegment=contact.Segment;surfacePosition=contact.Position;Body.SetPosition(contact.Position-AnchorOffset);if(!snap)Role.SetClimbing(true);return true;
    }
    public bool InActivitySpace(Point p)=>Space?.Contains(p)??(ActivityArea is Bounds b&&p.X>=b.Left&&p.X<=b.Right&&p.Y>=b.Top&&p.Y<=b.Bottom);
    public Point? FindEdgeFoodPosition(double width,double height,Random random)=>Space!=null&&(CurrentContact()??Space.Nearest(AnchorWorld,wrap:HorizontalWrap)) is BoundaryContact c?Space.SpawnEdge(c.Segment,width,height,HorizontalWrap,random):null;
    public double ForageDistance(Point target)
    {
        if(Space!=null&&CurrentContact() is BoundaryContact c)return Space.Route(c.Segment,c.Position,target,HorizontalWrap)?.Distance??double.PositiveInfinity;
        return PerimeterRoute.Calculate(Bounds,Body.Position,Body.ClimbEdge==Edge.None?RenderEdge:Body.ClimbEdge,target-new Point(DisplaySize/2,DisplaySize/2)).Distance;
    }
    public Point AnchorOffset=>AnchorAt(DisplaySize);
    private Point AnchorAt(double size)
    {
        if(geometryProvider==null)return AnchorGeometry.Transform(new Point(size/2,size),size,RenderEdge,Facing);
        var g=geometryProvider(Role.Animation.Current);var scale=size/Role.Package.AnchorRes;
        return AnchorGeometry.Transform(new Point(g.AnchorX*scale,g.AnchorY*scale),size,RenderEdge,Facing);
    }
    public void SetActivityArea(Bounds area,Func<Frame,FrameGeometry> geometry)
    {if(ActivityArea==area&&geometryProvider==geometry)return;ActivityArea=area;geometryProvider=geometry;RefreshGeometry();}
    public void RefreshGeometry()
    {
        if(ActivityArea is Bounds area&&geometryProvider!=null)
            {var limits=AnchorGeometry.WindowBounds(area,DisplaySize,AnchorOffset,RenderEdge);UpdateBounds(Space!=null?limits:ConnectedLimits?.Invoke(limits)??limits);if(Space!=null)ReconcileSpace();}
    }
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
        Body.ProjectMotion=(from,next)=>ConnectedMotion?.Invoke(from,next);
        Role.FrameEntered+=OnFrame;
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
        if(!enabled){surfaceSegment=null;surfacePosition=null;Body.DetachClimb();Role.SetClimbing(false);}
        else TryAttachCurrent();
        RefreshGeometry();
    }
    public void SetLeapEnabled(bool enabled)=>LeapEnabled=enabled&&Role.Package.Supports("climb/leap/leap");
    public void SetStaticStandby(bool enabled)
    {
        enabled=enabled&&Role.Package.Supports("standby/static_pool");
        if(Role.StaticStandby==enabled)return;
        if(enabled)StopForage();
        if(enabled&&Body.State==PhysicalState.Climbing){surfaceSegment=null;surfacePosition=null;Body.DetachClimb();Role.SetClimbing(false);}
        if(enabled&&Body.State is PhysicalState.Throwing or PhysicalState.Flying)
        {Body.Reset(Body.Position,Bounds);Role.ResetToStandby();}
        Body.MotionEnabled=!enabled;
        Role.SetStaticStandby(enabled);
        if(!enabled)TryAttachCurrent();
        RefreshGeometry();
    }
    public void BeginDrag(){surfaceSegment=null;surfacePosition=null;forageTarget=null;Body.Grab();Role.SetEnvironment(false,PhysicalState.Dragging);RefreshGeometry();}
    public void DragTo(Point p,double velocityX){Body.DragTo(p);Role.UpdateDragCondition(velocityX,DisplaySize);}
    public void EndDrag(Point velocity,Point? pointer=null)
    {
        if(Body.State!=PhysicalState.Dragging)return;
        if(Space!=null)
        {
            var snap=Body.SnapEnabled&&(!Body.MotionEnabled||velocity.Length<=Body.SnapSpeed)?Space.Nearest(pointer??AnchorWorld,Body.SnapEdges,wrap:HorizontalWrap):null;
            if(snap is BoundaryContact contact&&contact.Distance<=Body.SnapDistance){AttachSpace(contact,true);Role.SetEnvironment(true,PhysicalState.Free);}
            else{Body.Release(velocity);Body.SetPosition(Space.Clamp(AnchorWorld)-AnchorOffset);Role.SetEnvironment(false,Body.State);}
        }
        else{Body.Release(velocity,Bounds,ActivityArea,AnchorOffset,pointer);Role.SetEnvironment(Body.SnappedEdge!=Edge.None,Body.State);}
        RefreshGeometry();TryAttachCurrent();
    }
    public void ReturnHome()
    {
        surfaceSegment=null;surfacePosition=null;forageTarget=null;
        Body.Reset(Body.Position);
        Role.ResetToStandby();RefreshGeometry();
        Body.Reset(new Point((Bounds.Left+Bounds.Right)/2,Bounds.Bottom),Bounds);TryAttachCurrent();RefreshGeometry();
    }
    public bool PatHead()=>Role.Trigger(Body.SnappedEdge==Edge.None?"pat_head/pat_head":"snap/pat_head/pat_head");
    public bool MenuAction(string mode)=>Role.TriggerMode("menu/pool",mode);
    public void SetAutoForage(bool enabled)
    {AutoForageEnabled=enabled&&Role.Package.Supports(ForagePath);if(!AutoForageEnabled)StopForage();}
    public bool StartForage(Point target)
    {
        if(!CanForage)return false;
        forageTarget=target;TryAttachCurrent();
        if(Body.State!=PhysicalState.Climbing||!double.IsFinite(ForageDistance(target))){forageTarget=null;return false;}
        UpdateForageFacing();return Role.EnterSustained(ForagePath);
    }
    public void SetForageTarget(Point target){forageTarget=target;if(Foraging)UpdateForageFacing();}
    public void StopForage(bool resume=true)
    {forageTarget=null;if(Role.StateFunction==ForagePath)Role.EndSustained(resume);}
    public bool StartLure(Point target)
    {
        if(!CanInteract||(!Role.Snapped&&CanForage)||!Role.Package.Supports(FeedPath("lure")))return false;
        if(Role.FunctionPath is "snap/snap_head" or "feed/eat" or "snap/feed/eat")return false;
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
        Resize(Math.Min(Math.Max(DisplaySize,MaxDisplaySize),DisplaySize+amount));
    }
    public void Resize(double size)
    {
        if(!double.IsFinite(size)||size<=0||size==DisplaySize)return;
        var diff=size-DisplaySize;var old=Body.Position;var oldAnchor=AnchorAt(DisplaySize);
        var offset=Body.State is PhysicalState.Dragging or PhysicalState.Throwing?default:RenderEdge switch
        {Edge.Left=>new Point(0,-diff/2),Edge.Right=>new Point(-diff,-diff/2),Edge.Top=>new Point(-diff/2,0),_=>new Point(-diff/2,-diff)};
        DisplaySize=size;
        if(geometryProvider!=null&&ActivityArea!=null)
        {
            Body.SetPosition(old+(Body.State is PhysicalState.Dragging or PhysicalState.Throwing?default:oldAnchor-AnchorAt(size)));
            if(Body.State==PhysicalState.Flying)flightAnchorOffset=AnchorOffset;
            RefreshGeometry();return;
        }
        // Window bounds are refreshed by the host; preserve the support-side anchor approximately.
        Bounds=new Bounds(Bounds.Left,Bounds.Top,Math.Max(Bounds.Left,Bounds.Right-diff),Math.Max(Bounds.Top,Bounds.Bottom-diff));
        Body.SetPosition(old+offset);Body.Constrain(Bounds);
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
        if(Space!=null&&CurrentContact() is BoundaryContact contact){if(Space.Route(contact.Segment,contact.Position,target,HorizontalWrap) is {} desktopRoute){Facing=-desktopRoute.Direction;Body.SetPosition(contact.Position-AnchorOffset);}return;}
        var route=PerimeterRoute.Calculate(Bounds,Body.Position,Body.ClimbEdge,target-new Point(DisplaySize/2,DisplaySize/2));
        Facing=route.Direction;
    }
    private bool CanStartLeap()=>LeapEnabled&&ClimbEnabled&&!Role.StaticStandby&&Body.SnappedEdge==Edge.None&&
        Body.State is PhysicalState.Free or PhysicalState.Climbing&&Role.StateFunction==null;
    public bool TryStartLeap(Edge? requested=null)
    {
        if(!CanStartLeap())return false;
        if(Space!=null)
        {
            var start=AnchorWorld;var launch=Space.Touching(start,HorizontalWrap);
            var choice=Space.LeapTarget(start,launch,requested,HorizontalWrap,random);
            if(choice is not {} selected)return false;
            var delta=selected.Target-start;
            if(!Body.BeginLeapDirection(selected.Side,selected.Target,delta,launch,LeapSpeed))return false;
            surfaceSegment=null;surfacePosition=null;flightLaunchAnchor=start;LastFlightImpact=Edge.None;
            if(Math.Abs(delta.X)>1e-7)Facing=delta.X<0?-1:1;
            startingLeap=true;
            try{Role.SetEnvironment(false,PhysicalState.Flying);Body.SetPosition(start-AnchorOffset);flightAnchorOffset=AnchorOffset;}
            finally{startingLeap=false;}
            RefreshGeometry();ApplyFrameMotion(Role.Animation.Current);return true;
        }
        var touched=SurfaceMotion.Touching(Body.Position,Bounds);
        var open=ConnectedEdges?.Invoke()??Edge.None;var choices=SurfaceMotion.Sides.Where(x=>!touched.HasFlag(x)&&!open.HasFlag(x)).ToArray();
        if(choices.Length==0)return false;
        var side=requested??choices[random.Next(choices.Length)];
        if(!choices.Contains(side))return false;
        var p=new Point(Bounds.Left+random.NextDouble()*(Bounds.Right-Bounds.Left),
            Bounds.Top+random.NextDouble()*(Bounds.Bottom-Bounds.Top));
        if(!Body.BeginLeap(side,p,Bounds,LeapSpeed))return false;
        flightLaunchAnchor=Body.Position;LastFlightImpact=Edge.None;
        if(Body.Velocity.X!=0)Facing=Body.Velocity.X<0?-1:1;
        startingLeap=true;try{Role.SetEnvironment(false,PhysicalState.Flying);}finally{startingLeap=false;}
        RefreshGeometry();ApplyFrameMotion(Role.Animation.Current);return true;
    }
    private void TryAttachCurrent()
    {
        if(!ClimbEnabled||Role.StaticStandby||Body.State!=PhysicalState.Free||Body.SnappedEdge!=Edge.None)return;
        if(Space!=null){var contact=Space.Nearest(AnchorWorld,wrap:HorizontalWrap);if(contact is BoundaryContact c&&c.Distance<=.5)AttachSpace(c);return;}
        var touched=SurfaceMotion.Touching(Body.Position,Bounds);
        var open=ConnectedEdges?.Invoke()??Edge.None;
        var side=SurfaceMotion.Sides.FirstOrDefault(x=>touched.HasFlag(x)&&!open.HasFlag(x));
        if(Body.TryAttach(side,Bounds)){Role.SetClimbing(true);RefreshGeometry();}
    }
    private void OnFrame(Frame frame)
    {
        if(startingLeap)return;
        if(Space!=null&&Body.State==PhysicalState.Flying)
        {
            if(flightAnchorOffset is Point old)Body.SetPosition(Body.Position+old-AnchorOffset);
            flightAnchorOffset=AnchorOffset;
        }
        RefreshGeometry();ApplyFrameMotion(frame);RefreshGeometry();
    }
    private void ApplyFrameMotion(Frame frame)
    {
        if(Role.StaticStandby||Body.SnappedEdge!=Edge.None||
           Body.State is not (PhysicalState.Free or PhysicalState.Climbing or PhysicalState.Flying)||frame.Dx==0&&frame.Dy==0)return;
        var scale=DisplaySize/Role.Package.AnchorRes;
        var local=new Point(frame.Dx*scale*Facing,frame.Dy*scale);
        if(Body.State==PhysicalState.Flying){Body.MoveFrame(local,Bounds);return;}
        // Attach before applying the frame, so the first small step at a corner uses the contour.
        if(Space!=null&&local.Y>=0)TryAttachCurrent();
        if(Space!=null&&Body.State==PhysicalState.Climbing&&CurrentContact() is BoundaryContact contact&&local.Y>=0)
        {
            double travel=local.X;if(Foraging&&forageTarget is Point food&&Space.Route(contact.Segment,contact.Position,food,HorizontalWrap) is {} route){Facing=-route.Direction;travel=-route.Direction*Math.Min(Math.Abs(frame.Dx*scale*ForageMultiplier),Math.Max(0,route.Distance));}
            var result=Space.Walk(contact.Segment,contact.Position,-travel,HorizontalWrap);AttachSpace(new BoundaryContact(result.Segment,result.Position,0));return;
        }
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
        if(Body.State==PhysicalState.Climbing&&local.Y<0){surfaceSegment=null;surfacePosition=null;Body.DetachClimb();Role.SetClimbing(false);}
        var delta=SurfaceMotion.ToScreen(local,surface);
        var next=Body.Position+delta;
        var hit=Body.MoveFrame(delta,Bounds);
        if(Space!=null){if(hit&&local.X!=0)Facing=-Facing;return;}
        if(Body.State==PhysicalState.Climbing)
        {
            var side=Body.ClimbEdge;
            var end=SurfaceMotion.NextWall(side,delta);var atEnd=SurfaceMotion.Touching(Body.Position,Bounds).HasFlag(end);
            if(atEnd&&!(ConnectedEdges?.Invoke()??Edge.None).HasFlag(end)&&Body.TryAttach(end,Bounds))RefreshGeometry();
        }
        else if(hit&&local.X!=0)Facing=-Facing;
    }
    public void Step(long milliseconds)
    {
        if(milliseconds<0)throw new ArgumentOutOfRangeException(nameof(milliseconds));
        RefreshGeometry();AdvancePhysical(milliseconds/1000.0);
        if(Foraging)UpdateForageFacing();
        Role.Advance(milliseconds);
        // Frame displacement can hit a wall during animation advancement. Finish that
        // flight now, after the cursor callback has unwound, without advancing time twice.
        if(Body.State==PhysicalState.Flying)AdvancePhysical(0);
        TryAttachCurrent();
    }
    private void AdvancePhysical(double seconds)
    {
        var previous=Body.State;var beforeAnchor=AnchorOffset;
        if(!Body.Advance(seconds,Bounds))return;
        var impact=Body.LastImpactEdge;
        Point? impactAnchor=null;
        if(previous==PhysicalState.Flying)
        {
            LastFlightImpact=impact;
            if(Space!=null){impactAnchor=Body.Position+beforeAnchor;Body.SetPosition(impactAnchor.Value-AnchorOffset);}
        }
        if(ClimbEnabled&&!Role.StaticStandby)
        {
            if(Space!=null){var contact=Space.Nearest(impactAnchor??AnchorWorld,impact==Edge.None?Edge.All:impact,wrap:HorizontalWrap);if(contact is BoundaryContact c&&c.Distance<2)AttachSpace(c);}
            else Body.TryAttach(impact,Bounds);
        }
        flightAnchorOffset=null;flightLaunchAnchor=null;
        Role.CompletePhysicalMotion(previous==PhysicalState.Flying?"climb/leap/landing":"touch/jump_down",Body.State);
    }
    public void Dispose()
    {
        Body.ProjectMotion=null;Body.FlightContacts=null;ConnectedLimits=null;ConnectedEdges=null;ConnectedMotion=null;Role.FrameEntered-=OnFrame;Role.ChooseAutomaticFunction=null;ChooseForage=null;
    }
}
