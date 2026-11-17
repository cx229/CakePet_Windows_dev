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
    private bool fullScreen,clickThrough,pixelHit=true,thinkingEnabled=true;
    private RoleSettings settings=new();
    internal event Action<Guid,Func<RoleSettings,RoleSettings>>? ChangeSettings;
    internal event Action<Guid>? RecallRole;
    internal event Action<Guid>? OpenSettings;
    internal event Action<Guid>? ClearRoleFood;
    internal event Action<Guid>? ClearRoleFreezes;
    internal event Action<Guid>? ResetRoleSize;
    private void Change(Func<RoleSettings,RoleSettings> change)=>ChangeSettings?.Invoke(RoleId,change);
    internal Guid RoleId {get;}
    internal ThinkingStyle ThoughtStyle=>settings.ThoughtStyle;
    internal string? VisibleThought=>thinkingEnabled?role.Thoughts.VisibleText:null;
    internal bool IsAutomatic=>foodWorld.IsAutomatic(engine);
    internal void ApplySettings(RoleSettings value,bool primary,bool globalThinking=true)
    {
        var old=settings;settings=value.Effective(role.Package);
        if(!configured||old.MonitorId!=settings.MonitorId){monitorId=DesktopPlatform.Resolve(settings.MonitorId,engine.Center).Id;}
        var b=engine.Body;b.Gravity=settings.Gravity;b.GravityEnabled=settings.GravityEnabled;b.GravityEdges=settings.GravityEdges;b.MaxSpeed=settings.MaxSpeed;b.Bounce=settings.Bounce;b.BounceEnabled=settings.BounceEnabled;b.BounceEdges=settings.BounceEdges;b.ThrowCoefficient=settings.ThrowCoefficient;b.SnapDistance=settings.SnapDistance;b.SnapSpeed=settings.SnapSpeed;b.SnapEdges=settings.SnapEdges;
        engine.LeapProbability=settings.LeapProbability;engine.LeapSpeed=settings.LeapSpeed;engine.ForageMultiplier=settings.ForageMultiplier;engine.FeedGrowth=role.Package.AnchorRes*settings.FeedGrowth;engine.MaxDisplaySize=role.Package.AnchorRes*settings.MaxScale;
        if(old.Scale!=settings.Scale||!configured)engine.Resize(role.Package.AnchorRes*settings.Scale);
        configured=true;Opacity=settings.Opacity;fullScreen=settings.FullScreen;ClickThrough=settings.ClickThrough;pixelHit=settings.PixelHit;thinkingEnabled=settings.Thinking&&globalThinking;role.Thoughts.Enabled=thinkingEnabled;role.Thoughts.RestDuration=new Interval(settings.ThoughtRestMin*1000,settings.ThoughtRestMax*1000);
        engine.Body.ThrowEnabled=settings.Throw&&role.Package.Supports("touch/roll");engine.Body.SnapEnabled=settings.Snap&&role.Package.Supports("snap/pool");
        engine.SetStaticStandby(!settings.Move);engine.SetClimbing(settings.Climb);engine.SetLeapEnabled(settings.Leap);engine.SetAutoForage(settings.Forage);
        engine.FeedingEnabled=settings.Feed&&role.Package.Supports("feed/food");engine.GrowOnFeed=settings.Grow;
        var automatic=primary&&engine.FeedingEnabled&&settings.AutoFeed;
        if(automatic!=foodWorld.IsAutomatic(engine)||(automatic&&(old.FoodMinSeconds!=settings.FoodMinSeconds||old.FoodMaxSeconds!=settings.FoodMaxSeconds)))foodWorld.SetAutomatic(engine,automatic,new Interval(settings.FoodMinSeconds*1000,settings.FoodMaxSeconds*1000),SpawnFood);
        foreach(var food in foodWorld.Foods.Where(f=>f.Owner==engine))food.Opacity=settings.FoodOpacity;
        if(!engine.FeedingEnabled)foodWorld.Clear(engine);
        engine.SetActivityArea(ActivityArea,images.Geometry);Render();
    }
    private bool configured;
    private FoodInstance? SpawnFood()=>foodWorld.Spawn(engine,ActivityArea,48,settings.FoodLifetimeSeconds==0?null:settings.FoodLifetimeSeconds*1000L,settings.FoodAtEdge,settings.FoodLimit,id=>{var v=images.GetFoodVisual(id);return new FoodExtent(v.LogicalWidth*settings.FoodScale,v.LogicalHeight*settings.FoodScale);},settings.FoodOpacity);
    private bool HitAt(P pointer)
    {
        var local=AnchorGeometry.Inverse(pointer-body.Position,Side,engine.RenderEdge,engine.Facing);
        var g=images.Geometry(role.Animation.Current);var scale=Side/role.Package.AnchorRes;
        return images.OpaqueAt(role.Animation.Current,(local.X/scale-g.Left)/g.Width,(local.Y/scale-g.Top)/g.Height);
    }
    private DesktopScreen Monitor=>DesktopPlatform.Resolve(monitorId,engine.Center);
    internal Bounds ActivityArea=>AreaOf(Monitor);
    private Bounds AreaOf(DesktopScreen screen)=>settings.ApplyOffsets(fullScreen?screen.Full:screen.Work);
    private bool CanConnect=>settings.ScreenConnect&&settings.Move&&DesktopPlatform.Screens.Count>1&&body.SnappedEdge==Edge.None&&body.State is (PhysicalState.Free or PhysicalState.Climbing or PhysicalState.Throwing)&&!engine.Foraging;
    private ScreenRegion[] Regions()=>DesktopPlatform.Screens.Select(s=>{var a=AreaOf(s);return new ScreenRegion(s.Id,s.Full,a,AnchorGeometry.WindowBounds(a,Side,engine.AnchorOffset,engine.RenderEdge));}).ToArray();
    private ScreenRegion SourceRegion(ScreenRegion[] regions)=>regions.FirstOrDefault(s=>s.Id==monitorId)??regions.First(s=>s.Id==Monitor.Id);
    private Bounds ConnectedLimits(Bounds ordinary){if(!CanConnect)return ordinary;var regions=Regions();return ConnectedDesktop.Limits(regions,SourceRegion(regions),body.Position,engine.AnchorOffset,body.ClimbEdge);}
    private Edge ConnectedEdges(){if(!CanConnect)return Edge.None;var regions=Regions();return ConnectedDesktop.OpenEdges(regions,SourceRegion(regions),body.Position,body.ClimbEdge);}
    private MotionProjection? ConnectedMotion(P from,P next){if(!CanConnect)return null;var regions=Regions();var transfer=ConnectedDesktop.Project(regions,SourceRegion(regions),from,next,engine.AnchorOffset,body.ClimbEdge);monitorId=transfer.Region.Id;engine.AdoptMotionArea(transfer.Region.Area,transfer.Limits);return new MotionProjection(transfer.Position,transfer.Limits);}
    internal bool ClickThrough {get=>clickThrough;set{clickThrough=value;ApplyInputStyle();}}
    private void ApplyInputStyle()
    {
        var control=DesktopPlatform.ControlDown;
        var pass=!pressed&&ContextMenu?.IsOpen!=true&&!control&&(clickThrough||(pixelHit&&!HitAt(DesktopPlatform.Cursor())));
        NativeStyles.NonActivating(this,pass);
    }
    public PetWindow(RoleRuntime role,WpfImageCache images,int index,FoodWorld world,Guid roleId=default)
    {
        this.images=images;foodWorld=world;RoleId=roleId;Title=role.Package.Name;
        Width=Height=160;WindowStyle=WindowStyle.None;ResizeMode=ResizeMode.NoResize;AllowsTransparency=true;
        Background=Brushes.Transparent;Topmost=true;ShowInTaskbar=false;ShowActivated=false;
        canvas.Children.Add(image);Content=canvas;
        var monitor=DesktopPlatform.Primary;monitorId=monitor.Id;
        var b=DesktopGeometry.Fit(monitor.Work,160,160);engine=new PetEngine(role,b,160,new P(Math.Clamp((b.Left+b.Right)/2+index*30,b.Left,b.Right),b.Bottom));
        engine.ConnectedLimits=ConnectedLimits;engine.ConnectedEdges=ConnectedEdges;engine.ConnectedMotion=ConnectedMotion;
        engine.SetActivityArea(monitor.Work,images.Geometry);
        engine.ReturnHome();
        body.SetPosition(new P(Math.Clamp(body.Position.X+index*30,engine.Bounds.Left,engine.Bounds.Right),body.Position.Y));
        
        SourceInitialized+=(_,_)=>{ApplyInputStyle();DesktopPlatform.Place(this,body.Position.X,body.Position.Y,Side,Side);};
        var menu=new ContextMenu();
        Add("摸摸头",()=>engine.PatHead());
        var menuAction=new MenuItem{Header="菜单动画"};menu.Items.Add(menuAction);
        foreach(var mode in role.Package.Candidates("menu/pool"))
        {
            var item=new MenuItem{Header=mode};item.Click+=(_,_)=>{if(engine.MenuAction(mode))Render();};menuAction.Items.Add(item);
        }
        menuAction.IsEnabled=menuAction.Items.Count>0;
        Add("归位",()=>{engine.ReturnHome();Render();});
        var still=Add("静止待机",()=>Change(s=>s with{Move=!s.Move}));still.IsCheckable=true;
        var climb=Add("攀岩走壁",()=>Change(s=>s with{Climb=!s.Climb}));climb.IsCheckable=true;climb.Visibility=Visibility.Collapsed;
        var leap=Add("好动飞跃",()=>Change(s=>s with{Leap=!s.Leap}));leap.IsCheckable=true;leap.Visibility=Visibility.Collapsed;
        var leapTest=Add("立即飞跃（测试）",()=>engine.TryStartLeap());leapTest.Visibility=Visibility.Collapsed;
        menu.Items.Add(new Separator());
        FoodInstance? Spawn()=>SpawnFood();
        var feed=Add("投食",()=>{Spawn();world.ResetSpawnTimer(engine);});
        var automatic=Add("自动刷新食物",()=>Change(s=>s with{AutoFeed=!s.AutoFeed}));automatic.IsCheckable=true;automatic.Visibility=Visibility.Collapsed;
        var forage=Add("自动觅食",()=>Change(s=>s with{Forage=!s.Forage}));forage.IsCheckable=true;forage.Visibility=Visibility.Collapsed;
        var grow=Add("投喂变大",()=>Change(s=>s with{Grow=!s.Grow}));grow.IsCheckable=true;grow.Visibility=Visibility.Collapsed;
        Add("清除本角色食物",()=>ClearRoleFood?.Invoke(RoleId));
        Add("恢复大小",()=>ResetRoleSize?.Invoke(RoleId));
        var screen=Add("全屏活动范围（含任务栏）",()=>Change(s=>s with{FullScreen=!s.FullScreen}));screen.IsCheckable=true;screen.Visibility=Visibility.Collapsed;
        var pixel=Add("像素级命中",()=>Change(s=>s with{PixelHit=!s.PixelHit}));pixel.IsCheckable=true;pixel.Visibility=Visibility.Collapsed;
        var through=Add("点击穿透（Ctrl 临时恢复）",()=>Change(s=>s with{ClickThrough=!s.ClickThrough}));through.IsCheckable=true;
        Add("定格当前画面",Freeze);
        Add("清除本角色定格",()=>ClearRoleFreezes?.Invoke(RoleId));
        Add("实时调试窗口",ToggleDebug);
        Add("角色设置",()=>OpenSettings?.Invoke(RoleId));
        Add("召回",()=>RecallRole?.Invoke(RoleId));
        menu.Opened+=(_,_)=>{
            menuAction.IsEnabled=menuAction.Items.Count>0&&role.CanTrigger("menu/pool");still.IsEnabled=role.Package.Supports("standby/static_pool");still.IsChecked=role.StaticStandby;
            climb.IsChecked=engine.ClimbEnabled;leap.IsChecked=engine.LeapEnabled;leap.IsEnabled=role.Package.Supports("climb/leap/leap");
            leapTest.IsEnabled=engine.ClimbEnabled&&engine.LeapEnabled;
            feed.IsEnabled=automatic.IsEnabled=role.Package.Supports("feed/food");automatic.IsChecked=settings.AutoFeed;feed.IsEnabled=automatic.IsEnabled=engine.FeedingEnabled;
            forage.IsEnabled=engine.ClimbEnabled&&role.Package.Supports(PetEngine.ForagePath);forage.IsChecked=engine.AutoForageEnabled;
            grow.IsChecked=engine.GrowOnFeed;screen.IsChecked=fullScreen;through.IsChecked=ClickThrough;pixel.IsChecked=pixelHit;
        };
        ContextMenu=menu;
        MenuItem Add(string title,Action action){var item=new MenuItem{Header=title};item.Click+=(_,_)=>action();menu.Items.Add(item);return item;}
        MouseLeftButtonDown+=OnDown;MouseMove+=OnMove;MouseLeftButtonUp+=OnUp;
        LostMouseCapture+=(_,_)=>{if(pressed){pressed=false;if(dragging){velocity=default;FinishDrag();}}};
        engine.GrowOnFeed=true;engine.FeedGrowth=role.Package.AnchorRes*.03;engine.MaxDisplaySize=role.Package.AnchorRes*3;
        Loaded+=(_,_)=>Render();
        Closed+=(_,_)=>{closed=true;ClearFreezes();debug?.Close();engine.Dispose();};
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
    internal void ResetSize(){engine.Resize(role.Package.AnchorRes*settings.Scale);Render();}
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
