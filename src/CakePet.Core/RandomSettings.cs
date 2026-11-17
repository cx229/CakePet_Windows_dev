namespace CakePet.Core;
[Flags]
public enum RandomItems
{
    None=0,Scale=1,Opacity=2,GravityEnabled=4,MaxSpeed=8,Gravity=16,Bounce=32,VerticalGravity=64,HorizontalGravity=128,
    BounceTop=256,BounceBottom=512,BounceLeft=1024,BounceRight=2048,Default=255,All=4095
}
public sealed record RandomSettings
{
    public bool Enabled {get;init;}
    public RandomItems Items {get;init;}=RandomItems.Default;
    public int MinMinutes {get;init;}=30;
    public int MaxMinutes {get;init;}=120;
    public RandomSettings Normalize()=>this with{Items=Items&RandomItems.All,MinMinutes=Math.Clamp(MinMinutes,1,144),MaxMinutes=Math.Clamp(MaxMinutes,Math.Clamp(MinMinutes,1,144),144)};
    public long NextDelay(Random random)=>random.NextInt64(MinMinutes,(long)MaxMinutes+1)*60000L;
    public RoleSettings Apply(RoleSettings settings,Random random)
    {
        bool On(RandomItems item)=>Items.HasFlag(item);
        double Between(double lo,double hi)=>lo+random.NextDouble()*(hi-lo);
        var next=settings;
        if(On(RandomItems.Scale))next=next with{Scale=Between(.6,2)};
        if(On(RandomItems.Opacity))next=next with{Opacity=Between(.3,1)};
        if(On(RandomItems.GravityEnabled))next=next with{GravityEnabled=random.NextDouble()<.8};
        // Desktop velocity and acceleration have their own scale.
        if(On(RandomItems.MaxSpeed))next=next with{MaxSpeed=Between(500,6000)};
        if(On(RandomItems.Gravity))next=next with{Gravity=Between(250,3500)};
        if(On(RandomItems.Bounce))next=next with{Bounce=Between(.45,.95)};
        var vertical=On(RandomItems.VerticalGravity);var horizontal=On(RandomItems.HorizontalGravity);var edges=next.GravityEdges;
        if(vertical)edges=(edges&~(Edge.Top|Edge.Bottom))|(random.Next(3) switch{0=>Edge.Top,1=>Edge.Bottom,_=>Edge.None});
        if(horizontal)edges=(edges&~(Edge.Left|Edge.Right))|(random.Next(3) switch{0=>Edge.Left,1=>Edge.Right,_=>Edge.None});
        if(edges==Edge.None&&(vertical||horizontal)){if(vertical&&(!horizontal||random.Next(2)==0))edges=random.Next(2)==0?Edge.Top:Edge.Bottom;else edges=random.Next(2)==0?Edge.Left:Edge.Right;}
        var bounce=next.BounceEdges;
        foreach(var (flag,edge) in new[]{(RandomItems.BounceTop,Edge.Top),(RandomItems.BounceBottom,Edge.Bottom),(RandomItems.BounceLeft,Edge.Left),(RandomItems.BounceRight,Edge.Right)})if(On(flag))bounce=random.Next(2)==0?bounce&~edge:bounce|edge;
        return (next with{GravityEdges=edges,BounceEdges=bounce}).Normalize();
    }
}
public sealed class RandomSchedule
{
    private readonly Random random;
    private readonly Dictionary<Guid,(RandomSettings Settings,long Due)> entries=new();
    public RandomSchedule(Random? random=null)=>this.random=random??Random.Shared;
    public void Forget(Guid id)=>entries.Remove(id);
    public bool Due(Guid id,RandomSettings config,long now)
    {
        config=config.Normalize();
        if(!config.Enabled){Forget(id);return false;}
        if(!entries.TryGetValue(id,out var state)||state.Settings!=config){entries[id]=(config,now+config.NextDelay(random));return false;}
        if(now<state.Due)return false;
        entries[id]=(config,now+config.NextDelay(random));return true;
    }
}
