namespace CakePet.Core;

// Only aggregate counts and anonymous, temporary monotonic timestamps are retained.
public sealed record TypingSettings
{
    public bool Enabled {get;init;}
    public int Seconds {get;init;}=5;
    public bool ShowSpeed {get;init;}=true;
    public bool ShowToday {get;init;}=true;
    public ThinkingStyle Style {get;init;}=new();
    public TypingSettings Normalize()=>this with{Seconds=Math.Clamp(Seconds,1,30),Style=(Style??new()).Normalize()};
}
public sealed record ThinkingStyle
{
    public double Size {get;init;}=14;
    public double Alpha {get;init;}=1;
    public double Offset {get;init;}
    public uint Color {get;init;}=0xFFFFFFFF;
    public bool Background {get;init;}=true;
    public uint BackgroundColor {get;init;}=0xFF232323;
    public double BackgroundAlpha {get;init;}=.75;
    public bool FlashIn {get;init;}=true;
    public bool FlashOut {get;init;}=true;
    private static double N(double x,double min,double max,double fallback)=>double.IsFinite(x)?Math.Clamp(x,min,max):fallback;
    public ThinkingStyle Normalize()=>this with{Size=N(Size,8,48,14),Alpha=N(Alpha,0,1,1),Offset=N(Offset,-200,300,0),Color=Color|0xFF000000,BackgroundColor=BackgroundColor|0xFF000000,BackgroundAlpha=N(BackgroundAlpha,0,1,.75)};
}
public interface IThinkingSource
{
    string? CurrentText {get;}
    bool Suppressed {get;set;}
    event Action? NewText;
}
public sealed class TypingThinking:IThinkingSource
{
    private readonly Queue<long> times=new();
    private long until;
    private string? text;
    public string Day {get;private set;}="";
    public long Today {get;private set;}
    public bool Suppressed {get;set;}
    public string? CurrentText=>text;
    public event Action? NewText;
    public void Restore(string day,long total,string today){Day=day==today?day:today;Today=day==today?Math.Max(0,total):0;Stop();}
    public bool RollDay(string today){if(Day==today)return false;Day=today;Today=0;return true;}
    private void Trim(long now){while(times.Count>0&&now-times.Peek()>=60000)times.Dequeue();}
    public void Press(long now,string today,TypingSettings settings)
    {
        if(!settings.Enabled)return;RollDay(today);Trim(now);times.Enqueue(now);if(Today<long.MaxValue)Today++;
        var parts=new List<string>();if(settings.ShowSpeed)parts.Add($"打字：{times.Count} 次/分钟");if(settings.ShowToday)parts.Add($"今日：{Today:N0} 次");
        text=parts.Count==0?null:string.Join("　",parts);until=now+settings.Normalize().Seconds*1000L;Suppressed=false;if(text!=null)NewText?.Invoke();
    }
    public void Advance(long now){Trim(now);if(now>=until)text=null;}
    public void Stop(){times.Clear();text=null;until=0;Suppressed=false;}
}
