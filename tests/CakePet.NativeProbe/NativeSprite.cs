using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using CakePet.Core;
using P=CakePet.Core.Point;

namespace CakePet.NativeProbe;

// Isolated experiment, not a replacement for the production WPF host.
internal sealed class NativeSprite : IDisposable
{
    private readonly GdiImageCache images;
    private readonly GdiSurface surface;
    private readonly RoleRuntime role;
    private readonly PetEngine engine;
    private readonly int side;
    private readonly string className="CakePet.NativeProbe."+Environment.ProcessId;
    private readonly WndProc procedure;
    private readonly nint module=GetModuleHandle(null);
    private nint hwnd;
    private bool classRegistered,disposed,pressed,dragging;
    private P pressedAt,previousMouse,pressedBody,velocity;
    private long previousTime;
    private readonly Stopwatch clock=Stopwatch.StartNew();
    private Frame? rendered;
    private int renderedFacing,placedX=int.MinValue,placedY=int.MinValue;
    private Edge renderedEdge;
    public Exception? Failure {get;private set;}
    public long CachedBytes=>images.CachedBytes;
    public NativeSprite(string root)
    {
        var pack=RolePackage.Load(Path.GetFullPath(root));
        side=Math.Max(1,(int)Math.Round(pack.AnchorRes*.5));
        images=new GdiImageCache(pack);
        try{surface=new GdiSurface(side);}catch{images.Dispose();throw;}
        try
        {
            role=new RoleRuntime(pack,new Random(1234));role.Thoughts.Enabled=false;
            var area=WorkArea();
            var limits=DesktopGeometry.Fit(area,side,side);
            engine=new PetEngine(role,limits,side,new P((limits.Left+limits.Right)/2,limits.Bottom),new Random(1234));
            engine.SetActivityArea(area,images.Geometry);engine.SetDesktopSpace(new DesktopSpace(new[]{area}),false);
            engine.FeedingEnabled=false;engine.GrowOnFeed=false;engine.SetAutoForage(false);engine.SetLeapEnabled(false);engine.SetClimbing(true);engine.ReturnHome();
            procedure=OnMessage;
            var cls=new WindowClass{Size=(uint)Marshal.SizeOf<WindowClass>(),Instance=module,Procedure=Marshal.GetFunctionPointerForDelegate(procedure),ClassName=className,Cursor=LoadCursor(0,(nint)32512)};
            if(RegisterClassEx(ref cls)==0)throw new Win32Exception(Marshal.GetLastWin32Error());
            classRegistered=true;
            hwnd=CreateWindowEx(0x00080000|0x08000000|0x80|8,className,"CakePet native probe",0x80000000,0,0,side,side,0,0,module,0);
            if(hwnd==0)throw new Win32Exception(Marshal.GetLastWin32Error());
            Render();ShowWindow(hwnd,4);
        }
        catch{Dispose();throw;}
    }
    private static Bounds WorkArea()
    {
        if(SystemParametersInfo(0x30,0,out var r,0)&&r.Right>r.Left&&r.Bottom>r.Top)return new Bounds(r.Left,r.Top,r.Right,r.Bottom);
        return new Bounds(0,0,Math.Max(1,GetSystemMetrics(0)),Math.Max(1,GetSystemMetrics(1)));
    }
    public void Step(long dt){engine.Step(dt);Render();}
    public void ReturnHome(){engine.ReturnHome();Render();}
    private void Render()
    {
        if(hwnd==0)return;
        var frame=role.Animation.Current;
        var edge=engine.RenderEdge;var facing=engine.Facing;
        int x=(int)Math.Round(engine.Body.Position.X),y=(int)Math.Round(engine.Body.Position.Y);
        if(rendered!=frame||renderedFacing!=facing||renderedEdge!=edge)
        {
            var image=images.Get(frame);
            surface.Draw(image.Handle,images.Geometry(frame),role.Package.AnchorRes,facing,edge);
            var dest=new Point{X=x,Y=y};var src=new Point();var size=new Size{Width=side,Height=side};var blend=new Blend{Alpha=255,Format=1};
            if(!UpdateLayeredWindow(hwnd,0,ref dest,ref size,surface.Dc,ref src,0,ref blend,2))throw new Win32Exception(Marshal.GetLastWin32Error());
            rendered=frame;renderedFacing=facing;renderedEdge=edge;placedX=x;placedY=y;
        }
        else if(x!=placedX||y!=placedY)
        {
            if(!SetWindowPos(hwnd,0,x,y,0,0,0x0010|0x0004|0x0200|1))throw new Win32Exception(Marshal.GetLastWin32Error());
            placedX=x;placedY=y;
        }
    }
    private static P Cursor(){GetCursorPos(out var p);return new P(p.X,p.Y);}
    private void EndPress(bool cancelled)
    {
        if(!pressed)return;pressed=false;
        if(dragging){dragging=false;engine.EndDrag(cancelled||clock.ElapsedMilliseconds-previousTime>120?default:velocity,Cursor());}
        else if(!cancelled)engine.PatHead();
        Render();
    }
    private nint OnMessage(nint window,uint message,nint w,nint l)
    {
        try
        {
            switch(message)
            {
                case 0x21:return 3; // MA_NOACTIVATE
                case 0x14:return 1;
                case 0x10:Program.PostQuitMessage(0);return 0;
                case 0x0201:
                    pressed=true;dragging=false;pressedAt=previousMouse=Cursor();pressedBody=engine.Body.Position;velocity=default;previousTime=clock.ElapsedMilliseconds;
                    SetCapture(window);return 0;
                case 0x0200:
                    if(!pressed)break;
                    var p=Cursor();var delta=p-pressedAt;
                    if(!dragging&&delta.Length<5)return 0;
                    if(!dragging){dragging=true;engine.BeginDrag();}
                    var now=clock.ElapsedMilliseconds;var dt=Math.Max(1,now-previousTime)/1000.0;
                    velocity=velocity*.35+(p-previousMouse)*(1/dt)*.65;previousMouse=p;previousTime=now;
                    engine.DragTo(pressedBody+delta,velocity.X);Render();return 0;
                case 0x0202:EndPress(false);ReleaseCapture();return 0;
                case 0x0215:EndPress(true);return 0;
            }
            return DefWindowProc(window,message,w,l);
        }
        catch(Exception ex){Failure=ex;Program.PostQuitMessage(1);return 0;}
    }
    public void Dispose()
    {
        if(disposed)return;disposed=true;
        if(hwnd!=0){DestroyWindow(hwnd);hwnd=0;}
        if(classRegistered)UnregisterClass(className,module);
        engine?.Dispose();surface.Dispose();images.Dispose();
    }
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]private struct WindowClass
    {
        public uint Size,Style;public nint Procedure;public int ClassExtra,WindowExtra;public nint Instance,Icon,Cursor,Background;
        [MarshalAs(UnmanagedType.LPWStr)]public string? MenuName;
        [MarshalAs(UnmanagedType.LPWStr)]public string ClassName;public nint SmallIcon;
    }
    [StructLayout(LayoutKind.Sequential)]private struct Point{public int X,Y;}
    [StructLayout(LayoutKind.Sequential)]private struct Size{public int Width,Height;}
    [StructLayout(LayoutKind.Sequential)]private struct Rect{public int Left,Top,Right,Bottom;}
    [StructLayout(LayoutKind.Sequential,Pack=1)]private struct Blend{public byte Operation,Flags,Alpha,Format;}
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]private delegate nint WndProc(nint window,uint msg,nint w,nint l);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode)]private static extern nint GetModuleHandle(string? name);
    [DllImport("user32.dll",CharSet=CharSet.Unicode,SetLastError=true)]private static extern ushort RegisterClassEx(ref WindowClass cls);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]private static extern bool UnregisterClass(string name,nint instance);
    [DllImport("user32.dll",CharSet=CharSet.Unicode,SetLastError=true)]private static extern nint CreateWindowEx(uint ex,string cls,string title,uint style,int x,int y,int width,int height,nint parent,nint menu,nint instance,nint param);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]private static extern nint DefWindowProc(nint hwnd,uint msg,nint w,nint l);
    [DllImport("user32.dll")]private static extern bool DestroyWindow(nint hwnd);
    [DllImport("user32.dll")]private static extern bool ShowWindow(nint hwnd,int command);
    [DllImport("user32.dll",SetLastError=true)]private static extern bool SetWindowPos(nint hwnd,nint after,int x,int y,int w,int h,uint flags);
    [DllImport("user32.dll",SetLastError=true)]private static extern bool UpdateLayeredWindow(nint hwnd,nint destination,ref Point position,ref Size size,nint source,ref Point origin,uint color,ref Blend blend,uint flags);
    [DllImport("user32.dll",EntryPoint="SystemParametersInfoW")]private static extern bool SystemParametersInfo(uint action,uint parameter,out Rect rect,uint flags);
    [DllImport("user32.dll")]private static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll",EntryPoint="LoadCursorW")]private static extern nint LoadCursor(nint instance,nint name);
    [DllImport("user32.dll")]private static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")]private static extern nint SetCapture(nint hwnd);
    [DllImport("user32.dll")]private static extern bool ReleaseCapture();
}
