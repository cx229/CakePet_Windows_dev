namespace CakePet.Core;

public enum PhysicalState { Free, Dragging, Throwing, Climbing, Flying }
public sealed class RoleRuntime
{
    private readonly Random random;
    public RolePackage Package { get; }
    public ThinkingTimeline Thoughts { get; }
    public AnimationCursor Animation { get; private set; } = null!;
    public string FunctionPath { get; private set; } = "";
    public string? StateFunction { get; private set; }
    public PhysicalState Physics { get; private set; }
    public bool Snapped { get; private set; }
    public bool StaticStandby { get; private set; }
    public event Action<Frame>? FrameEntered;
    public event Action? ContextChanged;
    public RoleRuntime(RolePackage package,Random? random=null)
    {
        Package=package;this.random=random??new Random();
        Thoughts=new ThinkingTimeline(package.Texts,new Random(this.random.Next()));
        Resume();
    }
    private void PublishContext(){Thoughts.Apply(CurrentThinking);ContextChanged?.Invoke();}
    private bool Select(string path)
    {
        var candidates=Package.Candidates(path);
        if(candidates.Count==0)return false;
        FunctionPath=path;
        Animation=new AnimationCursor(Package,candidates[random.Next(candidates.Count)],random);
        Animation.FrameEntered+=f=>FrameEntered?.Invoke(f);
        Animation.ModeEntered+=_=>PublishContext();
        Animation.TimeAdvanced+=Thoughts.Advance;
        PublishContext();FrameEntered?.Invoke(Animation.Current);
        return true;
    }
    private void Resume()
    {
        if(StateFunction is string state&&Select(state))return;
        if(Snapped&&Select("snap/pool"))return;
        if(StaticStandby&&Select("standby/static_pool"))return;
        if(!Select("standby/pool"))throw new InvalidOperationException("No valid standby.");
    }
    public void SetStaticStandby(bool enabled)
    {
        enabled=enabled&&Package.Candidates("standby/static_pool").Count>0;
        if(StaticStandby==enabled)return;
        StaticStandby=enabled;
        if(Physics==PhysicalState.Free&&!Snapped&&StateFunction==null)Resume();
    }
    public void SetEnvironment(bool snapped,PhysicalState state,string? stateFunction=null)
    {
        if(snapped&&state!=PhysicalState.Free)throw new ArgumentException("Snapped environment cannot be dragged or in flight.");
        var enteringSnap=snapped&&!Snapped;
        stateFunction??=state switch {
            PhysicalState.Dragging when !StaticStandby=>"touch/lift_up/slow",
            PhysicalState.Throwing when !StaticStandby=>"touch/roll",
            PhysicalState.Flying=>"climb/leap/leap",
            _=>snapped?"snap/pool":null};
        if(Snapped==snapped&&Physics==state&&StateFunction==stateFunction)return;
        Snapped=snapped;Physics=state;StateFunction=stateFunction;
        if(enteringSnap&&Select("snap/snap_head"))return;
        Resume();
    }
    public void UpdateDragCondition(double vx,double width)
    {
        if(Physics!=PhysicalState.Dragging||StaticStandby)return;
        var condition=vx<=-width*3.5?"left_fast":vx<=-width*1.5?"left_medium":vx>=width*3.5?"right_fast":vx>=width*1.5?"right_medium":"slow";
        var path="touch/lift_up/"+condition;
        if(StateFunction==path)return;
        StateFunction=path;Resume();
    }
    private bool CanTrigger(string path)
    {
        if(Physics!=PhysicalState.Free&&Physics!=PhysicalState.Climbing)return false;
        if(path.StartsWith("snap/",StringComparison.Ordinal)&&!Snapped)return false;
        if(!path.StartsWith("snap/",StringComparison.Ordinal)&&Snapped&&!path.StartsWith("menu/",StringComparison.Ordinal))return false;
        return true;
    }
    public bool EnterSustained(string path)
    {
        if(!CanTrigger(path)||Package.Candidates(path).Count==0)return false;
        if(StateFunction==path)return true;
        StateFunction=path;return Select(path);
    }
    public void EndSustained(){StateFunction=Snapped?"snap/pool":null;Resume();}
    public bool Trigger(string path)=>CanTrigger(path)&&Select(path);
    public bool PhysicalEvent(string path)=>Select(path);
    public void CompletePhysicalMotion(string landing="touch/jump_down")
    {
        Physics=PhysicalState.Free;Snapped=false;StateFunction=null;
        if(!Select(landing))Resume();
    }
    public Thinking CurrentThinking=>Package.ResolveThinking(Animation.Mode.Id,FunctionPath);
    public void Advance(long milliseconds)
    {
        if(milliseconds<0)throw new ArgumentOutOfRangeException(nameof(milliseconds));
        while(milliseconds>0)
        {
            milliseconds=Animation.Advance(milliseconds);
            if(!Animation.Ended)return;
            Resume();
        }
    }
}
