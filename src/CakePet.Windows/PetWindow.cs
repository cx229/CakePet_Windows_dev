using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CakePet.Core;
using P= CakePet.Core.Point;
using Frame= CakePet.Core.Frame;
using WPoint=System.Windows.Point;

namespace CakePet.Windows;

internal sealed class PetWindow : Window
{
    private readonly PetEngine engine;
    private RoleRuntime role=>engine.Role;
    private PhysicsBody body=>engine.Body;
    private readonly WpfImageCache images;
    private readonly Canvas canvas=new(){Background=Brushes.Transparent};
    private readonly Image image=new(){Stretch=Stretch.Fill,IsHitTestVisible=false};
    private readonly TextBlock thoughtText=new(){FontSize=14,Foreground=Brushes.White,TextWrapping=TextWrapping.Wrap,TextAlignment=TextAlignment.Center};
    private readonly Window thoughtWindow;
    private readonly List<FreezeWindow> freezes=new();
    private DebugWindow? debug;
    private long debugTick;
    private readonly FoodWorld foodWorld;
    private readonly Stopwatch clock=Stopwatch.StartNew();
    private long lastMouse;
    private WPoint pressedAt,previousMouse;
    private P pressedBody,velocity;
    private bool pressed,dragging,closed;
    private Frame? rendered;
    private double Side=>engine.DisplaySize;
    internal PetEngine Engine=>engine;
    internal WpfImageCache Images=>images;
    private double renderedSize;
    private string monitorId;
    private bool fullScreen,clickThrough,pixelHit=true;
    private bool HitAt(P pointer)
    {
        var local=AnchorGeometry.Inverse(pointer-body.Position,Side,engine.RenderEdge,engine.Facing);
        var g=images.Geometry(role.Animation.Current);var scale=Side/role.Package.AnchorRes;
        return images.OpaqueAt(role.Animation.Current,(local.X/scale-g.Left)/g.Width,(local.Y/scale-g.Top)/g.Height);
    }
    private DesktopScreen Monitor=>DesktopPlatform.Resolve(monitorId,engine.Center);
    internal Bounds ActivityArea=>fullScreen?Monitor.Full:Monitor.Work;
    internal bool ClickThrough {get=>clickThrough;set{clickThrough=value;ApplyInputStyle();}}
    private void ApplyInputStyle()
    {
        var control=DesktopPlatform.ControlDown;
        var pass=!pressed&&ContextMenu?.IsOpen!=true&&!control&&(clickThrough||(pixelHit&&!HitAt(DesktopPlatform.Cursor())));
        NativeStyles.NonActivating(this,pass);
    }
    public PetWindow(RoleRuntime role,WpfImageCache images,int index,FoodWorld world)
    {
        this.images=images;foodWorld=world;Title=role.Package.Name;
        Width=Height=160;WindowStyle=WindowStyle.None;ResizeMode=ResizeMode.NoResize;AllowsTransparency=true;
        Background=Brushes.Transparent;Topmost=true;ShowInTaskbar=false;ShowActivated=false;
        canvas.Children.Add(image);Content=canvas;
        var monitor=DesktopPlatform.Primary;monitorId=monitor.Id;
        var b=DesktopGeometry.Fit(monitor.Work,160,160);engine=new PetEngine(role,b,160,new P(Math.Clamp((b.Left+b.Right)/2+index*30,b.Left,b.Right),b.Bottom));
        engine.SetActivityArea(monitor.Work,images.Geometry);
        engine.ReturnHome();
        body.SetPosition(new P(Math.Clamp(body.Position.X+index*30,engine.Bounds.Left,engine.Bounds.Right),body.Position.Y));
        
        thoughtWindow=new Window{Width=360,Height=44,WindowStyle=WindowStyle.None,ResizeMode=ResizeMode.NoResize,
            AllowsTransparency=true,Background=Brushes.Transparent,ShowInTaskbar=false,ShowActivated=false,Topmost=true,IsHitTestVisible=false};
        thoughtWindow.Content=new Border{Background=new SolidColorBrush(Color.FromArgb(175,35,35,35)),CornerRadius=new CornerRadius(8),
            Padding=new Thickness(8,3,8,3),VerticalAlignment=VerticalAlignment.Center,Child=thoughtText};
        thoughtWindow.SourceInitialized+=(_,_)=>NativeStyles.NonActivating(thoughtWindow,true);
        SourceInitialized+=(_,_)=>{ApplyInputStyle();DesktopPlatform.Place(this,body.Position.X,body.Position.Y,Side,Side);};
        var menu=new ContextMenu();
        Add("摸摸头",()=>engine.PatHead());
        var menuAction=Add("菜单动画",()=>engine.MenuAction());
        Add("归位",()=>{engine.ReturnHome();Render();});
        var still=Add("静止待机",()=>engine.SetStaticStandby(!role.StaticStandby));still.IsCheckable=true;
        var climb=Add("攀岩走壁",()=>engine.SetClimbing(!engine.ClimbEnabled));climb.IsCheckable=true;
        var leap=Add("好动飞跃",()=>engine.SetLeapEnabled(!engine.LeapEnabled));leap.IsCheckable=true;
        var leapTest=Add("立即飞跃（测试）",()=>engine.TryStartLeap());
        menu.Items.Add(new Separator());
        FoodInstance? Spawn()=>world.Spawn(engine,ActivityArea,48,60000,true,3,id=>
        {
            var v=images.GetFoodVisual(id);return new FoodExtent(v.LogicalWidth*1.4,v.LogicalHeight*1.4);
        });
        var feed=Add("投食",()=>{Spawn();world.ResetSpawnTimer(engine);});
        var automatic=Add("自动刷新食物",()=>world.SetAutomatic(engine,!world.IsAutomatic(engine),new Interval(30000,120000),Spawn));automatic.IsCheckable=true;
        var forage=Add("自动觅食",()=>engine.SetAutoForage(!engine.AutoForageEnabled));forage.IsCheckable=true;
        var grow=Add("投喂变大",()=>engine.GrowOnFeed=!engine.GrowOnFeed);grow.IsCheckable=true;
        Add("清除本角色食物",()=>world.Clear(engine));
        Add("恢复大小",()=>{engine.Resize(160);Render();});
        var screen=Add("全屏活动范围（含任务栏）",()=>{fullScreen=!fullScreen;engine.SetActivityArea(ActivityArea,images.Geometry);});screen.IsCheckable=true;
        var pixel=Add("像素级命中",()=>{pixelHit=!pixelHit;ApplyInputStyle();});pixel.IsCheckable=true;
        var through=Add("点击穿透（Ctrl 临时恢复）",()=>ClickThrough=!ClickThrough);through.IsCheckable=true;
        Add("定格当前画面",Freeze);
        Add("清除本角色定格",ClearFreezes);
        Add("实时调试窗口",ToggleDebug);
        Add("召回",Close);
        menu.Opened+=(_,_)=>{
            menuAction.IsEnabled=role.Package.Supports("menu/pool");still.IsEnabled=role.Package.Supports("standby/static_pool");still.IsChecked=role.StaticStandby;
            climb.IsChecked=engine.ClimbEnabled;leap.IsChecked=engine.LeapEnabled;leap.IsEnabled=role.Package.Supports("climb/leap/leap");
            leapTest.IsEnabled=engine.ClimbEnabled&&engine.LeapEnabled;
            feed.IsEnabled=automatic.IsEnabled=role.Package.Supports("feed/food");automatic.IsChecked=world.IsAutomatic(engine);
            forage.IsEnabled=engine.ClimbEnabled&&role.Package.Supports(PetEngine.ForagePath);forage.IsChecked=engine.AutoForageEnabled;
            grow.IsChecked=engine.GrowOnFeed;screen.IsChecked=fullScreen;through.IsChecked=ClickThrough;pixel.IsChecked=pixelHit;
        };
        ContextMenu=menu;
        MenuItem Add(string title,Action action){var item=new MenuItem{Header=title};item.Click+=(_,_)=>action();menu.Items.Add(item);return item;}
        MouseLeftButtonDown+=OnDown;MouseMove+=OnMove;MouseLeftButtonUp+=OnUp;
        LostMouseCapture+=(_,_)=>{if(pressed){pressed=false;if(dragging){velocity=default;FinishDrag();}}};
        engine.GrowOnFeed=true;engine.FeedGrowth=role.Package.AnchorRes*.03;engine.MaxDisplaySize=role.Package.AnchorRes*3;
        Loaded+=(_,_)=>Render();
        Closed+=(_,_)=>{closed=true;ClearFreezes();debug?.Close();engine.Dispose();thoughtWindow.Close();};
    }
    private WPoint Screen(MouseEventArgs e){var p=DesktopPlatform.Cursor();return new WPoint(p.X,p.Y);}
    private void OnDown(object sender,MouseButtonEventArgs e)
    {
        pressed=true;dragging=false;pressedAt=previousMouse=Screen(e);pressedBody=body.Position;velocity=default;lastMouse=clock.ElapsedMilliseconds;
        if(!CaptureMouse())pressed=false;e.Handled=true;
    }
    private void OnMove(object sender,MouseEventArgs e)
    {
        if(!pressed)return;
        var p=Screen(e);var dx=p.X-pressedAt.X;var dy=p.Y-pressedAt.Y;
        if(!dragging&&Math.Sqrt(dx*dx+dy*dy)<5)return;
        if(!dragging){dragging=true;engine.BeginDrag();}
        var now=clock.ElapsedMilliseconds;var dt=Math.Max(1,now-lastMouse)/1000.0;
        var instant=new P((p.X-previousMouse.X)/dt,(p.Y-previousMouse.Y)/dt);
        velocity=velocity*.35+instant*.65;previousMouse=p;lastMouse=now;
        monitorId=DesktopPlatform.At(new P(p.X,p.Y)).Id;
        engine.DragTo(pressedBody+new P(dx,dy),velocity.X);
        Render();
    }
    private void OnUp(object sender,MouseButtonEventArgs e)
    {
        if(!pressed)return;pressed=false;ReleaseMouseCapture();
        if(dragging)FinishDrag();
        else engine.PatHead();
        Render();e.Handled=true;
    }
    private void FinishDrag()
    {
        dragging=false;if(clock.ElapsedMilliseconds-lastMouse>120)velocity=default;
        engine.SetActivityArea(ActivityArea,images.Geometry);engine.EndDrag(velocity,DesktopPlatform.Cursor());Render();
    }
    internal void ClearFreezes(){foreach(var freeze in freezes.ToArray())freeze.Close();}
    private void Freeze()
    {
        Render();var dip=Side/DesktopPlatform.DpiScale(this);var pixels=Math.Max(1,(int)Math.Round(Side));
        var visual=new DrawingVisual();using(var dc=visual.RenderOpen())
        {
            var frame=role.Animation.Current;var g=images.Geometry(frame);var scale=Side/role.Package.AnchorRes;
            dc.PushTransform(new TranslateTransform(Side/2,Side/2));
            dc.PushTransform(new RotateTransform(engine.RenderEdge switch{Edge.Left=>90,Edge.Right=>-90,Edge.Top=>180,_=>0}));
            dc.PushTransform(new ScaleTransform(engine.Facing,1));dc.PushTransform(new TranslateTransform(-Side/2,-Side/2));
            dc.DrawImage(images.Get(frame),new Rect(g.Left*scale,g.Top*scale,g.Width*scale,g.Height*scale));
            dc.Pop();dc.Pop();dc.Pop();dc.Pop();
        }
        var bitmap=new RenderTargetBitmap(pixels,pixels,96,96,PixelFormats.Pbgra32);bitmap.Render(visual);bitmap.Freeze();
        var freeze=new FreezeWindow(bitmap,body.Position,Side);freezes.Add(freeze);freeze.Closed+=(_,_)=>freezes.Remove(freeze);freeze.Show();
    }
    private void ToggleDebug()
    {
        if(debug!=null){debug.Close();return;}
        var window=new DebugWindow(role.Package.Name);debug=window;window.Closed+=(_,_)=>debug=null;UpdateDebug();window.Show();
    }
    private void UpdateDebug()=>debug?.Refresh(engine,monitorId,DesktopPlatform.DpiScale(this),foodWorld.Foods.Count(f=>f.Owner==engine),ClickThrough,pixelHit);
    internal void Step(long dt)
    {
        if(closed)return;ApplyInputStyle();var monitor=Monitor;monitorId=monitor.Id;engine.SetActivityArea(ActivityArea,images.Geometry);engine.Step(dt);
        foreach(var freeze in freezes.ToArray())freeze.Refresh();
        debugTick+=dt;if(debugTick>=150){debugTick=0;UpdateDebug();}
    }
    internal void Render()
    {
        if(closed)return;
        DesktopPlatform.Place(this,body.Position.X,body.Position.Y,Side,Side);
        var dipSide=Side/DesktopPlatform.DpiScale(this);
        if(Math.Abs(Width-dipSide)>.01||Math.Abs(Height-dipSide)>.01)Width=Height=dipSide;
        DesktopPlatform.Place(this,body.Position.X,body.Position.Y,Side,Side);
        var frame=role.Animation.Current;
        if(rendered!=frame||renderedSize!=dipSide)
        {
            var bitmap=images.Get(frame);
            var g=images.Geometry(frame);var scale=dipSide/role.Package.AnchorRes;
            image.Source=bitmap;image.Width=g.Width*scale;image.Height=g.Height*scale;
            Canvas.SetLeft(image,g.Left*scale);Canvas.SetTop(image,g.Top*scale);rendered=frame;renderedSize=dipSide;
        }
        ApplyInputStyle();
        var facing=engine.Facing;
        var angle=engine.RenderEdge switch{Edge.Left=>90,Edge.Right=>-90,Edge.Top=>180,_=>0};
        if(canvas.RenderTransform is not TransformGroup group||((ScaleTransform)group.Children[0]).ScaleX!=facing||((RotateTransform)group.Children[1]).Angle!=angle)
        {
            canvas.RenderTransformOrigin=new WPoint(.5,.5);
            var transform=new TransformGroup();transform.Children.Add(new ScaleTransform(facing,1));transform.Children.Add(new RotateTransform(angle));canvas.RenderTransform=transform;
        }
        var text=role.Thoughts.VisibleText;
        if(string.IsNullOrWhiteSpace(text)){if(thoughtWindow.IsVisible)thoughtWindow.Hide();}
        else
        {
            thoughtText.Text=text;var r=Monitor.Work;
            if(!thoughtWindow.IsVisible)thoughtWindow.Show();
            DesktopPlatform.Place(thoughtWindow,r.Left+(r.Right-r.Left-360)/2,r.Bottom-44,360,44);
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
        var handle=new WindowInteropHelper(window).Handle;if(handle==0)return;
        var old=GetWindowLongPtr(handle,GwlExStyle).ToInt64();
        var next=(old|WsExNoActivate|WsExToolWindow)&~WsExTransparent;
        if(clickThrough)next|=WsExTransparent;
        if(next!=old)SetWindowLongPtr(handle,GwlExStyle,(nint)next);
    }
}
