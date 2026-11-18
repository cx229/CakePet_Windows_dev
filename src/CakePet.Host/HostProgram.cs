using System.Diagnostics;
using System.IO.Pipes;
using System.Text.Json;
using CakePet.Core;
namespace CakePet.Windows;

internal static class HostProgram
{
    [STAThread]
    public static int Main(string[] args)
    {
        var root=Path.Combine(AppContext.BaseDirectory,"assets","default");
        if(args.Length>=2&&args[0]=="--package")root=Path.GetFullPath(args[1]);
        using var mutex=new Mutex(true,"Local\\CakePet.NativeHost",out bool first);
        if(!first){try{HostConnection.Call(new("open"));}catch(Exception){}return 0;}
        using var loop=new HostLoop();
        using var runtime=new ResidentHost(root,loop);
        using var server=new HostServer(loop,runtime.Handle);
        runtime.Start();return loop.Run();
    }
}
internal sealed class ResidentHost:IDisposable
{
    private readonly string defaultRoot;
    private readonly CatalogStorage storage;
    private RoleCatalog catalog=>storage.Catalog;
    private readonly HostLoop loop;
    private readonly List<PetWindow> pets=new();
    private readonly Dictionary<string,(RolePackage Pack,NativeSpriteCache Images)> packages=new(StringComparer.OrdinalIgnoreCase);
    private readonly FoodWorld world=new();
    private readonly Dictionary<FoodInstance,FoodWindow> foodWindows=new();
    private readonly ThinkingCoordinator thoughts=new();
    private readonly ThinkingPresenter presenter=new();
    private readonly TypingThinking typing=new();
    private readonly TypingInput typingInput;
    private readonly RandomSchedule randomSchedule=new();
    private readonly Stopwatch clock=Stopwatch.StartNew();
    private readonly NativeTray tray;
    private long lastTick,lastRandomCheck,lastTypingSave;
    private bool typingDirty,disposed;
    private Process? settingsProcess;
    private string Today()=>DateTime.Now.ToString("yyyy-MM-dd");
    public ResidentHost(string root,HostLoop loop)
    {
        this.loop=loop;defaultRoot=root;storage=new(root);
        typingInput=new TypingInput(()=>{if(catalog.Typing.Enabled&&catalog.ThinkingPolicy!=ThinkingDisplayPolicy.None){typing.Press(clock.ElapsedMilliseconds,Today(),catalog.Typing);typingDirty=true;}});
        thoughts.Add(typing);thoughts.Policy=catalog.ThinkingPolicy;
        world.Spawned+=OnFoodSpawned;world.Removed+=OnFoodRemoved;
        tray=new NativeTray(Path.Combine(AppContext.BaseDirectory,"assets","CakePet.ico"),()=>OpenSettings(),a=>loop.Post(()=>Safe(a)),new (string?,Action?)[]{
            ("打开 CakePet",()=>OpenSettings()),("召唤全部",()=>{foreach(var r in catalog.Roles.ToArray())Summon(r);}),
            ("全部归位",()=>{foreach(var p in pets)p.Engine.ReturnHome();}),("召回全部",()=>Recall(null)),
            ("恢复全部点击",RestoreInput),("托盘显示设置",()=>Process.Start(new ProcessStartInfo("ms-settings:taskbar"){UseShellExecute=true})),
            ("清除全部定格",()=>{foreach(var p in pets)p.ClearFreezes();}),("内存诊断",MemoryReport),(null,null),("退出",()=>loop.Stop())
        });
        loop.Tick+=Tick;
    }
    private void Safe(Action action){try{action();}catch(Exception e){NativeMessage.Show(e.Message,"CakePet");}}
    public void Start(){ReloadTyping();if(catalog.AutoSummon&&catalog.Roles.FirstOrDefault() is RoleEntry r)Safe(()=>Summon(r));lastTick=clock.ElapsedMilliseconds;}
    private void ReloadTyping(){typing.Restore(catalog.TypingDay,catalog.TypingToday,Today());typingDirty=false;SetTyping();}
    private void SetTyping(){try{typingInput.SetEnabled(catalog.Typing.Enabled&&catalog.ThinkingPolicy!=ThinkingDisplayPolicy.None);}catch(Exception e){NativeMessage.Show(e.Message,"无法启用打字统计");}}
    private void SaveTyping(){if(!typingDirty)return;catalog.TypingDay=typing.Day;catalog.TypingToday=typing.Today;storage.Save();typingDirty=false;lastTypingSave=clock.ElapsedMilliseconds;}
    private void Tick()
    {
        if(disposed)return;
        var now=clock.ElapsedMilliseconds;var dt=Math.Clamp(now-lastTick,0,100);lastTick=now;
        if(now-lastRandomCheck>=1000){lastRandomCheck=now;foreach(var entry in catalog.Roles.Where(r=>pets.Any(p=>p.RoleId==r.Id)).ToArray())if(randomSchedule.Due(entry.Id,entry.Settings.RandomMode,now)){catalog.Update(entry with{Settings=entry.Settings.RandomMode.Apply(entry.Settings,Random.Shared)});ApplyRunning(catalog.Roles.Single(r=>r.Id==entry.Id));storage.Save();}}
        world.Advance(dt);var live=pets.ToArray();foreach(var pet in live)pet.Step(dt);world.UpdateInteractions();foreach(var pet in live)pet.Render();foreach(var food in foodWindows.Values.ToArray())food.Render();
        if(catalog.Typing.Enabled&&catalog.ThinkingPolicy!=ThinkingDisplayPolicy.None){if(typing.RollDay(Today()))typingDirty=true;typing.Advance(now);if(typingDirty&&now-lastTypingSave>=5000)SaveTyping();}
        var messages=pets.Where(p=>!string.IsNullOrWhiteSpace(p.VisibleThought)).Select(p=>new ThoughtMessage(p.InstanceId+":"+thoughts.Order(p.Engine.Role.Thoughts),p.RoleName,p.VisibleThought!,p.ThoughtStyle,thoughts.Order(p.Engine.Role.Thoughts))).ToList();
        if(!typing.Suppressed&&typing.CurrentText is string typed)messages.Add(new ThoughtMessage("typing","打字统计",typed,catalog.Typing.Style,thoughts.Order(typing)));
        presenter.Render(messages,catalog.ThinkingLocation,catalog.ThinkingPolicy);
    }
    private (RolePackage Pack,NativeSpriteCache Images) GetPack(Guid id)
    {
        var root=storage.Resolve(catalog.Packs.Single(p=>p.Id==id));
        if(!packages.TryGetValue(root,out var pair)){var pack=RolePackage.Load(root,GdiImageCache.IsReadable);pair=(pack,new NativeSpriteCache(pack));packages[root]=pair;}return pair;
    }
    private void ReleaseUnused(){foreach(var key in packages.Keys.ToArray())if(!pets.Any(p=>ReferenceEquals(p.NativeImages,packages[key].Images))){packages[key].Images.Dispose();packages.Remove(key);}}
    private void OnFoodSpawned(FoodInstance food)
    {
        var owner=pets.FirstOrDefault(p=>p.Engine==food.Owner);if(owner==null){world.Remove(food);return;}
        try{var window=new FoodWindow(world,food,owner.NativeImages,owner.NativeImages.GetFoodVisual(food.StartMode));foodWindows.Add(food,window);window.Show();}
        catch(Exception e){world.Remove(food);NativeMessage.Show(e.Message,"无法显示食物");}
    }
    private void OnFoodRemoved(FoodInstance food){if(foodWindows.Remove(food,out var window))window.CloseFromWorld();}
    private void ApplyRunning(RoleEntry entry){var group=pets.Where(p=>p.RoleId==entry.Id).ToArray();for(int i=0;i<group.Length;i++){group[i].RoleName=entry.Name;group[i].ApplySettings(entry.Settings,i==0,catalog.ThinkingPolicy!=ThinkingDisplayPolicy.None);}}
    private void ChangeRole(Guid id,Func<RoleSettings,RoleSettings> change){var r=catalog.Roles.Single(x=>x.Id==id);catalog.Update(r with{Settings=change(r.Settings)});storage.Save();ApplyRunning(catalog.Roles.Single(x=>x.Id==id));}
    private void RestoreInput(){foreach(var r in catalog.Roles.ToArray())ChangeRole(r.Id,s=>s with{ClickThrough=false});}
    private void Recall(Guid? id){foreach(var pet in pets.Where(p=>id==null||p.RoleId==id).ToArray())pet.Close();ReleaseUnused();}
    private void Summon(RoleEntry entry)
    {
        var pair=GetPack(entry.PackId);var settings=entry.Settings.Normalize();
        var existing=pets.Where(p=>p.RoleId==entry.Id).ToList();var created=new List<PetWindow>();
        while(existing.Count>settings.Count){var last=existing[^1];existing.RemoveAt(existing.Count-1);last.Close();}
        while(existing.Count<settings.Count)
        {
            var role=new RoleRuntime(pair.Pack);var pet=new PetWindow(role,pair.Images,pets.Count,world,entry.Id,existing.Count+1){RoleName=entry.Name};
            pet.OpenSettings+=id=>OpenSettings(id);pet.ChangeSettings+=ChangeRole;pet.RecallRole+=id=>Recall(id);
            pet.ClearRoleFood+=id=>{foreach(var p in pets.Where(p=>p.RoleId==id).ToArray())world.Clear(p.Engine);};
            pets.Add(pet);existing.Add(pet);world.Add(pet.Engine,entry.Id);role.Thoughts.Enabled=settings.Thinking&&catalog.ThinkingPolicy!=ThinkingDisplayPolicy.None;thoughts.Add(role.Thoughts,entry.Id);
            pet.Closed+=(_,_)=>{world.Remove(pet.Engine);thoughts.Remove(role.Thoughts);pets.Remove(pet);if(!pets.Any(p=>p.RoleId==entry.Id))randomSchedule.Forget(entry.Id);};
            created.Add(pet);
        }
        for(int i=0;i<existing.Count;i++)existing[i].ApplySettings(settings,i==0,catalog.ThinkingPolicy!=ThinkingDisplayPolicy.None);
        foreach(var pet in created)pet.Show();
    }
    private void OpenSettings(Guid? id=null)
    {
        SaveTyping();
        if(settingsProcess is {HasExited:false}){HostConnection.SignalSettings(id);return;}
        settingsProcess?.Dispose();
        var path=Path.Combine(AppContext.BaseDirectory,"settings","CakePet.exe");
        var info=new ProcessStartInfo(path){UseShellExecute=false};info.ArgumentList.Add("--settings-client");if(id.HasValue)info.ArgumentList.Add(id.Value.ToString());
        settingsProcess=Process.Start(info);
    }
    private void MemoryReport()
    {
        var report=MemoryDiagnostics.Sample("native-resident",new(){["pets"]=pets.Count,["foodWindows"]=foodWindows.Count,["settingsOpen"]=settingsProcess is {HasExited:false},["loadedPackages"]=packages.Count,["imageCacheBytes"]=0L,["foodCacheBytes"]=0L,["nativeCacheBytes"]=packages.Values.Sum(p=>p.Images.Bytes)});
        var directory=Path.Combine(Path.GetDirectoryName(storage.FilePath)!,"diagnostics");Directory.CreateDirectory(directory);
        var path=Path.Combine(directory,"memory-host-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+".json");File.WriteAllText(path,JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));
        NativeMessage.Show(MemoryDiagnostics.Summary(report)+"\n\n诊断文件：\n"+path,"CakePet · 内存诊断");
    }
    // Host is the runtime owner. UI writes the catalog atomically, then asks host to reconcile.
    private void ReloadCatalog(bool reloadAssets)
    {
        SaveTyping();
        var old=catalog.Roles.ToDictionary(r=>r.Id);var oldPacks=catalog.Packs.ToDictionary(p=>p.Id);
        var active=pets.Select(p=>p.RoleId).Distinct().ToArray();
        storage.Reload();
        thoughts.Policy=catalog.ThinkingPolicy;SetTyping();
        if(catalog.ThinkingPolicy==ThinkingDisplayPolicy.None){typing.Stop();presenter.Clear();}
        foreach(var id in active)
        {
            var next=catalog.Roles.FirstOrDefault(r=>r.Id==id);
            if(next==null){Recall(id);continue;}
            bool changed=reloadAssets||!old.TryGetValue(id,out var before)||before.PackId!=next.PackId||!oldPacks.TryGetValue(next.PackId,out var oldPack)||oldPack!=catalog.Packs.Single(p=>p.Id==next.PackId);
            if(changed){Recall(id);Summon(next);}
            else if(pets.Count(p=>p.RoleId==id)!=next.Settings.Count)Summon(next);
            else ApplyRunning(next);
        }
        ReleaseUnused();
    }
    public HostReply Handle(HostRequest req)
    {
        switch(req.Command)
        {
            case "open":OpenSettings(req.RoleId);break;
            case "snapshot":break;
            case "reload":ReloadCatalog(false);break;
            case "reload-assets":ReloadCatalog(true);break;
            case "summon":if(req.RoleId is Guid id)Summon(catalog.Roles.Single(r=>r.Id==id));else foreach(var r in catalog.Roles.ToArray())Summon(r);break;
            case "recall":Recall(req.RoleId);break;
            case "home":foreach(var p in pets.Where(p=>req.RoleId==null||p.RoleId==req.RoleId))p.Engine.ReturnHome();break;
            case "restore-input":RestoreInput();break;
            case "clear-food":world.Clear();break;
            case "clear-freezes":foreach(var p in pets)p.ClearFreezes();break;
            case "scale":pets.FirstOrDefault(p=>p.InstanceId==req.InstanceId)?.SetInstanceScale(req.Scale??.5);break;
            case "shutdown":loop.Post(loop.Stop);break;
            default:throw new InvalidOperationException("Unknown command: "+req.Command);
        }
        return new(true,null,pets.Select(p=>new LiveInstance(p.RoleId,p.InstanceId,p.InstanceNumber,p.CurrentScale)).ToArray(),world.Foods.Count);
    }
    public void Dispose()
    {
        if(disposed)return;disposed=true;loop.Tick-=Tick;SaveTyping();typingInput.Dispose();world.Clear();Recall(null);presenter.Dispose();tray.Dispose();settingsProcess?.Dispose();
    }
}
