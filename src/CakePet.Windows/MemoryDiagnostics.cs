using System.Diagnostics;
using System.Runtime.InteropServices;

namespace CakePet.Windows;

internal static class MemoryDiagnostics
{
    internal static Dictionary<string,object?> Sample(string phase,Dictionary<string,object?>? extra=null)
    {
        using var process=Process.GetCurrentProcess();process.Refresh();
        var gc=GC.GetGCMemoryInfo();
        var result=extra??new();
        result["phase"]=phase;result["timestamp"]=DateTimeOffset.Now;
        result["workingSetBytes"]=process.WorkingSet64;result["privateBytes"]=process.PrivateMemorySize64;
        result["managedLiveBytes"]=GC.GetTotalMemory(false);result["gcHeapBytes"]=gc.HeapSizeBytes;result["gcCommittedBytes"]=gc.TotalCommittedBytes;
        result["handles"]=process.HandleCount;result["threads"]=process.Threads.Count;
        result["managedFrameworkAssemblies"]=AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetName().Name).Where(n=>n!=null&&(n.Contains("Presentation",StringComparison.OrdinalIgnoreCase)||n.Contains("WindowsForms",StringComparison.OrdinalIgnoreCase)||n.Contains("System.Drawing",StringComparison.OrdinalIgnoreCase)||n=="WindowsBase"||n=="System.Xaml")).OrderBy(n=>n).ToArray();
        result["cpuMilliseconds"]=process.TotalProcessorTime.TotalMilliseconds;
        result["frameworkModules"]=process.Modules.Cast<ProcessModule>().Select(x=>x.ModuleName)
            .Where(x=>x!=null&&(x.Contains("wpf",StringComparison.OrdinalIgnoreCase)||x.Contains("Presentation",StringComparison.OrdinalIgnoreCase)||x.Contains("WindowsForms",StringComparison.OrdinalIgnoreCase)||x.Contains("System.Drawing",StringComparison.OrdinalIgnoreCase)||x.Contains("coreclr",StringComparison.OrdinalIgnoreCase)||x.Contains("milcore",StringComparison.OrdinalIgnoreCase)||x.Contains("wpfgfx",StringComparison.OrdinalIgnoreCase))).ToArray();
        return result;
    }
    internal static string Summary(Dictionary<string,object?> report)
    {
        string Size(string key)=>(Convert.ToDouble(report[key])/(1024*1024)).ToString("F1")+" MiB";
        return $"工作集：{Size("workingSetBytes")}\n专用提交内存：{Size("privateBytes")}\n托管内存估算：{Size("managedLiveBytes")}\nGC 已提交：{Size("gcCommittedBytes")}\n图片缓存：{Size("imageCacheBytes")}\n食物缓存：{Size("foodCacheBytes")}"+(report.ContainsKey("nativeCacheBytes")?"\n原生缓存："+Size("nativeCacheBytes"):"");
    }
}
internal static class NativeMessage
{
    [DllImport("user32.dll",EntryPoint="MessageBoxW",CharSet=CharSet.Unicode)]private static extern int MessageBox(nint owner,string text,string caption,uint type);
    public static void Show(string text,string title)=>MessageBox(0,text,title,0x40);
}
