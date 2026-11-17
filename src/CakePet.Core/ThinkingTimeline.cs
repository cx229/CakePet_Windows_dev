namespace CakePet.Core;

// A single role timeline. Suppression only affects presentation, never its phase.
public sealed class ThinkingTimeline:IThinkingSource
{
    private readonly IReadOnlyDictionary<string,string> texts;
    private readonly Random random;
    private Thinking config=Thinking.Empty;
    private long elapsed,target;
    private bool first=true;
    public Interval RestDuration { get; set; }
    public string? CurrentId { get; private set; }
    public bool Suppressed { get; set; }
    public bool Enabled {get;set;}=true;
    public string? CurrentText=>Enabled&&CurrentId!=null?texts.GetValueOrDefault(CurrentId):null;
    public string? VisibleText => Suppressed?null:CurrentText;
    public event Action? NewText;
    public ThinkingTimeline(IReadOnlyDictionary<string,string> texts,Random? random=null,Interval? rest=null)
    {this.texts=texts;this.random=random??new Random();RestDuration=rest??new Interval(2000,15000);}
    private string[] Candidates()=>config.Pool.Keys.Where(x=>texts.TryGetValue(x,out var t)&&!string.IsNullOrWhiteSpace(t)).ToArray();
    private void Rest()
    {
        CurrentId=null;elapsed=0;target=Math.Max(1,RestDuration.Sample(random));
    }
    private void Show(string id)
    {
        CurrentId=id;elapsed=0;target=config.Pool[id].Sample(random);Suppressed=false;NewText?.Invoke();
    }
    private void Choose()
    {
        var options=Candidates();
        if(options.Length==0)Rest();else Show(options[random.Next(options.Length)]);
    }
    public void Apply(Thinking next)
    {
        config=next;var initial=first;first=false;
        if(config.Policy=="off")
        {if(CurrentId!=null)Rest();return;}
        if(config.Policy=="hold")
        {
            var options=Candidates();
            if(options.Length==0){config=config with {Policy="off"};if(CurrentId!=null)Rest();return;}
            var selected=options[random.Next(options.Length)];if(selected!=CurrentId)Show(selected);return;
        }
        if(CurrentId is string id)
        {
            if(!config.Pool.TryGetValue(id,out var interval)||!texts.ContainsKey(id))Rest();
            else if(target<interval.Min||target>interval.Max)target=interval.Sample(random);
        }
        else if(initial&&Candidates().Length>0)Choose();
        Advance(0);
    }
    public void Advance(long ms)
    {
        if(ms<0)throw new ArgumentOutOfRangeException(nameof(ms));
        if(config.Policy=="off"||(config.Policy=="hold"&&CurrentId!=null))return;
        while(true)
        {
            var remaining=Math.Max(0,target-elapsed);
            if(ms<remaining){elapsed+=ms;return;}
            ms-=remaining;
            if(CurrentId==null)Choose();else Rest();
            if(ms==0&&target>0)return;
        }
    }
}

public sealed class ThinkingCoordinator
{
    private readonly Dictionary<IThinkingSource,(Action Callback,Guid Group)> sources=new();
    private readonly Dictionary<IThinkingSource,long> order=new();
    private long sequence;
    private bool single;
    public bool SingleAcrossRoles
    {
        get=>single;
        set
        {
            single=value;
            if(!value){foreach(var source in sources.Keys)source.Suppressed=false;return;}
            var latest=sources.Keys.Where(x=>x.CurrentText!=null&&!x.Suppressed).OrderByDescending(x=>order.GetValueOrDefault(x)).FirstOrDefault();
            if(latest!=null)foreach(var source in sources.Keys)source.Suppressed=sources[source].Group!=sources[latest].Group;
        }
    }
    public long Order(IThinkingSource source)=>order.GetValueOrDefault(source);
    public void Add(IThinkingSource source,Guid group=default)
    {
        if(sources.ContainsKey(source))return;
        Action callback=()=>OnNewText(source);
        sources.Add(source,(callback,group==Guid.Empty?Guid.NewGuid():group));source.NewText+=callback;
        if(source.CurrentText!=null)OnNewText(source);
    }
    public void Remove(IThinkingSource source)
    {
        if(sources.Remove(source,out var entry))source.NewText-=entry.Callback;
        order.Remove(source);source.Suppressed=false;
    }
    private void OnNewText(IThinkingSource source)
    {
        if(source.CurrentText==null)return;
        order[source]=++sequence;
        if(single)foreach(var other in sources.Keys)if(sources[other].Group!=sources[source].Group)other.Suppressed=true;
    }
}
