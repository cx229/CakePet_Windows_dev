using System.Text.Json;
namespace CakePet.Core;

public sealed record PackEntry(Guid Id,string Name,string Directory);
public sealed record RoleSettings
{
    public int Count {get;init;}=1;
    public double Scale {get;init;}=1.4;
    public double Opacity {get;init;}=1;
    public bool Move {get;init;}=true;
    public bool Climb {get;init;}=true;
    public bool Leap {get;init;}
    public bool Forage {get;init;}=true;
    public bool Feed {get;init;}=true;
    public bool AutoFeed {get;init;}=true;
    public bool Grow {get;init;}=true;
    public bool Throw {get;init;}=true;
    public bool Snap {get;init;}=true;
    public bool ScreenConnect {get;init;}=true;
    public bool FullScreen {get;init;}
    public bool ClickThrough {get;init;}
    public bool PixelHit {get;init;}=true;
    public bool Thinking {get;init;}=true;
    public RandomSettings RandomMode {get;init;}=new();
    public Dictionary<string,SettingLimit> Limits {get;init;}=new();
    public ThinkingStyle ThoughtStyle {get;init;}=new();
    public int ThoughtRestMin {get;init;}=2;
    public int ThoughtRestMax {get;init;}=15;
    public double Gravity {get;init;}=1500;
    public double MaxSpeed {get;init;}=2000;
    public double Bounce {get;init;}=.7;
    public bool BounceEnabled {get;init;}=true;
    public bool GravityEnabled {get;init;}=true;
    public Edge GravityEdges {get;init;}=Edge.Bottom;
    public Edge BounceEdges {get;init;}=Edge.All;
    public Edge SnapEdges {get;init;}=Edge.All;
    public double ThrowCoefficient {get;init;}=1;
    public double SnapSpeed {get;init;}=800;
    public double SnapDistance {get;init;}=100;
    public double LeapProbability {get;init;}=.1;
    public double LeapSpeed {get;init;}=840;
    public double ForageMultiplier {get;init;}=1;
    public int FoodMinSeconds {get;init;}=30;
    public int FoodMaxSeconds {get;init;}=120;
    public int FoodLifetimeSeconds {get;init;}=60;
    public int FoodLimit {get;init;}=3;
    public double FoodScale {get;init;}=1.4;
    public double FoodOpacity {get;init;}=.7;
    public bool FoodAtEdge {get;init;}=true;
    public double FeedGrowth {get;init;}=.03;
    public double MaxScale {get;init;}=3;
    public double OffsetLeft {get;init;}
    public double OffsetRight {get;init;}
    public double OffsetTop {get;init;}
    public double OffsetBottom {get;init;}
    public string MonitorId {get;init;}="";
    private static double N(double v,double min,double max,double fallback)=>double.IsFinite(v)?Math.Clamp(v,min,max):fallback;
    public RoleSettings Normalize()=>this with{Count=Math.Clamp(Count,1,30),Scale=double.IsFinite(Scale)?Math.Clamp(Scale,.5,3):1.4,Opacity=double.IsFinite(Opacity)?Math.Clamp(Opacity,.05,1):1,
        RandomMode=(RandomMode??new()).Normalize(),ThoughtStyle=(ThoughtStyle??new()).Normalize(),ThoughtRestMin=Math.Clamp(ThoughtRestMin,1,300),ThoughtRestMax=Math.Clamp(ThoughtRestMax,Math.Clamp(ThoughtRestMin,1,300),300),
        Gravity=N(Gravity,0,10000,1500),MaxSpeed=N(MaxSpeed,100,10000,2000),Bounce=N(Bounce,0,1,.7),
        GravityEdges=GravityEdges&Edge.All,BounceEdges=BounceEdges&Edge.All,SnapEdges=SnapEdges&Edge.All,
        ThrowCoefficient=N(ThrowCoefficient,0,5,1),SnapSpeed=N(SnapSpeed,0,5000,800),SnapDistance=N(SnapDistance,0,500,100),
        LeapProbability=N(LeapProbability,0,1,.1),LeapSpeed=N(LeapSpeed,100,5000,840),ForageMultiplier=N(ForageMultiplier,.1,5,1),
        FoodMinSeconds=Math.Clamp(FoodMinSeconds,1,3600),FoodMaxSeconds=Math.Clamp(FoodMaxSeconds,Math.Clamp(FoodMinSeconds,1,3600),3600),FoodLifetimeSeconds=Math.Clamp(FoodLifetimeSeconds,0,86400),FoodLimit=Math.Clamp(FoodLimit,1,100),FoodScale=N(FoodScale,.1,5,1.4),FoodOpacity=N(FoodOpacity,.05,1,.7),FeedGrowth=N(FeedGrowth,0,1,.03),MaxScale=N(MaxScale,Math.Clamp(double.IsFinite(Scale)?Scale:1.4,.5,3),10,3),
        OffsetLeft=N(OffsetLeft,-500,500,0),OffsetRight=N(OffsetRight,-500,500,0),OffsetTop=N(OffsetTop,-500,500,0),OffsetBottom=N(OffsetBottom,-500,500,0),MonitorId=MonitorId??""};
    public Bounds ApplyOffsets(Bounds area)
    {
        var left=area.Left+OffsetLeft;var top=area.Top+OffsetTop;
        return new Bounds(left,top,Math.Max(left+1,area.Right-OffsetRight),Math.Max(top+1,area.Bottom-OffsetBottom));
    }
    public RoleSettings Effective(RolePackage pack)=>Normalize() with{
        Move=Move||!pack.Supports("standby/static_pool"),Throw=Throw&&pack.Supports("touch/roll"),Snap=Snap&&pack.Supports("snap/pool"),Leap=Leap&&Climb&&pack.Supports("climb/leap/leap"),Forage=Forage&&Climb&&pack.Supports(PetEngine.ForagePath),Feed=Feed&&pack.Supports("feed/food"),AutoFeed=AutoFeed&&Feed&&pack.Supports("feed/food")};
}
public sealed record RoleEntry(Guid Id,Guid PackId,string Name,RoleSettings Settings);
public enum ThinkingLocation { PrimaryBottom, TrayLeft, TaskbarLeft }
public sealed class RoleCatalog
{
    public List<PackEntry> Packs {get;init;}=new();
    public List<RoleEntry> Roles {get;init;}=new();
    public ThinkingDisplayPolicy ThinkingPolicy {get;set;}=ThinkingDisplayPolicy.Latest;
    public bool SingleThinking {set=>ThinkingPolicy=value?ThinkingDisplayPolicy.Latest:ThinkingDisplayPolicy.Overlap;}
    public TypingSettings Typing {get;set;}=new();
    public string TypingDay {get;set;}="";
    public long TypingToday {get;set;}
    public ThinkingLocation ThinkingLocation {get;set;}=ThinkingLocation.PrimaryBottom;
    public bool AutoSummon {get;set;}=true;
    public RoleSettings Defaults {get;set;}=new();
    public static RoleCatalog Load(string file)
    {
        if(!File.Exists(file))return new();
        var data=JsonSerializer.Deserialize<RoleCatalog>(File.ReadAllText(file))??throw new InvalidDataException("Invalid catalog.");
        if(data.Packs==null||data.Roles==null||data.Packs.Select(x=>x.Id).Distinct().Count()!=data.Packs.Count||data.Roles.Select(x=>x.Id).Distinct().Count()!=data.Roles.Count||data.Roles.Any(x=>x.Settings==null||!data.Packs.Any(p=>p.Id==x.PackId)))throw new InvalidDataException("Invalid role references.");
        data.Defaults=SettingLimits.Clamp((data.Defaults??new()).Normalize());for(int i=0;i<data.Roles.Count;i++)data.Roles[i]=data.Roles[i] with{Settings=SettingLimits.Clamp(data.Roles[i].Settings.Normalize())};
        return data;
    }
    public void Save(string file)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(file))!);
        var temp=file+"."+Guid.NewGuid().ToString("N")+".tmp";
        try{File.WriteAllText(temp,JsonSerializer.Serialize(this,new JsonSerializerOptions{WriteIndented=true}));File.Move(temp,file,true);}
        finally{if(File.Exists(temp))File.Delete(temp);}
    }
    public RoleEntry AddRole(Guid packId,string name)
    {
        if(!Packs.Any(p=>p.Id==packId))throw new ArgumentException("Unknown pack.");
        if(string.IsNullOrWhiteSpace(name))throw new ArgumentException("Empty name.");
        var role=new RoleEntry(Guid.NewGuid(),packId,name.Trim(),SettingLimits.Clamp(Defaults.Normalize()));Roles.Add(role);return role;
    }
    public void Update(RoleEntry role)
    {
        var index=Roles.FindIndex(x=>x.Id==role.Id);
        if(index<0||!Packs.Any(x=>x.Id==role.PackId)||string.IsNullOrWhiteSpace(role.Name))throw new ArgumentException("Invalid role.");
        Roles[index]=role with{Name=role.Name.Trim(),Settings=SettingLimits.Clamp(role.Settings.Normalize())};
    }
}
