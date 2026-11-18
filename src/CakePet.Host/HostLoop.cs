using System.Collections.Concurrent;
using System.Runtime.InteropServices;
namespace CakePet.Windows;
internal sealed class HostLoop:IDisposable
{
    private readonly uint thread=GetCurrentThreadId();
    private readonly ConcurrentQueue<Action> queue=new();
    private const uint Wake=0x8005;
    private bool stopped;
    private nuint timer;
    public event Action? Tick;
    public HostLoop(){PeekMessage(out _,0,0,0,0);}
    public void Post(Action action){queue.Enqueue(action);PostThreadMessage(thread,Wake,0,0);}
    public void Stop(){stopped=true;PostQuitMessage(0);}
    public int Run()
    {
        timer=SetTimer(0,0,16,0);if(timer==0)throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            while(!stopped)
            {
                int result=GetMessage(out var m,0,0,0);if(result<=0)return result<0?1:0;
                if(m.Hwnd==0&&m.Message==Wake){while(!stopped&&queue.TryDequeue(out var a))a();continue;}
                if(m.Hwnd==0&&m.Message==0x113){Tick?.Invoke();continue;}
                TranslateMessage(ref m);DispatchMessage(ref m);
            }
            return 0;
        }
        finally{if(timer!=0){KillTimer(0,timer);timer=0;}}
    }
    public void Dispose(){if(timer!=0){KillTimer(0,timer);timer=0;}}
    [StructLayout(LayoutKind.Sequential)]private struct Msg{public nint Hwnd;public uint Message;public nuint WParam;public nint LParam;public uint Time;public int X,Y;public uint Private;}
    [DllImport("kernel32.dll")]private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll",EntryPoint="PostThreadMessageW")]private static extern bool PostThreadMessage(uint id,uint msg,nuint w,nint l);
    [DllImport("user32.dll")]private static extern void PostQuitMessage(int code);
    [DllImport("user32.dll",EntryPoint="PeekMessageW")]private static extern bool PeekMessage(out Msg m,nint h,uint min,uint max,uint remove);
    [DllImport("user32.dll",EntryPoint="GetMessageW")]private static extern int GetMessage(out Msg m,nint h,uint min,uint max);
    [DllImport("user32.dll")]private static extern bool TranslateMessage(ref Msg m);
    [DllImport("user32.dll",EntryPoint="DispatchMessageW")]private static extern nint DispatchMessage(ref Msg m);
    [DllImport("user32.dll",SetLastError=true)]private static extern nuint SetTimer(nint h,nuint id,uint interval,nint callback);
    [DllImport("user32.dll")]private static extern bool KillTimer(nint h,nuint id);
}
