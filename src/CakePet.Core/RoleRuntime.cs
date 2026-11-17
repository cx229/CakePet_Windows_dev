namespace CakePet.Core;

public enum PhysicalState { Free, Dragging, Throwing, Climbing, Flying }
public sealed class RoleRuntime
{
    private readonly Random random;
    public RolePackage Package { get; }
    public AnimationCursor Animation { get; private set; } = null!;
    public string FunctionPath { get; private set; } = "";
    public string? StateFunction { get; private set; }
    public PhysicalState Physics { get; private set; }
    public bool Snapped { get; private set; }
    public bool StaticStandby { get; private set; }
    public event Action<Frame>? FrameEntered;
    public event Action? ContextChanged;
    public RoleRuntime(RolePackage package,Random? random=null)
    { Package=package;this.random=random??new Random();Resume(); }
    private bool Select(string path)
    {
        var candidates=Package.Candidates(path);
        if(candidates.Count==0)return false;
        FunctionPath=path;
        Animation=new AnimationCursor(Package,candidates[random.Next(candidates.Count)],random);
        Animation.FrameEntered+=f=>FrameEntered?.Invoke(f);
        Animation.ModeEntered+=_=>ContextChanged?.Invoke();
        ContextChanged?.Invoke();FrameEntered?.Invoke(Animation.Current);
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
    { StaticStandby=enabled&&Package.Candidates("standby/static_pool").Count>0;if(Physics==PhysicalState.Free&&StateFunction==null)Resume(); }
    public void SetEnvironment(bool snapped,PhysicalState state,string? stateFunction=null)
    {
        Snapped=snapped;Physics=state;StateFunction=stateFunction;Resume();
    }
    public bool EnterSustained(string path)
    {
        if(Package.Candidates(path).Count==0)return false;
        StateFunction=path;return Select(path);
    }
    public void EndSustained(){StateFunction=null;Resume();}
    public bool Trigger(string path)
    {
        // Physical transitions have a separate unconditional entry point.
        if(Physics!=PhysicalState.Free)return false;
        if(path.StartsWith("snap/",StringComparison.Ordinal)&&!Snapped)return false;
        if(!path.StartsWith("snap/",StringComparison.Ordinal)&&Snapped&&!path.StartsWith("menu/",StringComparison.Ordinal))return false;
        return Select(path);
    }
    public bool PhysicalEvent(string path)=>Select(path);
    public Thinking CurrentThinking=>Package.ResolveThinking(Animation.Mode.Id,FunctionPath);
    public void Advance(long milliseconds)
    {
        while(milliseconds>0)
        {
            milliseconds=Animation.Advance(milliseconds);
            if(!Animation.Ended)return;
            Resume();
        }
    }
}
