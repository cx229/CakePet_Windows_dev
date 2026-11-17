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
    // Called only at a natural function-chain boundary, never on reset or state entry.
    public Func<bool>? ChooseAutomaticFunction { get; set; }
    public RoleRuntime(RolePackage package,Random? random=null)
    {
        Package=package;this.random=random??new Random();
        Thoughts=new ThinkingTimeline(package.Texts,new Random(this.random.Next()));
        Resume();
    }
    private void PublishContext(){Thoughts.Apply(CurrentThinking);ContextChanged?.Invoke();}
    private bool Select(string path,string? exclude=null)
    {
        var candidates=Package.Candidates(path);
        if(candidates.Count==0)return false;
        var alternatives=exclude==null?candidates:candidates.Where(x=>x!=exclude).ToArray();
        if(alternatives.Count==0)alternatives=candidates;
        FunctionPath=path;
        Animation=new AnimationCursor(Package,alternatives[random.Next(alternatives.Count)],random);
        Animation.FrameEntered+=f=>FrameEntered?.Invoke(f);
        Animation.ModeEntered+=_=>PublishContext();
        Animation.TimeAdvanced+=Thoughts.Advance;
        PublishContext();FrameEntered?.Invoke(Animation.Current);
        return true;
    }
    private void Resume(bool natural=false)
    {
        if(StateFunction is string state&&Select(state))return;
        if(Snapped&&Select("snap/pool"))return;
        if(natural&&ChooseAutomaticFunction?.Invoke()==true)return;
        var exclude=natural?Animation.Mode.Id:null;
        if(StaticStandby&&Select("standby/static_pool",exclude))return;
        if(!Select("standby/pool",exclude))throw new InvalidOperationException("No valid standby.");
    }
    public void SetStaticStandby(bool enabled)
    {
        enabled=enabled&&Package.Candidates("standby/static_pool").Count>0;
        if(StaticStandby==enabled)return;
        StaticStandby=enabled;
        if(Physics==PhysicalState.Free&&!Snapped&&StateFunction==null)Resume();
    }
    public void SetClimbing(bool active)
    {
        if(!Snapped&&Physics is PhysicalState.Free or PhysicalState.Climbing)
            Physics=active?PhysicalState.Climbing:PhysicalState.Free;
    }
    public void ResetToStandby(){Snapped=false;Physics=PhysicalState.Free;StateFunction=null;Resume();}
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
    public void CompletePhysicalMotion(string landing="touch/jump_down",PhysicalState nextState=PhysicalState.Free)
    {
        if(nextState is not (PhysicalState.Free or PhysicalState.Climbing))throw new ArgumentException("Invalid landing state.");
        Physics=nextState;Snapped=false;StateFunction=null;
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
            Resume(natural:true);
        }
    }
}
