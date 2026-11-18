using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;

namespace CakePet.Windows;

// Native notification-area icon; no WinForms or System.Drawing runtime.
internal sealed class NativeTray : IDisposable
{
    private const uint Callback=0x8001, NifGuid=0x20, NifMessage=1, NifIcon=2, NifTip=4, NifShowTip=0x80;
    private readonly Guid identity;
    private readonly string tooltip;
    internal bool Registered=>registered;
    private readonly Action activate;
    private readonly Action<Action> dispatch;
    private readonly (string? Label,Action? Run)[] items;
    private readonly string className="CakePet.Tray."+Environment.ProcessId;
    private readonly WndProc procedure;
    private readonly nint instance=GetModuleHandle(null);
    private readonly uint taskbarCreated=RegisterWindowMessage("TaskbarCreated");
    private nint hwnd,icon;
    private bool ownsIcon,registered,version4,useGuid=true,disposed;
    public NativeTray(string iconPath,Action activate,Action<Action> dispatch,(string? Label,Action? Run)[] items,Guid? identity=null,string tooltip="CakePet")
    {
        this.activate=activate;this.dispatch=dispatch;this.items=items;this.identity=identity??new Guid("9e0ecb47-15ad-4bdb-a290-0c3ff6edcd10");this.tooltip=tooltip;procedure=OnMessage;
        var cls=new WindowClass{Size=(uint)Marshal.SizeOf<WindowClass>(),Instance=instance,Procedure=Marshal.GetFunctionPointerForDelegate(procedure),ClassName=className};
        if(RegisterClassEx(ref cls)==0)throw new Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            hwnd=CreateWindowEx(0x80,className,"CakePet",0x80000000,0,0,0,0,0,0,instance,0);
            if(hwnd==0)throw new Win32Exception(Marshal.GetLastWin32Error());
            if(File.Exists(iconPath)){icon=LoadImage(0,iconPath,1,0,0,0x10|0x40);ownsIcon=icon!=0;}
            if(icon==0)icon=LoadIcon(0,(nint)32512);
            Add();
            // Explorer may not be ready at logon. TaskbarCreated retries registration.
        }
        catch{Dispose();throw;}
    }
    private NotifyData Data(uint flags)=>new(){Size=(uint)Marshal.SizeOf<NotifyData>(),Hwnd=hwnd,Id=1,Flags=flags|(useGuid?NifGuid:0),CallbackMessage=Callback,Icon=icon,Tip=tooltip,Info="",InfoTitle="",GuidItem=identity};
    private void Add()
    {
        var data=Data(NifMessage|NifIcon|NifTip|NifShowTip);
        registered=ShellNotifyIcon(0,ref data);
        // Explorer associates GUIDs with executable paths. Fall back to ID if moved.
        if(!registered&&useGuid){useGuid=false;data=Data(NifMessage|NifIcon|NifTip|NifShowTip);registered=ShellNotifyIcon(0,ref data);}
        if(registered){data=Data(0);data.Version=4;version4=ShellNotifyIcon(4,ref data);}
    }
    private nint OnMessage(nint window,uint message,nint w,nint l)
    {
        if(message==taskbarCreated&&taskbarCreated!=0){if(!disposed&&hwnd!=0)Add();return 0;}
        if(message==Callback)
        {
            int code=version4?(int)(l.ToInt64()&0xffff):(int)l.ToInt64();
            if(code==0x0202||code==0x0400||code==0x0401){dispatch(activate);return 0;}
            if(code==0x007B||!version4&&code==0x0205){ShowMenu();return 0;}
        }
        return DefWindowProc(window,message,w,l);
    }
    private void ShowMenu()
    {
        var menu=CreatePopupMenu();if(menu==0)return;
        uint chosen=0;
        try
        {
            for(int i=0;i<items.Length;i++)
            {
                var label=items[i].Label;
                AppendMenu(menu,label==null?0x800u:0u,(nuint)(i+1),label);
            }
            GetCursorPos(out var point);SetForegroundWindow(hwnd);
            chosen=TrackPopupMenu(menu,0x100|0x2,point.X,point.Y,0,hwnd,0);
            PostMessage(hwnd,0,0,0);
        }
        finally{DestroyMenu(menu);}
        if(chosen>0&&chosen<=(uint)items.Length&&items[(int)chosen-1].Run is Action run)dispatch(run);
    }
    public void Dispose()
    {
        if(disposed)return;disposed=true;
        if(registered){var data=Data(0);ShellNotifyIcon(2,ref data);registered=false;}
        if(hwnd!=0){DestroyWindow(hwnd);hwnd=0;}
        if(ownsIcon&&icon!=0)DestroyIcon(icon);
        UnregisterClass(className,instance);
    }
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]private struct NotifyData
    {
        public uint Size;public nint Hwnd;public uint Id,Flags,CallbackMessage;public nint Icon;
        [MarshalAs(UnmanagedType.ByValTStr,SizeConst=128)]public string Tip;
        public uint State,StateMask;
        [MarshalAs(UnmanagedType.ByValTStr,SizeConst=256)]public string Info;
        public uint Version;
        [MarshalAs(UnmanagedType.ByValTStr,SizeConst=64)]public string InfoTitle;
        public uint InfoFlags;public Guid GuidItem;public nint BalloonIcon;
    }
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]private struct WindowClass
    {
        public uint Size,Style;public nint Procedure;public int ClassExtra,WindowExtra;public nint Instance,Icon,Cursor,Background;
        [MarshalAs(UnmanagedType.LPWStr)]public string? MenuName;
        [MarshalAs(UnmanagedType.LPWStr)]public string ClassName;
        public nint SmallIcon;
    }
    [StructLayout(LayoutKind.Sequential)]private struct Point{public int X,Y;}
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]private delegate nint WndProc(nint hwnd,uint message,nint w,nint l);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode)]private static extern nint GetModuleHandle(string? name);
    [DllImport("user32.dll",CharSet=CharSet.Unicode,SetLastError=true)]private static extern ushort RegisterClassEx(ref WindowClass cls);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]private static extern bool UnregisterClass(string name,nint instance);
    [DllImport("user32.dll",CharSet=CharSet.Unicode,SetLastError=true)]private static extern nint CreateWindowEx(uint ex,string cls,string name,uint style,int x,int y,int width,int height,nint parent,nint menu,nint instance,nint param);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]private static extern nint DefWindowProc(nint hwnd,uint msg,nint w,nint l);
    [DllImport("user32.dll")]private static extern bool DestroyWindow(nint hwnd);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]private static extern nint LoadImage(nint instance,string path,uint type,int x,int y,uint flags);
    [DllImport("user32.dll",EntryPoint="LoadIconW")]private static extern nint LoadIcon(nint instance,nint name);
    [DllImport("user32.dll")]private static extern bool DestroyIcon(nint icon);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]private static extern uint RegisterWindowMessage(string message);
    [DllImport("shell32.dll",EntryPoint="Shell_NotifyIconW")]private static extern bool ShellNotifyIcon(uint message,ref NotifyData data);
    [DllImport("user32.dll")]private static extern nint CreatePopupMenu();
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]private static extern bool AppendMenu(nint menu,uint flags,nuint id,string? label);
    [DllImport("user32.dll")]private static extern bool DestroyMenu(nint menu);
    [DllImport("user32.dll")]private static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")]private static extern bool SetForegroundWindow(nint hwnd);
    [DllImport("user32.dll")]private static extern uint TrackPopupMenu(nint menu,uint flags,int x,int y,int reserved,nint hwnd,nint rect);
    [DllImport("user32.dll",EntryPoint="PostMessageW")]private static extern bool PostMessage(nint hwnd,uint message,nint w,nint l);
}
