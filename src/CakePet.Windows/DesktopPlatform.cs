using System.Runtime.InteropServices;
#if !NATIVE_HOST
using System.Windows;
using System.Windows.Interop;
#endif
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
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]private static extern nint FindWindow(string className,string? title);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]private static extern nint FindWindowEx(nint parent,nint after,string className,string? title);
    [DllImport("user32.dll")]private static extern bool IsWindowVisible(nint window);
    [DllImport("user32.dll")]private static extern bool GetWindowRect(nint hwnd,out Rect r);
    [DllImport("user32.dll")]private static extern uint GetDpiForWindow(nint hwnd);
    [DllImport("user32.dll")]private static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")]private static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll",EntryPoint="SystemParametersInfoW")]private static extern bool SystemParametersInfo(uint action,uint param,out Rect rect,uint flags);
    private static IReadOnlyList<DesktopScreen> screens=ReadScreens();
    public static long ScreenRevision {get;private set;}
    private static long refreshed=Environment.TickCount64;
    public static IReadOnlyList<DesktopScreen> Screens
    {
        get {if(Environment.TickCount64-refreshed>=500){var next=ReadScreens();if(!screens.SequenceEqual(next)){screens=next;ScreenRevision++;}refreshed=Environment.TickCount64;}return screens;}
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
            var full=new Bounds(0,0,Math.Max(1,GetSystemMetrics(0)),Math.Max(1,GetSystemMetrics(1)));
            var work=SystemParametersInfo(0x30,0,out var rect,0)?rect.Bounds:full;
            list.Add(new DesktopScreen("primary",full,work,true));
        }
        return list;
    }
    private readonly record struct SpaceKey(string Monitor,bool FullScreen,double Left,double Top,double Right,double Bottom);
    private static readonly Dictionary<SpaceKey,DesktopSpace> spaces=new();
    private static long spacesRevision=-1;
    public static DesktopSpace ActivitySpace(DesktopScreen monitor,RoleSettings settings)
    {
        var all=Screens;
        if(spacesRevision!=ScreenRevision){spaces.Clear();spacesRevision=ScreenRevision;}
        var key=new SpaceKey(settings.ScreenConnect?"":monitor.Id,settings.FullScreen,settings.OffsetLeft,settings.OffsetTop,settings.OffsetRight,settings.OffsetBottom);
        if(spaces.TryGetValue(key,out var existing))return existing;
        var selected=settings.ScreenConnect?all:new[]{monitor};
        var raw=selected.Select(s=>settings.FullScreen?s.Full:s.Work).ToArray();
        var areas=raw.Select(a=>{
            bool left=raw.Any(b=>b.Right==a.Left&&Math.Min(a.Bottom,b.Bottom)>Math.Max(a.Top,b.Top));
            bool right=raw.Any(b=>b.Left==a.Right&&Math.Min(a.Bottom,b.Bottom)>Math.Max(a.Top,b.Top));
            bool top=raw.Any(b=>b.Bottom==a.Top&&Math.Min(a.Right,b.Right)>Math.Max(a.Left,b.Left));
            bool bottom=raw.Any(b=>b.Top==a.Bottom&&Math.Min(a.Right,b.Right)>Math.Max(a.Left,b.Left));
            double l=a.Left+(left?0:settings.OffsetLeft),t=a.Top+(top?0:settings.OffsetTop);
            return new Bounds(l,t,Math.Max(l+1,a.Right-(right?0:settings.OffsetRight)),Math.Max(t+1,a.Bottom-(bottom?0:settings.OffsetBottom)));
        }).Distinct().OrderBy(a=>a.Left).ThenBy(a=>a.Top).ThenBy(a=>a.Right).ThenBy(a=>a.Bottom).ToArray();
        if(areas.Length==0)areas=new[]{settings.ApplyOffsets(settings.FullScreen?monitor.Full:monitor.Work)};
        // Settings sliders can generate many unique rectangles: keep the lookup bounded.
        if(spaces.Count>=64)spaces.Clear();
        return spaces[key]=new DesktopSpace(areas);
    }
    public static DesktopScreen Primary{get{var all=Screens;foreach(var s in all)if(s.Primary)return s;return all[0];}}
    public static DesktopScreen At(P p)=>DesktopGeometry.Nearest(Screens,p);
    public static DesktopScreen Resolve(string id,P fallback){var all=Screens;foreach(var s in all)if(s.Id==id)return s;return DesktopGeometry.Nearest(all,fallback);}
    private static long taskbarRead=-1000;
    private static (Bounds Bar,Bounds? Tray)? taskbar;
    public static (Bounds Bar,Bounds? Tray)? Taskbar()
    {
        if(Environment.TickCount64-taskbarRead<500)return taskbar;taskbarRead=Environment.TickCount64;taskbar=null;
        var hwnd=FindWindow("Shell_TrayWnd",null);if(hwnd==0||!IsWindowVisible(hwnd)||!GetWindowRect(hwnd,out var bar))return null;
        var t=FindWindowEx(hwnd,0,"TrayNotifyWnd",null);Bounds? tray=t!=0&&GetWindowRect(t,out var tr)?tr.Bounds:null;
        var area=bar.Bounds;var full=Primary.Full;
        if(area.Right<=full.Left||area.Left>=full.Right||area.Bottom<=full.Top||area.Top>=full.Bottom||area.Right-area.Left<4||area.Bottom-area.Top<4)return null;
        taskbar=(area,tray);return taskbar;
    }
    public static P Cursor(){GetCursorPos(out var p);return new P(p.X,p.Y);}
    public static bool ControlDown=>(GetAsyncKeyState(0x11)&0x8000)!=0;
#if !NATIVE_HOST
    public static double DpiScale(Window window)
    {
        var h=new WindowInteropHelper(window).Handle;var dpi=h==0?96:GetDpiForWindow(h);return dpi==0?1:dpi/96.0;
    }
    private sealed class Placement
    {
        public int X,Y,Width,Height;public nint Handle;public bool Valid;public long Revision;
    }
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Window,Placement> placements=new();
    public static void Place(Window window,double x,double y,double width,double height)
    {
        var h=new WindowInteropHelper(window).Handle;if(h==0)return;
        int ix=(int)Math.Round(x),iy=(int)Math.Round(y),w=Math.Max(1,(int)Math.Round(width)),he=Math.Max(1,(int)Math.Round(height));
        var last=placements.GetValue(window,key=>{
            var entry=new Placement();
            key.DpiChanged+=(_,_)=>entry.Valid=false;
            key.SizeChanged+=(_,_)=>entry.Valid=false;
            key.LocationChanged+=(_,_)=>entry.Valid=false;
            return entry;
        });
        if(last.Valid&&last.Handle==h&&last.Revision==ScreenRevision&&last.X==ix&&last.Y==iy&&last.Width==w&&last.Height==he)return;
        if(!(GetWindowRect(h,out var old)&&old.Left==ix&&old.Top==iy&&old.Right-old.Left==w&&old.Bottom-old.Top==he))
            if(!SetWindowPos(h,0,ix,iy,w,he,0x0010|0x0004|0x0200))return;
        last.Handle=h;last.Revision=ScreenRevision;last.X=ix;last.Y=iy;last.Width=w;last.Height=he;last.Valid=true;
    }
#endif
}
