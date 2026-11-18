using System.Diagnostics;
using System.Text.Json.Nodes;
using System.Runtime.CompilerServices;
namespace CakePet.Windows;

// A second invocation of the same AOT executable, with no resident, tray, engine or settings UI.
internal static class DiagnosticClient
{
    internal static int Run(string[] args)
    {
        try
        {
            int pid=int.Parse(args[1]);string directory=args[2];
            using var process=Process.GetProcessById(pid);process.Refresh();
            var report=JsonNode.Parse(HostConnection.Call(new("diagnostics"),args[3]).Catalog??throw new InvalidDataException("Missing diagnostics"))!.AsObject();
            report["timestamp"]=DateTimeOffset.Now.ToString("O");report["phase"]="native-resident";
            report["workingSetBytes"]=process.WorkingSet64;report["privateBytes"]=process.PrivateMemorySize64;
            report["handles"]=process.HandleCount;report["threads"]=process.Threads.Count;
            report["cpuMilliseconds"]=process.TotalProcessorTime.TotalMilliseconds;
            report["frameworkModules"]=new JsonArray(process.Modules.Cast<ProcessModule>().Select(m=>m.ModuleName).Where(n=>n.Contains("coreclr",StringComparison.OrdinalIgnoreCase)||n.Contains("clrjit",StringComparison.OrdinalIgnoreCase)||n.Contains("wpf",StringComparison.OrdinalIgnoreCase)||n.Contains("Presentation",StringComparison.OrdinalIgnoreCase)).Select(n=>(JsonNode?)JsonValue.Create(n)).ToArray());
            Directory.CreateDirectory(directory);
            var path=Path.Combine(directory,"memory-host-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+".json");
            File.WriteAllText(path,report.ToJsonString(new System.Text.Json.JsonSerializerOptions{WriteIndented=true,TypeInfoResolver=CakePet.Core.CoreJson.Default}));
            string Size(string key)=>(report[key]!.GetValue<double>()/1048576).ToString("F1")+" MiB";
            NativeMessage.Show($"运行时：{report["runtime"]}\n工作集：{Size("workingSetBytes")}\n专用提交内存：{Size("privateBytes")}\n托管内存估算：{Size("managedLiveBytes")}\nGC 已提交：{Size("gcCommittedBytes")}\n原生缓存：{Size("nativeCacheBytes")}\n刷新间隔：{report["intervalMs"]} ms\n\n诊断文件：\n{path}","CakePet · 内存诊断");
            return 0;
        }
        catch(Exception e){NativeMessage.Show(e.Message,"CakePet · 内存诊断");return 1;}
    }
}
