using System.Diagnostics;
using System.Runtime.InteropServices;
using CakePet.Core;
using P= CakePet.Core.Point;
using Frame= CakePet.Core.Frame;

namespace CakePet.Windows;

internal sealed class PetWindow
{
    private readonly PetEngine engine;
    private RoleRuntime role=>engine.Role;
    private PhysicsBody body=>engine.Body;
    private readonly NativeSpriteCache images;
    private readonly NativeLayer layer=new();
    private double opacity=1;
    private bool menuOpen;
    private int renderedFacing;
    private Edge renderedEdge;
    private bool renderedCanvas;
    internal event EventHandler? Closed;
    public void Show(){Render();layer.Show();}
    public void Close()=>layer.Dispose();
    private readonly List<FreezeWindow> freezes=new();
    private NativeDebugWindow? debug;
    private long debugTick;
    private readonly FoodWorld foodWorld;
    private readonly Stopwatch clock=Stopwatch.StartNew();
    private long lastMouse;
    private P pressedAt,previousMouse;
    private P pressedBody,velocity;
    private bool pressed,dragging,closed;
    private bool? lastInputPass;
    private nint lastInputHandle;
    private Func<Frame,FrameGeometry>? frameGeometry;
    private Frame? rendered;
    private double Side=>engine.DisplaySize;
    internal PetEngine Engine=>engine;
    internal NativeSpriteCache NativeImages=>images;
#if !NATIVE_HOST
    internal WpfImageCache Images {get;private set;}=null!;
#endif
    private double renderedSize;
    private string monitorId;
    private bool fullScreen,clickThrough,pixelHit=true,thinkingEnabled=true;
    private RoleSettings settings=new();
    internal event Action<Guid,Func<RoleSettings,RoleSettings>>? ChangeSettings;
    internal event Action<Guid>? RecallRole;
    internal event Action<Guid>? OpenSettings;
    internal event Action<Guid>? ClearRoleFood;
    private void Change(Func<RoleSettings,RoleSettings> change)=>ChangeSettings?.Invoke(RoleId,change);
    internal Guid RoleId {get;}
    internal Guid InstanceId {get;}=Guid.NewGuid();
    internal int InstanceNumber {get;}
    internal double CurrentScale=>engine.DisplaySize/role.Package.AnchorRes;
    internal string RoleName {get;set;}="";
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
        else if(old.MaxScale!=settings.MaxScale&&CurrentScale>settings.MaxScale)engine.Resize(role.Package.AnchorRes*settings.MaxScale);
        configured=true;opacity=settings.Opacity;fullScreen=settings.FullScreen;ClickThrough=settings.ClickThrough;pixelHit=settings.PixelHit;thinkingEnabled=settings.Thinking&&globalThinking;role.Thoughts.Enabled=thinkingEnabled;role.Thoughts.RestDuration=new Interval(settings.ThoughtRestMin*1000,settings.ThoughtRestMax*1000);
        engine.Body.ThrowEnabled=settings.Throw&&role.Package.Supports("touch/roll");engine.Body.SnapEnabled=settings.Snap&&role.Package.Supports("snap/pool");
        engine.SetStaticStandby(!settings.Move);engine.SetClimbing(settings.Climb);engine.SetLeapEnabled(settings.Leap);engine.SetAutoForage(settings.Forage);
        engine.FeedingEnabled=settings.Feed&&role.Package.Supports("feed/food");engine.GrowOnFeed=settings.Grow;
        var automatic=primary&&engine.FeedingEnabled&&settings.AutoFeed;
        if(automatic!=foodWorld.IsAutomatic(engine)||(automatic&&(old.FoodMinSeconds!=settings.FoodMinSeconds||old.FoodMaxSeconds!=settings.FoodMaxSeconds)))foodWorld.SetAutomatic(engine,automatic,new Interval(settings.FoodMinSeconds*1000,settings.FoodMaxSeconds*1000),SpawnFood);
        foreach(var food in foodWorld.Foods.Where(f=>f.Owner==engine))food.Opacity=settings.FoodOpacity;
        if(!engine.FeedingEnabled)foodWorld.Clear(engine);
        RefreshDesktopSpace();engine.SetActivityArea(ActivityArea,frameGeometry!);Render();
    }
    private bool configured;
    private FoodInstance? SpawnFood()=>foodWorld.Spawn(engine,ActivityArea,48,settings.FoodLifetimeSeconds==0?null:settings.FoodLifetimeSeconds*1000L,settings.FoodAtEdge,settings.FoodLimit,id=>{var v=images.GetFoodVisual(id);return new FoodExtent(v.LogicalWidth*settings.FoodScale,v.LogicalHeight*settings.FoodScale);},settings.FoodOpacity);
    private bool HitAt(P pointer)
    {
        var offset=pointer-body.Position;if(offset.X<0||offset.Y<0||offset.X>=Side||offset.Y>=Side)return false;
        var local=AnchorGeometry.Inverse(offset,Side,engine.RenderEdge,engine.Facing);
        var g=images.Geometry(role.Animation.Current);var scale=Side/role.Package.AnchorRes;
        return images.OpaqueAt(role.Animation.Current,(local.X/scale-g.Left)/g.Width,(local.Y/scale-g.Top)/g.Height);
    }
    private DesktopScreen Monitor=>DesktopPlatform.Resolve(monitorId,engine.Center);
    internal Bounds ActivityArea=>AreaOf(Monitor);
    private Bounds AreaOf(DesktopScreen screen)=>settings.ApplyOffsets(fullScreen?screen.Full:screen.Work);
    private bool LegacyCanConnect=>settings.ScreenConnect&&settings.Move&&DesktopPlatform.Screens.Count>1&&body.SnappedEdge==Edge.None&&body.State is (PhysicalState.Free or PhysicalState.Climbing or PhysicalState.Throwing)&&!engine.Foraging;
    private ScreenRegion[] Regions()=>DesktopPlatform.Screens.Select(s=>{var a=AreaOf(s);return new ScreenRegion(s.Id,s.Full,a,AnchorGeometry.WindowBounds(a,Side,engine.AnchorOffset,engine.RenderEdge));}).ToArray();
    private ScreenRegion SourceRegion(ScreenRegion[] regions)=>regions.FirstOrDefault(s=>s.Id==monitorId)??regions.First(s=>s.Id==Monitor.Id);
    private Bounds ConnectedLimits(Bounds ordinary){if(!LegacyCanConnect)return ordinary;var regions=Regions();return ConnectedDesktop.Limits(regions,SourceRegion(regions),body.Position,engine.AnchorOffset,body.ClimbEdge);}
    private Edge ConnectedEdges(){if(!LegacyCanConnect)return Edge.None;var regions=Regions();return ConnectedDesktop.OpenEdges(regions,SourceRegion(regions),body.Position,body.ClimbEdge);}
    private MotionProjection? ConnectedMotion(P from,P next){if(!LegacyCanConnect)return null;var regions=Regions();var transfer=ConnectedDesktop.Project(regions,SourceRegion(regions),from,next,engine.AnchorOffset,body.ClimbEdge);monitorId=transfer.Region.Id;engine.AdoptMotionArea(transfer.Region.Area,transfer.Limits);return new MotionProjection(transfer.Position,transfer.Limits);}
    private long spaceRevision=-1;
    private RoleSettings? spaceSettings;
    private string? spaceMonitor;
    private void RefreshDesktopSpace()
    {
        var monitor=Monitor;var revision=DesktopPlatform.ScreenRevision;
        var selected=settings.ScreenConnect?"":monitor.Id;
        if(spaceRevision==revision&&spaceSettings==settings&&spaceMonitor==selected)return;
        spaceRevision=revision;spaceSettings=settings;spaceMonitor=selected;
        engine.SetDesktopSpace(DesktopPlatform.ActivitySpace(monitor,settings),settings.HorizontalWrap);
    }
    internal bool ClickThrough {get=>clickThrough;set{clickThrough=value;ApplyInputStyle();}}
    private void ApplyInputStyle()
    {
        var control=DesktopPlatform.ControlDown;
        var pass=!pressed&&!menuOpen&&!control&&(clickThrough||(pixelHit&&!HitAt(DesktopPlatform.Cursor())));
        var handle=layer.Handle;if(handle==0)return;
        if(lastInputPass==pass&&lastInputHandle==handle)return;
        layer.Pass(pass);lastInputPass=pass;lastInputHandle=handle;
    }
     #if !NATIVE_HOST
    public PetWindow(RoleRuntime role,WpfImageCache images,int index,FoodWorld world,Guid roleId=default,int instanceNumber=1):this(role,images.Native,index,world,roleId,instanceNumber){Images=images;}
