using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using CakePet.Core;
namespace CakePet.Windows;

// Exercises the production resident host, not a simplified sprite test.
// Data and IPC are isolated from the user's normal installation.
internal static class HostFrameworkProbe
{
    public static int Run(string[] args)
    {
        if(args.Length!=5)return 2;
        var root=Path.GetFullPath(args[1]);int count=int.Parse(args[2]);
        int duration=Math.Clamp(int.Parse(args[3]),1000,600000);
        string output=Path.GetFullPath(args[4]);
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
                resident.ConfigureProbe(count);
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
                if(!RuntimeFeature.IsDynamicCodeSupported&&samples.Any(s=>((IEnumerable<string>)s["frameworkModules"]!).Any(m=>m.Contains("coreclr",StringComparison.OrdinalIgnoreCase)||m.Contains("clrjit",StringComparison.OrdinalIgnoreCase))))throw new InvalidDataException("AOT host loaded CoreCLR/JIT");
                if(samples.Any(s=>((IEnumerable<string>)s["frameworkModules"]!).Any(m=>m.Contains("wpf",StringComparison.OrdinalIgnoreCase)||m.Contains("Presentation",StringComparison.OrdinalIgnoreCase))))throw new InvalidDataException("Resident loaded WPF");
            }
            var report=new Dictionary<string,object?>{["runtime"]=RuntimeFeature.IsDynamicCodeSupported?"JIT":"NativeAOT",["count"]=count,["durationMs"]=duration,["samples"]=samples};
            File.WriteAllText(output,MemoryDiagnostics.ToJson(report));return 0;
        }
        catch(Exception e){File.WriteAllText(output+".error",e.ToString());return 1;}
        finally{if(Directory.Exists(dataRoot))Directory.Delete(dataRoot,true);}
    }
}
