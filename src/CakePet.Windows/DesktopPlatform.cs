using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using CakePet.Core;
using P=CakePet.Core.Point;

namespace CakePet.Windows;

// Win32 monitor rectangles, pointer coordinates and window placement share physical pixels.
// WPF DIPs are used only inside each HWND for drawing.
internal static class DesktopPlatform
{
    [StructLayout(LayoutKind.Sequential)]private struct Rect {public int Left,Top,Right,Bottom; public Bounds Bounds=>new(Left,Top,Right,Bottom);}
    [StructLayout(LayoutKind.Sequential)]private struct CursorPoint {public int X,Y;}
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]private struct MonitorInfo
    {
        public int Size;public Rect Monitor,Work;public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr,SizeConst=32)]public string Device;
    }
    private delegate bool MonitorCallback(nint monitor,nint dc,nint rect,nint data);
    [DllImport("user32.dll")]private static extern bool EnumDisplayMonitors(nint dc,nint clip,MonitorCallback callback,nint data);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]private static extern bool GetMonitorInfo(nint monitor,ref MonitorInfo info);
    [DllImport("user32.dll")]private static extern bool GetCursorPos(out CursorPoint p);
    [DllImport("user32.dll")]private static extern bool SetWindowPos(nint hwnd,nint after,int x,int y,int cx,int cy,uint flags);
    [DllImport("user32.dll")]private static extern bool GetWindowRect(nint hwnd,out Rect r);
    [DllImport("user32.dll")]private static extern uint GetDpiForWindow(nint hwnd);
    [DllImport("user32.dll")]private static extern short GetAsyncKeyState(int key);
    private static IReadOnlyList<DesktopScreen> screens=ReadScreens();
    private static long refreshed=Environment.TickCount64;
    public static IReadOnlyList<DesktopScreen> Screens
    {
        get {if(Environment.TickCount64-refreshed>=500){screens=ReadScreens();refreshed=Environment.TickCount64;}return screens;}
    }
    private static IReadOnlyList<DesktopScreen> ReadScreens()
    {
        var list=new List<DesktopScreen>();
        EnumDisplayMonitors(0,0,(monitor,dc,rect,data)=>{
            var info=new MonitorInfo{Size=Marshal.SizeOf<MonitorInfo>(),Device=""};
            if(GetMonitorInfo(monitor,ref info))list.Add(new DesktopScreen(info.Device,info.Monitor.Bounds,info.Work.Bounds,(info.Flags&1)!=0));
            return true;
        },0);
        if(list.Count==0)
        {
            var r=SystemParameters.WorkArea;var full=new Bounds(0,0,SystemParameters.PrimaryScreenWidth,SystemParameters.PrimaryScreenHeight);
            list.Add(new DesktopScreen("primary",full,new Bounds(r.Left,r.Top,r.Right,r.Bottom),true));
        }
        return list;
    }
    public static DesktopScreen Primary=>Screens.FirstOrDefault(x=>x.Primary)??Screens[0];
    public static DesktopScreen At(P p)=>DesktopGeometry.Nearest(Screens,p);
    public static DesktopScreen Resolve(string id,P fallback)=>Screens.FirstOrDefault(x=>x.Id==id)??At(fallback);
    public static P Cursor(){GetCursorPos(out var p);return new P(p.X,p.Y);}
    public static bool ControlDown=>(GetAsyncKeyState(0x11)&0x8000)!=0;
    public static double DpiScale(Window window)
    {
        var h=new WindowInteropHelper(window).Handle;var dpi=h==0?96:GetDpiForWindow(h);return dpi==0?1:dpi/96.0;
    }
    public static void Place(Window window,double x,double y,double width,double height)
    {
        var h=new WindowInteropHelper(window).Handle;if(h==0)return;
        int ix=(int)Math.Round(x),iy=(int)Math.Round(y),w=Math.Max(1,(int)Math.Round(width)),he=Math.Max(1,(int)Math.Round(height));
        if(GetWindowRect(h,out var old)&&old.Left==ix&&old.Top==iy&&old.Right-old.Left==w&&old.Bottom-old.Top==he)return;
        SetWindowPos(h,0,ix,iy,w,he,0x0010|0x0004|0x0200);
    }
}
