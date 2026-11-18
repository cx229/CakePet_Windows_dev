using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json;
using CakePet.Windows;

namespace CakePet.NativeProbe;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            if(args.Length<2)throw new ArgumentException("Usage: baseline|tray <report.json> [ms]; sprite <package> <report.json> [ms]; interactive <package>");
            if(args[0]=="baseline")return Baseline(args);
            return RunNative(args);
        }
        catch(Exception e){Console.Error.WriteLine(e);return 1;}
    }
    private static int Duration(string[] args,int i)=>args.Length>i?Math.Clamp(int.Parse(args[i]),1000,60000):5000;
    private static void Save(string output,Dictionary<string,object?> data)
        =>File.WriteAllText(Path.GetFullPath(output),JsonSerializer.Serialize(data,new JsonSerializerOptions{WriteIndented=true}));
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int Baseline(string[] args)
    {
        var duration=Duration(args,2);Thread.Sleep(duration);
        Save(args[1],MemoryDiagnostics.Sample("dotnet-baseline",new(){["durationMs"]=duration,["sprites"]=0,["imageCacheBytes"]=0L,["foodCacheBytes"]=0L}));
        return 0;
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int RunNative(string[] args)
    {
        var mode=args[0];bool interactive=mode=="interactive";bool sprite=mode=="sprite"||interactive;
        if(mode!="tray"&&!sprite||sprite&&!interactive&&args.Length<3)throw new ArgumentException("Invalid probe mode.");
        string? output=interactive?null:args[sprite?2:1];
        int duration=interactive?int.MaxValue:Duration(args,sprite?3:2);
        using var role=sprite?new NativeSprite(args[1]):null;
        string iconPath=sprite?Path.Combine(Path.GetFullPath(args[1]),"..","CakePet.ico"):"";
        using var tray=new NativeTray(iconPath,()=>role?.ReturnHome(),a=>a(),new (string?,Action?)[]{
            ("归位",()=>role?.ReturnHome()),("退出",()=>PostQuitMessage(0))
        },new Guid("0b20b633-01d0-45da-bcb4-79085d37448e"),"CakePet native probe");
        var watch=Stopwatch.StartNew();long last=0;
        nuint timer=SetTimer(0,0,16,0);if(timer==0)throw new InvalidOperationException("SetTimer failed.");
        try
        {
            while(true)
            {
                int status=GetMessage(out var msg,0,0,0);
                if(status<0)throw new InvalidOperationException("GetMessage failed.");
                if(status==0)break;
                if(msg.Message==0x113&&msg.Hwnd==0&&msg.WParam==timer)
                {
                    var now=watch.ElapsedMilliseconds;var dt=Math.Clamp(now-last,0,100);last=now;
                    role?.Step(dt);
                    if(now>=duration)
                    {
                        if(output!=null)Save(output,MemoryDiagnostics.Sample("native-"+mode,new(){
                            ["durationMs"]=now,["trayRegistered"]=tray.Registered,["sprites"]=role==null?0:1,
                            ["imageCacheBytes"]=role?.CachedBytes??0,["foodCacheBytes"]=0L}));
                        break;
                    }
                }
                else{TranslateMessage(ref msg);DispatchMessage(ref msg);}
            }
        }
        finally{KillTimer(0,timer);}
        if(role?.Failure is Exception failure)throw failure;
        return 0;
    }
    [StructLayout(LayoutKind.Sequential)]private struct Point{public int X,Y;}
    [StructLayout(LayoutKind.Sequential)]private struct Msg{public nint Hwnd;public uint Message;public nuint WParam;public nint LParam;public uint Time;public Point Pt;public uint Private;}
    [DllImport("user32.dll")]private static extern nuint SetTimer(nint hwnd,nuint id,uint ms,nint proc);
    [DllImport("user32.dll")]private static extern bool KillTimer(nint hwnd,nuint id);
    [DllImport("user32.dll",EntryPoint="GetMessageW")]private static extern int GetMessage(out Msg msg,nint hwnd,uint min,uint max);
    [DllImport("user32.dll")]private static extern bool TranslateMessage(ref Msg msg);
    [DllImport("user32.dll",EntryPoint="DispatchMessageW")]private static extern nint DispatchMessage(ref Msg msg);
    [DllImport("user32.dll")]internal static extern void PostQuitMessage(int code);
}
