using System.Text.Json.Nodes;
namespace CakePet.Core;
public sealed record SettingLimit(double Min,double Max);
public sealed record NumericSetting(string Key,string Label,string Group,double Min,double Max,double Step,string Unit);
public static class SettingLimits
{
    public static IReadOnlyList<NumericSetting> Specs {get;}=new NumericSetting[]{
        new("Count","召唤数量","显示与交互",1,30,1,"个"),new("Scale","大小","显示与交互",.5,3,.05,"倍"),new("Opacity","透明度","显示与交互",.05,1,.01,""),
        new("MaxSpeed","最大速度","移动",100,10000,50,"px/s"),new("Gravity","重力强度","移动",0,10000,50,"px/s²"),new("Bounce","反弹系数","移动",0,1,.01,""),new("ThrowCoefficient","抛掷系数","移动",0,5,.05,"倍"),new("ForageMultiplier","觅食速度加成","移动",.1,5,.05,"倍"),new("LeapProbability","飞跃概率","移动",0,1,.01,""),new("LeapSpeed","飞跃速度","移动",100,5000,25,"px/s"),
        new("SnapDistance","吸附距离","吸附边缘",0,500,5,"px"),new("SnapSpeed","吸附速度","吸附边缘",0,5000,25,"px/s"),
        new("FoodScale","食物大小","投食",.1,5,.05,"倍"),new("FoodOpacity","食物透明度","投食",.05,1,.01,""),new("FoodLimit","最大数量","投食",1,100,1,"个"),new("FoodLifetimeSeconds","过期时间","投食",0,86400,1,"秒"),new("FeedGrowth","变大系数","投食",0,1,.01,""),new("MaxScale","最大大小","投食",.5,10,.05,"倍"),
        new("ThoughtStyle.Size","字号","碎碎念",8,48,1,"DIP"),new("ThoughtStyle.Alpha","文字透明度","碎碎念",0,1,.01,""),new("ThoughtStyle.Offset","位置偏移","碎碎念",-200,300,1,"px"),new("ThoughtStyle.BackgroundAlpha","背景透明度","碎碎念",0,1,.01,""),
        new("OffsetLeft","左侧偏移","屏幕适应",-500,500,1,"px"),new("OffsetRight","右侧偏移","屏幕适应",-500,500,1,"px"),new("OffsetTop","顶部偏移","屏幕适应",-500,500,1,"px"),new("OffsetBottom","底部偏移","屏幕适应",-500,500,1,"px")
    };
    public static SettingLimit Get(RoleSettings settings,NumericSetting spec)
    {
        if(settings.Limits==null||!settings.Limits.TryGetValue(spec.Key,out var limit)||limit==null||!double.IsFinite(limit.Min)||!double.IsFinite(limit.Max))return new(spec.Min,spec.Max);
        var min=Math.Clamp(limit.Min,spec.Min,spec.Max-spec.Step);var max=Math.Clamp(limit.Max,min+spec.Step,spec.Max);return new(min,max);
    }
    private static JsonObject Parent(JsonObject root,string key)=>key.StartsWith("ThoughtStyle.")?root["ThoughtStyle"]!.AsObject():root;
    private static string Leaf(string key)=>key.Split('.').Last();
    public static RoleSettings Clamp(RoleSettings settings)
    {
        if(settings.Limits==null||settings.Limits.Count==0)return settings;
        var node=System.Text.Json.JsonSerializer.SerializeToNode(settings,ArchiveIO.Options)!.AsObject();
        foreach(var spec in Specs){if(!settings.Limits.ContainsKey(spec.Key))continue;var limit=Get(settings,spec);var parent=Parent(node,spec.Key);var leaf=Leaf(spec.Key);if(parent[leaf] is not JsonValue v||!double.TryParse(v.ToString(),System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out var n))continue;var value=Math.Clamp(n,limit.Min,limit.Max);parent[leaf]=spec.Key is "Count" or "FoodLimit" or "FoodLifetimeSeconds"?JsonValue.Create((int)Math.Round(value)):JsonValue.Create(value);}
        return node.DeserializeRole();
    }
    private static RoleSettings DeserializeRole(this JsonObject node)=>System.Text.Json.JsonSerializer.Deserialize<RoleSettings>(node.ToJsonString(),ArchiveIO.Options)??throw new InvalidDataException("无效设置");
    public static RoleSettings ApplyItem(RoleSettings target,RoleSettings source,string key)
    {
        if(!Specs.Any(s=>s.Key==key))return target;
        var dest=System.Text.Json.JsonSerializer.SerializeToNode(target,ArchiveIO.Options)!.AsObject();var src=System.Text.Json.JsonSerializer.SerializeToNode(source,ArchiveIO.Options)!.AsObject();
        Parent(dest,key)[Leaf(key)]=Parent(src,key)[Leaf(key)]?.DeepClone();var limits=new Dictionary<string,SettingLimit>(target.Limits??new());limits.Remove(key);if(source.Limits?.TryGetValue(key,out var limit)==true)limits[key]=limit;
        dest["Limits"]=System.Text.Json.JsonSerializer.SerializeToNode(limits,ArchiveIO.Options);return Clamp(dest.DeserializeRole().Normalize());
    }
    public static RoleSettings ApplyGroup(RoleSettings target,RoleSettings source,string group)
    {
        var keys=group switch{
            "显示与交互"=>new[]{"Count","Scale","Opacity","ClickThrough","PixelHit"},
            "移动"=>new[]{"Move","MaxSpeed","ScreenConnect","GravityEnabled","Gravity","GravityEdges","BounceEnabled","Bounce","BounceEdges","Throw","ThrowCoefficient","Climb","Forage","ForageMultiplier","Leap","LeapProbability","LeapSpeed"},
            "吸附边缘"=>new[]{"Snap","SnapDistance","SnapSpeed","SnapEdges"},
            "投食"=>new[]{"Feed","AutoFeed","FoodMinSeconds","FoodMaxSeconds","FoodLimit","FoodLifetimeSeconds","FoodScale","FoodOpacity","FoodAtEdge","Grow","FeedGrowth","MaxScale"},
            "碎碎念"=>new[]{"Thinking","ThoughtStyle","ThoughtRestMin","ThoughtRestMax"},
            "屏幕适应"=>new[]{"MonitorId","FullScreen","OffsetLeft","OffsetRight","OffsetTop","OffsetBottom"},
            "随机模式"=>new[]{"RandomMode"},_=>Array.Empty<string>()};
        var dest=System.Text.Json.JsonSerializer.SerializeToNode(target,ArchiveIO.Options)!.AsObject();var src=System.Text.Json.JsonSerializer.SerializeToNode(source,ArchiveIO.Options)!.AsObject();
        foreach(var key in keys)dest[key]=src[key]?.DeepClone();
        var limits=new Dictionary<string,SettingLimit>(target.Limits??new());foreach(var spec in Specs.Where(x=>x.Group==group)){limits.Remove(spec.Key);if(source.Limits?.TryGetValue(spec.Key,out var value)==true)limits[spec.Key]=value;}
        dest["Limits"]=System.Text.Json.JsonSerializer.SerializeToNode(limits,ArchiveIO.Options);
        return Clamp(dest.DeserializeRole().Normalize());
    }
}
