using System.Runtime.InteropServices;
using CakePet.Core;
namespace CakePet.Windows;

// Native, draggable diagnostic window. The text stays readable and can be selected.
internal sealed class NativeDebugWindow
{
    private nint hwnd,edit;
    private static readonly Dictionary<nint,NativeDebugWindow> windows=new();
    private static readonly WndProc Procedure=Dispatch;
    private static readonly nint module=GetModuleHandle(null);
    private static bool registered;
    private readonly string title;
    public event EventHandler? Closed;
    public NativeDebugWindow(string name)
    {
        title=name+" · 实时调试";
        if(!registered)
        {
            var cls=new WindowClass{Size=(uint)Marshal.SizeOf<WindowClass>(),Procedure=Marshal.GetFunctionPointerForDelegate(Procedure),Instance=module,ClassName="CakePet.NativeDebug",Cursor=LoadCursor(0,(nint)32512),Background=(nint)6};
            if(RegisterClassEx(ref cls)==0)throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            registered=true;
        }
        hwnd=CreateWindowEx(0x80|8,"CakePet.NativeDebug",title,0x00CF0000,100,100,530,590,0,0,module,0);
        if(hwnd==0)throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        windows.Add(hwnd,this);
        edit=CreateWindowEx(0x200,"EDIT","",0x50000000|0x0004|0x0800|0x0040|0x00200000,0,0,500,550,hwnd,0,module,0);
        if(edit==0){Close();throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());}
        SendMessage(edit,0x30,GetStockObject(17),1);
        Resize();
    }
    private void Resize(){if(edit!=0&&GetClientRect(hwnd,out var r))MoveWindow(edit,0,0,r.Right,r.Bottom,true);}
    private static nint Dispatch(nint h,uint msg,nint w,nint l)
    {
        if(windows.TryGetValue(h,out var instance))
        {
            if(msg==5)instance.Resize();
            if(msg==0x10){instance.Close();return 0;}
            if(msg==2){windows.Remove(h);instance.hwnd=0;instance.Closed?.Invoke(instance,EventArgs.Empty);}
        }
        return DefWindowProc(h,msg,w,l);
    }
    public void Show(){if(hwnd!=0)ShowWindow(hwnd,5);}
    public void Close(){if(hwnd!=0)DestroyWindow(hwnd);}
    public void Refresh(PetEngine e,string monitor,double dpi,int foodCount,bool pass,bool pixel,Guid instanceId,int number,double baseScale,double actualScale,int count)
    {
        if(edit!=0)SetWindowText(edit,Format(e,monitor,dpi,foodCount,pass,pixel,instanceId,number,baseScale,actualScale,count).Replace("\n","\r\n"));
    }
    private static string Format(PetEngine e,string monitor,double dpi,int foodCount,bool pass,bool pixel,Guid instanceId,int number,double baseScale,double actualScale,int count)
    {
        var r=e.Role;var a=r.Animation;var b=e.Body;var f=a.Current;var p=b.Position;var v=b.Velocity;var box=e.Bounds;var anchor=e.AnchorOffset;
        return $"实例：{number}/{count} · {instanceId.ToString("N")[..8]} · 基础 {baseScale:F2} / 当前 {actualScale:F2} 倍\n"+
            $"素材包：{r.Package.Name} · {r.Package.Version}\n"+
            $"功能：{r.FunctionPath}\n持续功能：{r.StateFunction??"—"}\n"+
            $"动画链：[{string.Join(" > ",a.Chain)}]\n"+
            $"当前动画：{a.Mode.Id} · {a.FrameIndex+1}/{a.Mode.Frames.Count} 帧\n"+
            $"播放：{a.Mode.Loop} · 帧 {a.FrameElapsed}/{a.FrameDuration}ms · {(a.Ended?"已结束":"播放中")}\n"+
            $"动画计时：{a.ModeElapsed}ms / {a.TargetDuration?.ToString()??"—"} · 后续 {a.Mode.Next??"—"}\n"+
            $"图片：{f.Resource}\n帧参数：dx={f.Dx}, dy={f.Dy}, 锚点={f.AnchorX},{f.AnchorY} · {f.Align}/{f.Transform}\n"+
            $"物理：{b.State} / {r.Physics} · 吸附 {b.SnappedEdge} · 攀爬 {b.ClimbEdge}\n"+
             $"飞跃：起跳 {b.LeapLaunchEdges} {e.FlightLaunchAnchor?.ToString()??"—"}\n"+
            $"飞跃目标：{b.LeapTargetEdge} {b.LeapTarget?.ToString()??"—"} · 碰撞 {e.LastFlightImpact}\n"+
            $"位置：({p.X:F1}, {p.Y:F1}) · 速度 ({v.X:F1}, {v.Y:F1}) px/s\n"+
            $"锚点：({p.X+anchor.X:F1}, {p.Y+anchor.Y:F1}) · 大小 {e.DisplaySize:F1}px\n"+
            $"边界：({box.Left:F0}, {box.Top:F0}) — ({box.Right:F0}, {box.Bottom:F0})\n"+
            $"重力：{b.GravitySurface} · 朝向 {e.RenderEdge} · 镜像 {e.Facing}\n"+
            $"屏幕：{monitor} · DPI {dpi*96:F0} · 穿透 {pass} · 像素命中 {pixel}\n"+
            $"食物：{foodCount} · 觅食 {e.Foraging} · 吸引 {e.IsLuring}\n"+
            $"碎碎念：{r.CurrentThinking.Policy} · {r.Thoughts.CurrentId??"—"} · 隐藏 {r.Thoughts.Suppressed}";
    }
    [StructLayout(LayoutKind.Sequential)]private struct Rect{public int Left,Top,Right,Bottom;}
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]private struct WindowClass
    {public uint Size,Style;public nint Procedure;public int ClassExtra,WindowExtra;public nint Instance,Icon,Cursor,Background;[MarshalAs(UnmanagedType.LPWStr)]public string? MenuName;[MarshalAs(UnmanagedType.LPWStr)]public string ClassName;public nint SmallIcon;}
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]private delegate nint WndProc(nint h,uint msg,nint w,nint l);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode)]private static extern nint GetModuleHandle(string? name);
    [DllImport("user32.dll",CharSet=CharSet.Unicode,SetLastError=true)]private static extern ushort RegisterClassEx(ref WindowClass cls);
    [DllImport("user32.dll",CharSet=CharSet.Unicode,SetLastError=true)]private static extern nint CreateWindowEx(uint ex,string cls,string title,uint style,int x,int y,int w,int h,nint parent,nint menu,nint module,nint param);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]private static extern nint DefWindowProc(nint h,uint msg,nint w,nint l);
    [DllImport("user32.dll",EntryPoint="LoadCursorW")]private static extern nint LoadCursor(nint h,nint id);
    [DllImport("user32.dll")]private static extern bool DestroyWindow(nint h);
    [DllImport("user32.dll")]private static extern bool ShowWindow(nint h,int cmd);
    [DllImport("user32.dll")]private static extern bool GetClientRect(nint h,out Rect r);
    [DllImport("user32.dll")]private static extern bool MoveWindow(nint h,int x,int y,int w,int height,bool repaint);
    [DllImport("user32.dll",EntryPoint="SetWindowTextW",CharSet=CharSet.Unicode)]private static extern bool SetWindowText(nint h,string text);
    [DllImport("user32.dll",EntryPoint="SendMessageW")]private static extern nint SendMessage(nint h,uint msg,nint w,nint l);
    [DllImport("gdi32.dll")]private static extern nint GetStockObject(int id);
}
