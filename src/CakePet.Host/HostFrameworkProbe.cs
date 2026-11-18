using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using CakePet.Core;
namespace CakePet.Windows;

// Exercises the production resident host, not a simplified sprite test.
// Data and IPC are isolated from the user's normal installation.
internal static class HostFrameworkProbe
{
    private static int ImportProbe(string root,string output)
    {
        string dataRoot=Path.Combine(Path.GetTempPath(),"cakepet-import-probe-"+Guid.NewGuid().ToString("N"));
        string pipe="CakePet.ImportProbe."+Environment.ProcessId;
        try
        {
            using var loop=new HostLoop();
            using var resident=new ResidentHost(Path.GetFullPath(root),loop,dataRoot,true);
            resident.ConfigureProbe(1,3);
            using var server=new HostServer(loop,resident.Handle,pipe);
            var work=Task.Run(()=>{
                try
                {
                    var before=HostConnection.Call(new("snapshot"),pipe);
                    var initial=RoleCatalog.Parse(before.Catalog!);
                    var id=Guid.NewGuid();var entry=new PackEntry(id,"Import probe",id.ToString("N"));
                    var dest=Path.Combine(dataRoot,"packs",entry.Directory);
                    Directory.CreateDirectory(dest);
                    foreach(var file in Directory.EnumerateFiles(root,"*",SearchOption.AllDirectories))
                    {
                        var target=Path.Combine(dest,Path.GetRelativePath(root,file));Directory.CreateDirectory(Path.GetDirectoryName(target)!);File.Copy(file,target);
                    }
                    for(int i=0;i<2;i++)
                    {
                        var after=HostConnection.Call(new("register-pack",Pack:entry),pipe);
                        var catalog=RoleCatalog.Parse(after.Catalog!);
                        if(catalog.Packs.Count!=initial.Packs.Count+1||catalog.Packs.Count(p=>p.Id==id)!=1)throw new InvalidDataException("Registration must be idempotent");
                        if(!after.Instances.Select(p=>p.InstanceId).SequenceEqual(before.Instances.Select(p=>p.InstanceId)))throw new InvalidDataException("Import must preserve running instances");
                    }
                    if(!RoleCatalog.Load(Path.Combine(dataRoot,"catalog.json")).Packs.Contains(entry))throw new InvalidDataException("Registration not persisted");
                    var raw=initial.Roles.Single().Settings;
                    for(int i=0;i<6;i++)
                    {
                        bool allowed=i%2!=0;
                        var reads=Enumerable.Range(0,2).Select(_=>HostConnection.CallAsync(new("snapshot"),pipe)).ToArray();
                        var changed=HostConnection.Call(new("set-global-limit",Feature:"投食",Allowed:allowed),pipe);
                        Task.WhenAll(reads).GetAwaiter().GetResult();
                        var live=RoleCatalog.Parse(changed.Catalog!);
                        if(live.FeatureLimits.Feed!=allowed||live.Roles.Single().Settings.Feed!=raw.Feed)throw new InvalidDataException("Global gate changed role settings");
                        var baseline=changed.Catalog!;live.RefreshIntervalMs=4+i;
                        var save=HostConnection.Call(new("save-catalog",Baseline:baseline,Catalog:System.Text.Json.JsonSerializer.Serialize(live,CoreJson.Default.RoleCatalog)),pipe);
                        if(RoleCatalog.Parse(save.Catalog!).RefreshIntervalMs!=4+i)throw new InvalidDataException("Configuration save failed");
                    }
                    var roleId=initial.Roles.Single().Id;
                    if(HostConnection.Call(new("recall",roleId),pipe).Instances.Length!=0)throw new InvalidDataException("Recall failed");
                    if(HostConnection.Call(new("summon",roleId),pipe).Instances.Length!=1)throw new InvalidDataException("Summon failed");
                    if(!HostConnection.Call(new("diagnostics"),pipe).Success)throw new InvalidDataException("Diagnostics failed");
                }
                finally{loop.Post(loop.Stop);}
            });
            loop.Run();work.GetAwaiter().GetResult();
            File.WriteAllText(output,"Package registration IPC passed");return 0;
        }
        catch(Exception e){File.WriteAllText(output,e.ToString());return 1;}
        finally{if(Directory.Exists(dataRoot))Directory.Delete(dataRoot,true);}
    }
    public static int Run(string[] args)
    {
        if(args.Length==3&&args[0]=="--import-probe")return ImportProbe(args[1],args[2]);
        if(args.Length is not (5 or 6))return 2;
        var root=Path.GetFullPath(args[1]);int count=int.Parse(args[2]);
        int duration=Math.Clamp(int.Parse(args[3]),1000,600000);
        string output=Path.GetFullPath(args[4]);int interval=args.Length==6?Math.Clamp(int.Parse(args[5]),1,50):3;
        if(count is not (-1 or 0 or 1 or 10))return 2;
        var dataRoot=Path.Combine(Path.GetDirectoryName(output)!,"probe-data-"+Guid.NewGuid().ToString("N"));
        var samples=new List<Dictionary<string,object?>>();
        try
        {
            if(count==-1)
            {
                var clock=Stopwatch.StartNew();
                do{samples.Add(MemoryDiagnostics.Sample("baseline",new(){["elapsedMs"]=clock.ElapsedMilliseconds,["pets"]=0,["nativeCacheBytes"]=0L,["ticks"]=0L}));Thread.Sleep(Math.Min(1000,duration));}while(clock.ElapsedMilliseconds<duration);
                samples.Add(MemoryDiagnostics.Sample("baseline",new(){["elapsedMs"]=clock.ElapsedMilliseconds,["pets"]=0,["nativeCacheBytes"]=0L,["ticks"]=0L}));
            }
            else
            {
                using var loop=new HostLoop();
                // This also exercises source-generated configuration, validation and native interop.
                using var resident=new ResidentHost(root,loop,dataRoot,true);
                resident.ConfigureProbe(count,interval);
                // Real pipe server and real request/response payloads, but an isolated endpoint.
                using var server=new HostServer(loop,resident.Handle,"CakePet.Probe."+Environment.ProcessId);
                var ipc=Task.Run(()=>HostConnection.Call(new("snapshot"),"CakePet.Probe."+Environment.ProcessId));
                var clock=Stopwatch.StartNew();long next=0;
                loop.Tick+=()=>{
                    long now=clock.ElapsedMilliseconds;
                    if(now>=next||now>=duration){var sample=resident.Sample("resident");sample["elapsedMs"]=now;samples.Add(sample);next=now+5000;}
                    if(now>=duration)loop.Stop();
                };
                loop.Run();
                if(!ipc.GetAwaiter().GetResult().Success)throw new InvalidDataException("IPC smoke test failed");
                if(samples.Count==0||Convert.ToInt32(samples[^1]["pets"])!=count)throw new InvalidDataException("Probe instance count mismatch");
                if(count==0&&resident.TickCount<duration*0.75/interval)throw new InvalidDataException("High-resolution scheduler below 75% of target on idle host");
                if(!RuntimeFeature.IsDynamicCodeSupported&&samples.Any(s=>((IEnumerable<string>)s["frameworkModules"]!).Any(m=>m.Contains("coreclr",StringComparison.OrdinalIgnoreCase)||m.Contains("clrjit",StringComparison.OrdinalIgnoreCase))))throw new InvalidDataException("AOT host loaded CoreCLR/JIT");
                if(samples.Any(s=>((IEnumerable<string>)s["frameworkModules"]!).Any(m=>m.Contains("wpf",StringComparison.OrdinalIgnoreCase)||m.Contains("Presentation",StringComparison.OrdinalIgnoreCase))))throw new InvalidDataException("Resident loaded WPF");
            }
            var report=new Dictionary<string,object?>{["runtime"]=RuntimeFeature.IsDynamicCodeSupported?"JIT":"NativeAOT",["count"]=count,["intervalMs"]=interval,["durationMs"]=duration,["samples"]=samples};
            File.WriteAllText(output,MemoryDiagnostics.ToJson(report));return 0;
        }
        catch(Exception e){File.WriteAllText(output+".error",e.ToString());return 1;}
        finally{if(Directory.Exists(dataRoot))Directory.Delete(dataRoot,true);}
    }
}
