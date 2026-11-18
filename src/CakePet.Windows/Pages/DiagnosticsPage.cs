using System.Diagnostics;
using System.IO;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Win32;
namespace CakePet.Windows;

internal sealed class DiagnosticsPage:ScrollViewer
{
    private readonly DispatcherTimer refresh=new(){Interval=TimeSpan.FromSeconds(1)};
    private readonly Dictionary<string,TextBlock> values=new();
    private readonly TextBlock status;
    private readonly bool memoryOnly;
    private int generation;
    private JsonObject? latest;
    private bool sampling;
    private long previousTicks,previousTime;
    private int previousPid;
    public DiagnosticsPage(Action back,Action? openMemory=null,bool memoryOnly=false)
    {
        this.memoryOnly=memoryOnly;
        VerticalScrollBarVisibility=ScrollBarVisibility.Auto;
        var panel=new StackPanel{MaxWidth=850,Margin=new Thickness(22)};Content=panel;
        panel.Children.Add(Ui.Light("‹ 返回",back));
        var header=Ui.Caption(memoryOnly?"内存占用":"调试信息",memoryOnly?"分别查看常驻进程与设置进程的内存占用。":"查看运行状态与实际更新频率。",true);header.Margin=new Thickness(0,16,0,16);panel.Children.Add(header);
        var resident=new SettingCard("常驻进程","桌宠实际运行的进程。",collapsible:false);
        if(memoryOnly)
        {
            foreach(var (key,title) in new (string,string)[]{("workingSetBytes","工作集"),("privateBytes","专用提交内存"),("managedLiveBytes","托管内存估算"),("gcCommittedBytes","GC 已提交"),("nativeCacheBytes","原生缓存")})Add(resident,key,title);
            panel.Children.Add(resident);
            var settings=new SettingCard("设置进程","关闭设置后，此进程退出。",collapsible:false);
            Add(settings,"settingsWorkingSetBytes","工作集");Add(settings,"settingsPrivateBytes","专用提交内存");panel.Children.Add(settings);
        }
        else
        {
            foreach(var (key,title) in new (string,string)[]{("runtime","运行时"),("pid","进程 ID"),("pets","角色实例"),("foodWindows","食物窗口"),("loadedPackages","已加载素材包"),("intervalMs","刷新间隔"),("actualHz","实际更新频率"),("handles","句柄数"),("threads","线程数"),("frameworkModules","框架模块")})Add(resident,key,title);
            panel.Children.Add(resident);
            if(openMemory!=null)panel.Children.Add(Ui.Card(Ui.Pair(Ui.Caption("内存占用","分别查看常驻进程和设置进程。"),Ui.Light("进入 ›",openMemory))));
        }
        status=Ui.Text("正在读取…",12,true);panel.Children.Add(status);
        var actions=new StackPanel{Orientation=Orientation.Horizontal,Margin=new Thickness(0,12,0,0)};
        actions.Children.Add(Ui.Button("复制调试信息",()=>{if(latest!=null)Clipboard.SetText(latest.ToJsonString(new System.Text.Json.JsonSerializerOptions{WriteIndented=true}));}));
        actions.Children.Add(Ui.Light("导出诊断报告",Export));panel.Children.Add(actions);
        refresh.Tick+=async(_,_)=>await Refresh();
        Loaded+=(_,_)=>Start();Unloaded+=(_,_)=>Stop();
        IsVisibleChanged+=(_,_)=>{if(IsVisible&&IsLoaded)Start();else Stop();};
    }
    private void Add(SettingCard card,string key,string title)
    {
        var value=Ui.Text("—",13);value.TextWrapping=TextWrapping.Wrap;value.MaxWidth=420;values[key]=value;
        card.Add(Ui.Pair(Ui.Caption(title),value));
    }
    private void Start(){if(refresh.IsEnabled)return;previousTime=0;refresh.Start();_ = Refresh();}
    private void Stop(){refresh.Stop();previousTime=0;generation++;}
    private async Task Refresh()
    {
        if(sampling||!IsVisible||!IsLoaded)return;sampling=true;int currentGeneration=generation;
        try
        {
            var report=await Task.Run(()=>Collect(memoryOnly));
            if(currentGeneration!=generation||!IsVisible||!IsLoaded)return;
            int pid=report["pid"]!.GetValue<int>();long ticks=report["ticks"]!.GetValue<long>();long now=Stopwatch.GetTimestamp();
            if(previousTime!=0&&previousPid==pid&&ticks>=previousTicks)report["actualHz"]=Math.Round((ticks-previousTicks)* (double)Stopwatch.Frequency/Math.Max(1,now-previousTime),1);
            previousPid=pid;previousTicks=ticks;previousTime=now;
            latest=report;
            foreach(var entry in values)
            {
                var node=report[entry.Key];string text=node?.ToString()??"—";
                if(node!=null&&entry.Key.EndsWith("Bytes"))text=(node.GetValue<long>()/1048576.0).ToString("F1")+" MiB";
                else if(node!=null&&entry.Key=="intervalMs")text+=" ms";
                else if(node!=null&&entry.Key=="actualHz")text+=" Hz";
                else if(entry.Key=="frameworkModules"&&node is JsonArray modules)text=modules.Count==0?"无 CoreCLR/JIT/WPF":string.Join(", ",modules.Select(x=>x?.ToString()));
                entry.Value.Text=text;
            }
            status.Text="更新于 "+DateTime.Now.ToString("HH:mm:ss");
        }
        catch(Exception e){if(currentGeneration==generation&&IsVisible&&IsLoaded)status.Text="读取失败："+e.Message;}
        finally{sampling=false;}
    }
    private static JsonObject Collect(bool memoryOnly)
    {
        var report=JsonNode.Parse(HostConnection.Call(new("diagnostics")).Catalog??throw new InvalidDataException("Missing diagnostics"))!.AsObject();
        using var process=Process.GetProcessById(report["pid"]!.GetValue<int>());process.Refresh();
        report["timestamp"]=DateTimeOffset.Now.ToString("O");report["phase"]="native-resident";
        if(memoryOnly)
        {
            report["workingSetBytes"]=process.WorkingSet64;report["privateBytes"]=process.PrivateMemorySize64;
            using var settings=Process.GetCurrentProcess();settings.Refresh();report["settingsWorkingSetBytes"]=settings.WorkingSet64;report["settingsPrivateBytes"]=settings.PrivateMemorySize64;
        }
        else
        {
            report["handles"]=process.HandleCount;report["threads"]=process.Threads.Count;report["cpuMilliseconds"]=process.TotalProcessorTime.TotalMilliseconds;
            report["frameworkModules"]=new JsonArray(process.Modules.Cast<ProcessModule>().Select(m=>m.ModuleName).Where(n=>n.Contains("coreclr",StringComparison.OrdinalIgnoreCase)||n.Contains("clrjit",StringComparison.OrdinalIgnoreCase)||n.Contains("wpf",StringComparison.OrdinalIgnoreCase)||n.Contains("Presentation",StringComparison.OrdinalIgnoreCase)).Select(n=>(JsonNode?)JsonValue.Create(n)).ToArray());
        }
        if(!memoryOnly)foreach(var key in new[]{"managedLiveBytes","gcHeapBytes","gcCommittedBytes","imageCacheBytes","foodCacheBytes","nativeCacheBytes"})report.Remove(key);
        return report;
    }
    private void Export()
    {
        if(latest==null)return;
        var dialog=new SaveFileDialog{FileName="CakePet-diagnostics-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+".json",Filter="JSON 文件|*.json"};
        if(dialog.ShowDialog(Window.GetWindow(this))!=true)return;
        try{File.WriteAllText(dialog.FileName,latest.ToJsonString(new System.Text.Json.JsonSerializerOptions{WriteIndented=true}));status.Text="已导出："+dialog.FileName;}
        catch(Exception e){status.Text="导出失败："+e.Message;}
    }
}
