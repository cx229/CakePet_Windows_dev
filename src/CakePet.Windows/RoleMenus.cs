using CakePet.Core;
namespace CakePet.Windows;

internal static class RoleMenus
{
    internal sealed record Feature(string Name,Func<RoleSettings,bool> Get,Func<RoleSettings,bool,RoleSettings> Set,Func<RolePackage?,bool> Supported);
    internal static readonly Feature[] Switches={
        new("点击穿透",s=>s.ClickThrough,(s,v)=>s with{ClickThrough=v},_=>true),
        new("投食",s=>s.Feed,(s,v)=>s with{Feed=v},p=>p?.Supports("feed/food")==true),
        new("移动",s=>s.Move,(s,v)=>s with{Move=v},p=>p?.Supports("standby/static_pool")==true),
        new("自动攀爬",s=>s.Climb,(s,v)=>s with{Climb=v},p=>p!=null),
        new("飞跃",s=>s.Leap,(s,v)=>s with{Leap=v},p=>p?.Supports("climb/leap/leap")==true)
    };
    internal static IReadOnlyList<NativeMenu.Item> Build(RoleSettings settings,RolePackage? pack,IReadOnlyList<PetWindow> live,PetWindow? instance,
        Action? summon,Action recall,Action openSettings,Action<Func<RoleSettings,RoleSettings>> change,Action clearFood,bool hasFood)
    {
        var items=new List<NativeMenu.Item>();
        IReadOnlyList<PetWindow> targets=instance==null?live:new[]{instance};
        bool active=live.Count>0;
        void Each(Action<PetWindow> action){foreach(var p in targets.ToArray())action(p);}
        NativeMenu.Item TargetAction(string label,Action<PetWindow> action,Func<PetWindow,bool>? allowed=null)
        {
            allowed??=_=>true;
            var enabled=targets.Where(allowed).ToArray();
            if(instance!=null||targets.Count<=1)return new(label,()=>{foreach(var p in enabled)action(p);},enabled.Length>0);
            var children=new List<NativeMenu.Item>{new("全部实例",()=>{foreach(var p in enabled)action(p);},enabled.Length>0),new(null)};
            children.AddRange(targets.Select(p=>new NativeMenu.Item("实例 "+p.InstanceNumber,()=>action(p),allowed(p))));
            return new(label,Enabled:enabled.Length>0,Children:children);
        }
        if(active)
        {
            items.Add(new("召回",recall));
            items.Add(new("归位",()=>Each(p=>p.ReturnHome())));
            items.Add(new("恢复大小",()=>Each(p=>p.ResetSize())));
            items.Add(new(null));
            items.Add(TargetAction("摸摸头",p=>p.Pat(),p=>p.CanPat));
            items.Add(TargetAction("立即投食",p=>p.FeedNow(),p=>p.CanFeed));
            var animations=pack?.Candidates("menu/pool").Select(mode=>TargetAction(mode.Replace("&","&&"),p=>p.PlayMenuAnimation(mode),p=>p.CanMenuAnimation)).ToArray()??Array.Empty<NativeMenu.Item>();
            items.Add(new("菜单动画",Enabled:animations.Any(a=>a.Enabled),Children:animations));
        }
        else if(summon!=null)items.Add(new("召唤",summon));
        items.Add(new(null));
        foreach(var feature in Switches)
        {
            bool supported=feature.Supported(pack);
            bool on=feature.Name=="移动"&&pack!=null&&!supported||supported&&feature.Get(settings);
            items.Add(new(feature.Name,()=>change(s=>feature.Set(s,!on)),supported,on));
        }
        if(active)
        {
            items.Add(new(null));
            items.Add(TargetAction("定格当前画面",p=>p.Freeze()));
            items.Add(new("清除食物",clearFood,hasFood));
            items.Add(new("清除定格",()=>Each(p=>p.ClearFreezes()),targets.Any(p=>p.HasFreezes)));
        }
        items.Add(new(null));items.Add(new("角色设置",openSettings));
        return items;
    }
}
