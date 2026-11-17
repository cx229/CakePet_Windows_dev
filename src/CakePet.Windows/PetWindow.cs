using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using CakePet.Core;
using P= CakePet.Core.Point;
using Frame= CakePet.Core.Frame;
using WPoint=System.Windows.Point;

namespace CakePet.Windows;

internal sealed class PetWindow : Window
{
    private readonly RoleRuntime role;
    private readonly WpfImageCache images;
    private readonly PhysicsBody body;
    private readonly Canvas canvas=new(){Background=Brushes.Transparent};
    private readonly Image image=new(){Stretch=Stretch.Fill,IsHitTestVisible=false};
    private readonly TextBlock thoughtText=new(){FontSize=14,Foreground=Brushes.White,TextWrapping=TextWrapping.Wrap,TextAlignment=TextAlignment.Center};
    private readonly Window thoughtWindow;
    private readonly DispatcherTimer timer=new(DispatcherPriority.Render){Interval=TimeSpan.FromMilliseconds(16)};
    private readonly Stopwatch clock=Stopwatch.StartNew();
    private long lastTick,lastMouse;
    private WPoint pressedAt,previousMouse;
    private P pressedBody,velocity;
    private bool pressed,dragging,closed;
    private int facing=1;
    private Frame? rendered;
    private const double Side=160;
    public PetWindow(RoleRuntime role,WpfImageCache images,int index)
    {
        this.role=role;this.images=images;Title=role.Package.Name;
        Width=Height=Side;WindowStyle=WindowStyle.None;ResizeMode=ResizeMode.NoResize;AllowsTransparency=true;
        Background=Brushes.Transparent;Topmost=true;ShowInTaskbar=false;ShowActivated=false;
        canvas.Children.Add(image);Content=canvas;
        var b=CurrentBounds();body=new PhysicsBody(new P(Math.Clamp((b.Left+b.Right)/2+index*30,b.Left,b.Right),b.Bottom));
        body.SnapEnabled=role.Package.Supports("snap/pool");body.ThrowEnabled=role.Package.Supports("touch/roll");
        Left=body.Position.X;Top=body.Position.Y;
        thoughtWindow=new Window{Width=360,Height=44,WindowStyle=WindowStyle.None,ResizeMode=ResizeMode.NoResize,
            AllowsTransparency=true,Background=Brushes.Transparent,ShowInTaskbar=false,ShowActivated=false,Topmost=true,IsHitTestVisible=false};
        thoughtWindow.Content=new Border{Background=new SolidColorBrush(Color.FromArgb(175,35,35,35)),CornerRadius=new CornerRadius(8),
            Padding=new Thickness(8,3,8,3),VerticalAlignment=VerticalAlignment.Center,Child=thoughtText};
        thoughtWindow.SourceInitialized+=(_,_)=>NativeStyles.NonActivating(thoughtWindow,true);
        SourceInitialized+=(_,_)=>NativeStyles.NonActivating(this,false);
        role.FrameEntered+=ApplyMotion;
        var menu=new ContextMenu();
        Add("摸摸头",()=>role.Trigger(body.SnappedEdge==Edge.None?"pat_head/pat_head":"snap/pat_head/pat_head"));
        Add("菜单动画",()=>role.Trigger("menu/pool"));
        Add("归位",()=>{var bounds=CurrentBounds();body.Reset(new P((bounds.Left+bounds.Right)/2,bounds.Bottom),bounds);role.ResetToStandby();Render();});
        Add("切换静止待机",()=>role.SetStaticStandby(!role.StaticStandby));
        Add("召回",Close);
        menu.Opened+=(_,_)=>{((MenuItem)menu.Items[1]).IsEnabled=role.Package.Supports("menu/pool");((MenuItem)menu.Items[3]).IsEnabled=role.Package.Supports("standby/static_pool");};
        ContextMenu=menu;
        void Add(string title,Action action){var item=new MenuItem{Header=title};item.Click+=(_,_)=>action();menu.Items.Add(item);}
        MouseLeftButtonDown+=OnDown;MouseMove+=OnMove;MouseLeftButtonUp+=OnUp;
        LostMouseCapture+=(_,_)=>{if(pressed){pressed=false;if(dragging)FinishDrag();}};
        Loaded+=(_,_)=>{ApplyMotion(role.Animation.Current);Render();lastTick=clock.ElapsedMilliseconds;timer.Start();};
        timer.Tick+=(_,_)=>Tick();
        Closed+=(_,_)=>{closed=true;timer.Stop();role.FrameEntered-=ApplyMotion;thoughtWindow.Close();};
    }
    // Stage 2 deliberately constrains the host to the primary monitor's work area.
    // Later platform work replaces this with native monitor/DPI-aware bounds.
    private static Bounds CurrentBounds()
    {
        var r=SystemParameters.WorkArea;
        return new Bounds(r.Left,r.Top,Math.Max(r.Left,r.Right-Side),Math.Max(r.Top,r.Bottom-Side));
    }
    private WPoint Screen(MouseEventArgs e)
    {
        var screen=PointToScreen(e.GetPosition(this));
        return PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice.Transform(screen)??screen;
    }
    private void OnDown(object sender,MouseButtonEventArgs e)
    {
        pressed=true;dragging=false;pressedAt=previousMouse=Screen(e);pressedBody=body.Position;velocity=default;lastMouse=clock.ElapsedMilliseconds;
        CaptureMouse();e.Handled=true;
    }
    private void OnMove(object sender,MouseEventArgs e)
    {
        if(!pressed)return;
        var p=Screen(e);var dx=p.X-pressedAt.X;var dy=p.Y-pressedAt.Y;
        if(!dragging&&Math.Sqrt(dx*dx+dy*dy)<5)return;
        if(!dragging){dragging=true;body.Grab();role.SetEnvironment(false,PhysicalState.Dragging);}
        var now=clock.ElapsedMilliseconds;var dt=Math.Max(1,now-lastMouse)/1000.0;
        var instant=new P((p.X-previousMouse.X)/dt,(p.Y-previousMouse.Y)/dt);
        velocity=velocity*.35+instant*.65;previousMouse=p;lastMouse=now;
        body.DragTo(pressedBody+new P(dx,dy));role.UpdateDragCondition(velocity.X,Side);
        Render();
    }
    private void OnUp(object sender,MouseButtonEventArgs e)
    {
        if(!pressed)return;pressed=false;ReleaseMouseCapture();
        if(dragging)FinishDrag();
        else role.Trigger(body.SnappedEdge==Edge.None?"pat_head/pat_head":"snap/pat_head/pat_head");
        Render();e.Handled=true;
    }
    private void FinishDrag()
    {
        dragging=false;if(clock.ElapsedMilliseconds-lastMouse>120)velocity=default;
        body.Release(velocity,CurrentBounds());
        role.SetEnvironment(body.SnappedEdge!=Edge.None,body.State);Render();
    }
    private void ApplyMotion(Frame frame)
    {
        if(role.StaticStandby||body.SnappedEdge!=Edge.None)return;
        var scale=Side/role.Package.AnchorRes;
        if(body.MoveFrame(new P(frame.Dx*scale*facing,frame.Dy*scale),CurrentBounds())&&frame.Dx!=0)facing=-facing;
    }
    private void Tick()
    {
        if(closed)return;var now=clock.ElapsedMilliseconds;var dt=Math.Clamp(now-lastTick,0,100);lastTick=now;
        if(body.Advance(dt/1000.0,CurrentBounds()))role.CompletePhysicalMotion();
        role.Advance(dt);Render();
    }
    private void Render()
    {
        Left=body.Position.X;Top=body.Position.Y;
        var frame=role.Animation.Current;
        if(rendered!=frame)
        {
            var bitmap=images.Get(frame);
            // Bitmap has already been transformed. Recover the unrotated dimensions for the logical geometry.
            var (w,h)=frame.Transform is "rotate90" or "rotate270"?(bitmap.PixelHeight,bitmap.PixelWidth):(bitmap.PixelWidth,bitmap.PixelHeight);
            var g=FrameGeometry.Calculate(w,h,role.Package.AnchorRes,frame);var scale=Side/role.Package.AnchorRes;
            image.Source=bitmap;image.Width=g.Width*scale;image.Height=g.Height*scale;
            Canvas.SetLeft(image,g.Left*scale);Canvas.SetTop(image,g.Top*scale);rendered=frame;
        }
        var angle=body.SnappedEdge switch{Edge.Left=>90,Edge.Right=>-90,Edge.Top=>180,_=>0};
        if(canvas.RenderTransform is not TransformGroup group||((ScaleTransform)group.Children[0]).ScaleX!=facing||((RotateTransform)group.Children[1]).Angle!=angle)
        {
            canvas.RenderTransformOrigin=new WPoint(.5,.5);
            var transform=new TransformGroup();transform.Children.Add(new ScaleTransform(facing,1));transform.Children.Add(new RotateTransform(angle));canvas.RenderTransform=transform;
        }
        var text=role.Thoughts.VisibleText;
        if(string.IsNullOrWhiteSpace(text)){if(thoughtWindow.IsVisible)thoughtWindow.Hide();}
        else
        {
            thoughtText.Text=text;var r=SystemParameters.WorkArea;
            thoughtWindow.Left=r.Left+(r.Width-thoughtWindow.Width)/2;thoughtWindow.Top=r.Bottom-thoughtWindow.Height;
            if(!thoughtWindow.IsVisible)thoughtWindow.Show();
        }
    }
}

internal static class NativeStyles
{
    private const int GwlExStyle=-20;
    private const long WsExNoActivate=0x08000000,WsExToolWindow=0x80,WsExTransparent=0x20;
    [DllImport("user32.dll",EntryPoint="GetWindowLongPtrW")]private static extern nint GetWindowLongPtr(nint hwnd,int index);
    [DllImport("user32.dll",EntryPoint="SetWindowLongPtrW")]private static extern nint SetWindowLongPtr(nint hwnd,int index,nint value);
    public static void NonActivating(Window window,bool clickThrough)
    {
        var handle=new WindowInteropHelper(window).Handle;
        var old=GetWindowLongPtr(handle,GwlExStyle).ToInt64();
        SetWindowLongPtr(handle,GwlExStyle,(nint)(old|WsExNoActivate|WsExToolWindow|(clickThrough?WsExTransparent:0)));
    }
}
