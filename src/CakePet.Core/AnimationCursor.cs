namespace CakePet.Core;

// Pure playback cursor: no role state, UI, or physics dependencies.
public sealed class AnimationCursor
{
    private readonly RolePackage package;
    private readonly Random random;
    private long frameElapsed, modeElapsed, target;
    private int delay;
    public Mode Mode { get; private set; }
    public int FrameIndex { get; private set; }
    public Frame Current => Mode.Frames[FrameIndex];
    public bool Ended { get; private set; }
    public event Action<Frame>? FrameEntered;
    public event Action<string>? ModeEntered;
    public AnimationCursor(RolePackage package,string id,Random random)
    {
        this.package=package; this.random=random; Mode=package.Modes[id]; Reset();
    }
    private void Reset()
    {
        FrameIndex=0;frameElapsed=modeElapsed=0;Ended=false;
        delay=Current.Delay.Sample(random);target=Mode.Duration?.Sample(random)??long.MaxValue;
    }
    private void NextMode()
    {
        if(Mode.Loop!=LoopKind.Infinite&&Mode.Next is string next&&package.Modes.TryGetValue(next,out var m))
        {
            Mode=m;Reset();ModeEntered?.Invoke(m.Id);FrameEntered?.Invoke(Current);
        }
        else Ended=true;
    }
    // Return unused time so the role controller can select the next function pool.
    public long Advance(long milliseconds)
    {
        if(milliseconds<0) throw new ArgumentOutOfRangeException(nameof(milliseconds));
        while(milliseconds>0&&!Ended)
        {
            long span=Math.Min(milliseconds,delay-frameElapsed);
            if(Mode.Loop==LoopKind.Timed&&!Mode.FinishCycle) span=Math.Min(span,Math.Max(0,target-modeElapsed));
            milliseconds-=span;frameElapsed+=span;modeElapsed+=span;
            if(Mode.Loop==LoopKind.Timed&&!Mode.FinishCycle&&modeElapsed>=target){NextMode();continue;}
            if(frameElapsed<delay) continue;
            if(FrameIndex+1<Mode.Frames.Count) FrameIndex++;
            else if(Mode.Loop==LoopKind.Infinite||(Mode.Loop==LoopKind.Timed&&modeElapsed<target)) FrameIndex=0;
            else {NextMode();continue;}
            frameElapsed=0;delay=Current.Delay.Sample(random);FrameEntered?.Invoke(Current);
        }
        return milliseconds;
    }
}