#endif
    public PetWindow(RoleRuntime role,NativeSpriteCache images,int index,FoodWorld world,Guid roleId=default,int instanceNumber=1)
    {
        this.images=images;frameGeometry=images.Geometry;foodWorld=world;RoleId=roleId;InstanceNumber=instanceNumber;
        var monitor=DesktopPlatform.Primary;monitorId=monitor.Id;
        var b=DesktopGeometry.Fit(monitor.Work,160,160);engine=new PetEngine(role,b,160,new P(Math.Clamp((b.Left+b.Right)/2+index*30,b.Left,b.Right),b.Bottom));
        RefreshDesktopSpace();
        engine.SetActivityArea(monitor.Work,frameGeometry!);
        engine.ReturnHome();
        body.SetPosition(new P(Math.Clamp(body.Position.X+index*30,engine.Bounds.Left,engine.Bounds.Right),body.Position.Y));
        
        layer.Down+=OnDown;layer.Move+=OnMove;layer.Up+=OnUp;layer.Menu+=OpenMenu;
        layer.Lost+=()=>{if(pressed){pressed=false;if(dragging){velocity=default;FinishDrag();}}};
        engine.GrowOnFeed=true;engine.FeedGrowth=role.Package.AnchorRes*.03;engine.MaxDisplaySize=role.Package.AnchorRes*3;
        layer.Closed+=()=>{closed=true;ClearFreezes();debug?.Close();engine.Dispose();Closed?.Invoke(this,EventArgs.Empty);};
    }
    private void OpenMenu()
    {
        if(closed)return;
        menuOpen=true;layer.Pass(false);
        try
        {
            var animations=role.Package.Candidates("menu/pool").Select(mode=>new NativeMenu.Item(mode,()=>{if(engine.MenuAction(mode))Render();})).ToArray();
            NativeMenu.Show(layer.Handle,new NativeMenu.Item[]{
                new("摸摸头",()=>{engine.PatHead();Render();}),
                new("菜单动画",Enabled:animations.Length>0&&role.CanTrigger("menu/pool"),Children:animations),
                new("归位",()=>{engine.ReturnHome();Render();}),
                new("静止待机",()=>Change(s=>s with{Move=!s.Move}),role.Package.Supports("standby/static_pool"),role.StaticStandby),
                new(null),
                new("投食",()=>{SpawnFood();foodWorld.ResetSpawnTimer(engine);},engine.FeedingEnabled),
                new("清除本角色食物",()=>ClearRoleFood?.Invoke(RoleId)),
                new("恢复大小",ResetSize),
                new("点击穿透（Ctrl 临时恢复）",()=>Change(s=>s with{ClickThrough=!s.ClickThrough}),true,ClickThrough),
                new("定格当前画面",Freeze),new("清除当前实例定格",ClearFreezes),
                new("实时调试窗口",ToggleDebug),new("角色设置",()=>OpenSettings?.Invoke(RoleId)),
                new("召回角色（全部实例）",()=>RecallRole?.Invoke(RoleId))
            });
        }
        finally{menuOpen=false;ApplyInputStyle();}
    }
    private P Screen(){var p=DesktopPlatform.Cursor();return new P(p.X,p.Y);}
    private void OnDown()
    {
        pressed=true;dragging=false;pressedAt=previousMouse=Screen();pressedBody=body.Position;velocity=default;lastMouse=clock.ElapsedMilliseconds;
        if(!layer.Capture())pressed=false;
    }
    private void OnMove()
    {
        if(!pressed)return;
        var p=Screen();var dx=p.X-pressedAt.X;var dy=p.Y-pressedAt.Y;
        if(!dragging&&Math.Sqrt(dx*dx+dy*dy)<5)return;
        if(!dragging){dragging=true;engine.BeginDrag();}
        var now=clock.ElapsedMilliseconds;var dt=Math.Max(1,now-lastMouse)/1000.0;
        var instant=new P((p.X-previousMouse.X)/dt,(p.Y-previousMouse.Y)/dt);
        velocity=velocity*.35+instant*.65;previousMouse=p;lastMouse=now;
        monitorId=DesktopPlatform.At(new P(p.X,p.Y)).Id;
        engine.DragTo(pressedBody+new P(dx,dy),velocity.X);Render();
    }
    private void OnUp()
    {
        if(!pressed)return;pressed=false;layer.Release();
        if(dragging)FinishDrag();else engine.PatHead();Render();
    }
    private void FinishDrag()
    {
        dragging=false;if(clock.ElapsedMilliseconds-lastMouse>120)velocity=default;
        RefreshDesktopSpace();engine.SetActivityArea(ActivityArea,frameGeometry!);engine.EndDrag(velocity,DesktopPlatform.Cursor());Render();
    }
    internal void SetInstanceScale(double scale){if(closed||!double.IsFinite(scale))return;var min=SettingLimits.Get(settings,SettingLimits.Specs.First(x=>x.Key=="Scale")).Min;engine.Resize(role.Package.AnchorRes*Math.Clamp(scale,min,Math.Max(min,settings.MaxScale)));Render();}
    internal void ResetSize(){SetInstanceScale(settings.Scale);}
    internal void ClearFreezes(){foreach(var freeze in freezes.ToArray())freeze.Close();}
    private void Freeze()
    {
        var pixels=Math.Max(1,(int)Math.Round(Side));
        var bitmap=images.Get(role.Animation.Current,pixels,engine.Facing,engine.RenderEdge,false);
        var freeze=new FreezeWindow(bitmap,body.Position,Side);freezes.Add(freeze);freeze.Closed+=(_,_)=>freezes.Remove(freeze);freeze.Show();
    }
    private void ToggleDebug()
    {
        if(debug!=null){debug.Close();return;}
        var window=new NativeDebugWindow((string.IsNullOrWhiteSpace(RoleName)?role.Package.Name:RoleName)+" #"+InstanceNumber);debug=window;window.Closed+=(_,_)=>debug=null;UpdateDebug();window.Show();
    }
    private void UpdateDebug()=>debug?.Refresh(engine,monitorId,layer.Dpi,foodWorld.Foods.Count(f=>f.Owner==engine),ClickThrough,pixelHit,InstanceId,InstanceNumber,settings.Scale,CurrentScale,settings.Count);
    internal void Step(long dt)
    {
        if(closed)return;var monitor=Monitor;monitorId=monitor.Id;RefreshDesktopSpace();engine.SetActivityArea(AreaOf(monitor),frameGeometry!);engine.Step(dt);if(body.State!=PhysicalState.Dragging)monitorId=DesktopPlatform.At(body.Position+engine.AnchorOffset).Id;
        if(freezes.Count>0)foreach(var freeze in freezes)freeze.Refresh();
        if(debug!=null){debugTick+=dt;if(debugTick>=150){debugTick=0;UpdateDebug();}}
    }
    internal void Render()
    {
        if(closed)return;
        var size=Math.Max(1,(int)Math.Round(Side));var frame=role.Animation.Current;var facing=engine.Facing;var edge=engine.RenderEdge;bool canvasHit=!pixelHit||DesktopPlatform.ControlDown;
        if(rendered!=frame||renderedSize!=size||renderedFacing!=facing||renderedEdge!=edge||renderedCanvas!=canvasHit)
        {
            var renderedPixels=images.Get(frame,size,facing,edge,canvasHit);
            layer.Paint(renderedPixels,size,size,body.Position.X,body.Position.Y,opacity);
            rendered=frame;renderedSize=size;renderedFacing=facing;renderedEdge=edge;renderedCanvas=canvasHit;
        }
        else layer.Place(body.Position.X,body.Position.Y,opacity);
        ApplyInputStyle();
    }
}

