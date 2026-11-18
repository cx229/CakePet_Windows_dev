using System.ComponentModel;
using System.Runtime.InteropServices;
using CakePet.Core;

namespace CakePet.Windows;

// One native HWND per sprite. WPF is not involved in this surface.
internal sealed class NativeLayer:IDisposable
{
    private static readonly string ClassName="CakePet.Layer."+Environment.ProcessId;
    private static readonly WndProc Procedure=OnMessage;
    private static readonly nint Module=GetModuleHandle(null);
    private static bool registered;
    private static readonly Dictionary<nint,NativeLayer> windows=new();
    private nint hwnd,dc,dib,old,bits;
    private int width,height,x=int.MinValue,y=int.MinValue;
    private byte opacity=255;
    private bool pass,closed;
    public event Action? Down,Move,Up,Lost,Menu,Closed;
    public event Action<nint>? RawInput;
    public nint Handle=>hwnd;
    public bool IsCaptured=>GetCapture()==hwnd&&hwnd!=0;
    public double Dpi=>hwnd==0?1:Math.Max(1,GetDpiForWindow(hwnd))/96.0;
    public NativeLayer()
    {
        if(!registered)
        {
            var cls=new WindowClass{Size=(uint)Marshal.SizeOf<WindowClass>(),Instance=Module,Procedure=Marshal.GetFunctionPointerForDelegate(Procedure),ClassName=ClassName,Cursor=LoadCursor(0,(nint)32512)};
            if(RegisterClassEx(ref cls)==0)throw new Win32Exception(Marshal.GetLastWin32Error());
            registered=true;
        }
        hwnd=CreateWindowEx(0x80000|0x08000000|0x80|8,ClassName,"CakePet",0x80000000,0,0,1,1,0,0,Module,0);
        if(hwnd==0)throw new Win32Exception(Marshal.GetLastWin32Error());
        windows.Add(hwnd,this);
    }
    private static nint OnMessage(nint h,uint msg,nint w,nint l)
    {
        if(!windows.TryGetValue(h,out var layer))return DefWindowProc(h,msg,w,l);
        try
        {
            switch(msg)
            {
                case 0x00FF:layer.RawInput?.Invoke(l);break;
                case 0x21:return 3;
                case 0x14:return 1;
                case 0x10:layer.Dispose();return 0;
                case 0x201:layer.Down?.Invoke();return 0;
                case 0x200:layer.Move?.Invoke();return 0;
                case 0x202:layer.Up?.Invoke();return 0;
                case 0x215:layer.Lost?.Invoke();return 0;
                case 0x205:layer.Menu?.Invoke();return 0;
            }
        }
        catch(Exception ex){NativeMessage.Show(ex.Message,"CakePet");}
        return DefWindowProc(h,msg,w,l);
    }
    public void Show(){if(hwnd!=0)ShowWindow(hwnd,4);}
    public void Hide(){if(hwnd!=0)ShowWindow(hwnd,0);}
    public bool Capture(){if(hwnd==0)return false;SetCapture(hwnd);return IsCaptured;}
    public void Release(){if(IsCaptured)ReleaseCapture();}
    public void Pass(bool value)
    {
        if(hwnd==0||pass==value)return;pass=value;
        var style=GetWindowLongPtr(hwnd,-20).ToInt64();
        SetWindowLongPtr(hwnd,-20,(nint)(value?style|0x20:style&~0x20));
    }
    private void Ensure(int w,int h)
    {
        if(width==w&&height==h&&dc!=0)return;
        FreeSurface();
        dc=CreateCompatibleDC(0);
        if(dc==0)throw new Win32Exception(Marshal.GetLastWin32Error());
        var info=new BitmapInfo{Size=40,Width=w,Height=-h,Planes=1,BitCount=32};
        dib=CreateDIBSection(dc,ref info,0,out bits,0,0);
        if(dib==0||bits==0){FreeSurface();throw new Win32Exception(Marshal.GetLastWin32Error());}
        old=SelectObject(dc,dib);
        if(old==0||old==(nint)(-1)){FreeSurface();throw new Win32Exception(Marshal.GetLastWin32Error());}
        width=w;height=h;
    }
    public void Paint(byte[] pixels,int w,int h,double left,double top,double alpha)
    {
        if(closed)return;
        if(w<1||h<1||pixels.Length!=checked(w*h*4))throw new ArgumentException("Invalid pixel surface.");
        Ensure(w,h);Marshal.Copy(pixels,0,bits,pixels.Length);
        opacity=(byte)Math.Round(Math.Clamp(alpha,0,1)*255);
        x=(int)Math.Round(left);y=(int)Math.Round(top);Upload();
    }
    private void Upload()
    {
        if(hwnd==0||dc==0)return;
        var destination=new Point{X=x,Y=y};var source=new Point();var size=new Size{Width=width,Height=height};var blend=new Blend{Alpha=opacity,Format=1};
        if(!UpdateLayeredWindow(hwnd,0,ref destination,ref size,dc,ref source,0,ref blend,2))throw new Win32Exception(Marshal.GetLastWin32Error());
    }
    public void Place(double left,double top,double alpha)
    {
        if(closed||dc==0)return;
        var nx=(int)Math.Round(left);var ny=(int)Math.Round(top);var a=(byte)Math.Round(Math.Clamp(alpha,0,1)*255);
        if(a!=opacity){opacity=a;x=nx;y=ny;Upload();return;}
        if(nx==x&&ny==y)return;
        if(SetWindowPos(hwnd,0,nx,ny,0,0,0x10|0x4|0x200|1)){x=nx;y=ny;}
    }
    private void FreeSurface()
    {
        if(dc!=0&&old!=0&&old!=(nint)(-1))SelectObject(dc,old);
        if(dib!=0)DeleteObject(dib);
        if(dc!=0)DeleteDC(dc);
        dc=dib=old=bits=0;width=height=0;
    }
    public void Dispose()
    {
        if(closed)return;closed=true;
        if(hwnd!=0){var h=hwnd;hwnd=0;windows.Remove(h);DestroyWindow(h);}
        FreeSurface();Closed?.Invoke();
    }
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]private struct WindowClass
    {
        public uint Size,Style;public nint Procedure;public int ClassExtra,WindowExtra;public nint Instance,Icon,Cursor,Background;
        [MarshalAs(UnmanagedType.LPWStr)]public string? MenuName;
        [MarshalAs(UnmanagedType.LPWStr)]public string ClassName;public nint SmallIcon;
    }
    [StructLayout(LayoutKind.Sequential)]private struct Point{public int X,Y;}
    [StructLayout(LayoutKind.Sequential)]private struct Size{public int Width,Height;}
    [StructLayout(LayoutKind.Sequential,Pack=1)]private struct Blend{public byte Operation,Flags,Alpha,Format;}
    [StructLayout(LayoutKind.Sequential)]private struct BitmapInfo
    {
        public uint Size;public int Width,Height;public ushort Planes,BitCount;
        public uint Compression,ImageSize;public int XResolution,YResolution;public uint Used,Important,Color;
    }
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]private delegate nint WndProc(nint h,uint msg,nint w,nint l);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode)]private static extern nint GetModuleHandle(string? name);
    [DllImport("user32.dll",CharSet=CharSet.Unicode,SetLastError=true)]private static extern ushort RegisterClassEx(ref WindowClass cls);
    [DllImport("user32.dll",CharSet=CharSet.Unicode,SetLastError=true)]private static extern nint CreateWindowEx(uint ex,string cls,string title,uint style,int x,int y,int w,int h,nint parent,nint menu,nint module,nint param);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]private static extern nint DefWindowProc(nint h,uint msg,nint w,nint l);
    [DllImport("user32.dll",EntryPoint="LoadCursorW")]private static extern nint LoadCursor(nint instance,nint name);
    [DllImport("user32.dll")]private static extern bool ShowWindow(nint h,int command);
    [DllImport("user32.dll")]private static extern bool DestroyWindow(nint h);
    [DllImport("user32.dll")]private static extern uint GetDpiForWindow(nint h);
    [DllImport("user32.dll")]private static extern nint SetCapture(nint h);
    [DllImport("user32.dll")]private static extern nint GetCapture();
    [DllImport("user32.dll")]private static extern bool ReleaseCapture();
    [DllImport("user32.dll",EntryPoint="GetWindowLongPtrW")]private static extern nint GetWindowLongPtr(nint h,int index);
    [DllImport("user32.dll",EntryPoint="SetWindowLongPtrW")]private static extern nint SetWindowLongPtr(nint h,int index,nint value);
    [DllImport("user32.dll",SetLastError=true)]private static extern bool UpdateLayeredWindow(nint h,nint destination,ref Point pos,ref Size size,nint source,ref Point origin,uint color,ref Blend blend,uint flags);
    [DllImport("user32.dll")]private static extern bool SetWindowPos(nint h,nint after,int x,int y,int w,int height,uint flags);
    [DllImport("gdi32.dll",SetLastError=true)]private static extern nint CreateCompatibleDC(nint dc);
    [DllImport("gdi32.dll",SetLastError=true)]private static extern nint CreateDIBSection(nint dc,ref BitmapInfo info,uint usage,out nint bits,nint section,uint offset);
    [DllImport("gdi32.dll")]private static extern nint SelectObject(nint dc,nint obj);
    [DllImport("gdi32.dll")]private static extern bool DeleteObject(nint obj);
    [DllImport("gdi32.dll")]private static extern bool DeleteDC(nint dc);
}

