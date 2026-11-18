using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
namespace CakePet.Windows;

// One high-resolution waitable timer, integrated with the Win32 message queue.
// The host thread owns all windows and engine state. No spin loop and no per-pet timers.
internal sealed class HostLoop:IDisposable
{
    private readonly ConcurrentQueue<Action> queue=new();
    private readonly AutoResetEvent wake=new(false);
    private bool stopped;
    private nint timer;
    private int interval=3;
    private long nextDeadline;
    public event Action? Tick;
    public int IntervalMs
    {
        get=>interval;
        set
        {
            var next=Math.Clamp(value,1,50);if(next==interval)return;
            interval=next;nextDeadline=0;
        }
    }
    public HostLoop(){PeekMessage(out _,0,0,0,0);}
    public void Post(Action action){queue.Enqueue(action);wake.Set();}
    public void Stop(){stopped=true;PostQuitMessage(0);}
    private long Period=>Math.Max(1,Stopwatch.Frequency*interval/1000);
    private void Arm(long now)
    {
        long remaining=Math.Max(1,nextDeadline-now);
        long due=-Math.Max(1,(long)Math.Ceiling(remaining*10_000_000.0/Stopwatch.Frequency));
        if(!SetWaitableTimer(timer,ref due,0,0,0,false))throw new Win32Exception(Marshal.GetLastWin32Error());
    }
    public int Run()
    {
        timer=CreateWaitableTimerEx(0,null,2,0x1F0003);
        if(timer==0)timer=CreateWaitableTimerEx(0,null,0,0x1F0003);
        if(timer==0)throw new Win32Exception(Marshal.GetLastWin32Error());
        var handles=new[]{timer,wake.SafeWaitHandle.DangerousGetHandle()};
        try
        {
            while(!stopped)
            {
                // Bound message dispatch work so a busy queue cannot starve motion.
                for(int i=0;i<64&&!stopped&&PeekMessage(out var m,0,0,0,1);i++)
                {
                    if(m.Message==0x12){stopped=true;break;}
                    TranslateMessage(ref m);DispatchMessage(ref m);
                    if(nextDeadline!=0&&Stopwatch.GetTimestamp()>=nextDeadline)break;
                }
                // Drain even if a native modal menu consumed messages. Keep work bounded.
                for(int j=0;j<8&&!stopped&&queue.TryDequeue(out var action);j++)action();
                if(stopped)break;
                if(!queue.IsEmpty)wake.Set();
                var now=Stopwatch.GetTimestamp();
                if(nextDeadline==0)nextDeadline=now+Period;
                if(now>=nextDeadline)
                {
                    // Never burst through accumulated missed ticks. Physics consumes actual elapsed time.
                    nextDeadline+=Period;
                    Tick?.Invoke();
                    now=Stopwatch.GetTimestamp();
                    if(nextDeadline!=0&&now>=nextDeadline)nextDeadline=now+Period;
                    continue;
                }
                Arm(now);
                var result=MsgWaitForMultipleObjectsEx(2,handles,0xFFFFFFFF,0x04FF,0x0004);
                if(result==0xFFFFFFFF)throw new Win32Exception(Marshal.GetLastWin32Error());
            }
            return 0;
        }
        finally{if(timer!=0){CancelWaitableTimer(timer);CloseHandle(timer);timer=0;}}
    }
    public void Dispose(){if(timer!=0){CancelWaitableTimer(timer);CloseHandle(timer);timer=0;}wake.Dispose();}
    [StructLayout(LayoutKind.Sequential)]private struct Msg{public nint Hwnd;public uint Message;public nuint WParam;public nint LParam;public uint Time;public int X,Y;public uint Private;}
    [DllImport("user32.dll")]private static extern void PostQuitMessage(int code);
    [DllImport("user32.dll",EntryPoint="PeekMessageW")]private static extern bool PeekMessage(out Msg m,nint h,uint min,uint max,uint remove);
    [DllImport("user32.dll")]private static extern bool TranslateMessage(ref Msg m);
    [DllImport("user32.dll",EntryPoint="DispatchMessageW")]private static extern nint DispatchMessage(ref Msg m);
    [DllImport("kernel32.dll",EntryPoint="CreateWaitableTimerExW",CharSet=CharSet.Unicode,SetLastError=true)]private static extern nint CreateWaitableTimerEx(nint attributes,string? name,uint flags,uint access);
    [DllImport("kernel32.dll",SetLastError=true)]private static extern bool SetWaitableTimer(nint timer,ref long due,int period,nint completion,nint argument,bool resume);
    [DllImport("kernel32.dll")]private static extern bool CancelWaitableTimer(nint timer);
    [DllImport("kernel32.dll")]private static extern bool CloseHandle(nint handle);
    [DllImport("user32.dll",SetLastError=true)]private static extern uint MsgWaitForMultipleObjectsEx(uint count,[In] nint[] handles,uint milliseconds,uint mask,uint flags);
}
