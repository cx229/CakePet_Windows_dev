using System.Windows;
using System.Windows.Controls;
using CakePet.Core;
namespace CakePet.Windows;
internal sealed class RoleSettingsPage:UserControl
{
    private sealed record Option(string Id,string Label);
    private readonly Func<RoleEntry> current;
    private readonly Action<Func<RoleEntry,RoleEntry>> change;
    private readonly List<Action> dependencies=new();
    public RoleSettingsPage(Func<RoleEntry> current,RolePackage? pack,Action<Func<RoleEntry,RoleEntry>> change,Action back,bool defaults=false)
    {
        this.current=current;this.change=change;
        var entry=current();var s=entry.Settings.Normalize();
        bool Supports(string path)=>pack?.Supports(path)??true;
        var root=new Grid();root.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(188)});root.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});Content=root;
        var nav=new StackPanel{Margin=new Thickness(14,22,14,12)};root.Children.Add(nav);
        nav.Children.Add(Ui.Button("‹ 返回",back));var title=Ui.Caption(defaults?"用户习惯":entry.Name,defaults?"新角色默认设置":"角色设置",true);title.Margin=new Thickness(4,20,0,20);nav.Children.Add(title);
        var host=new ContentControl{Margin=new Thickness(22,20,22,12)};Grid.SetColumn(host,1);root.Children.Add(host);
        var sections=new List<ScrollViewer>();var buttons=new List<Button>();
        void Select(int index){host.Content=sections[index];for(int i=0;i<buttons.Count;i++)buttons[i].Background=Ui.Brush(i==index?"AccentSoft":"Canvas");}
        StackPanel Section(string name,string note)
        {
            var panel=new StackPanel{MaxWidth=900};var heading=Ui.Caption(name,note,true);((TextBlock)heading.Children[0]).FontSize=24;heading.Margin=new Thickness(0,0,0,18);panel.Children.Add(heading);
            int index=sections.Count;sections.Add(new ScrollViewer{Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});var b=Ui.Button(name,()=>Select(index));b.HorizontalContentAlignment=HorizontalAlignment.Left;b.Margin=new Thickness(0,3,0,3);buttons.Add(b);nav.Children.Add(b);return panel;
        }
        void Set(Func<RoleSettings,RoleSettings> patch){change(r=>r with{Settings=patch(r.Settings)});foreach(var refresh in dependencies)refresh();}
        SettingCard Card(StackPanel page,string name,string note,bool? enabled=null,Func<RoleSettings,bool,RoleSettings>? setter=null,bool available=true,bool details=true,bool expanded=true,Func<bool>? dependency=null)
        {
            var c=new SettingCard(name,note,enabled,setter==null?null:v=>Set(r=>setter(r,v)),available,details,expanded);page.Children.Add(c);
            if(dependency!=null){void Refresh()=>c.SetAvailable(available&&dependency());dependencies.Add(Refresh);Refresh();}return c;
        }
        void Number(SettingCard c,string name,string note,double value,double min,double max,double step,string unit,Func<RoleSettings,double,RoleSettings> setter)=>c.Add(Ui.NumberRow(name,note,value,min,max,step,unit,v=>Set(r=>setter(r,v))));
        void Flag(SettingCard c,string name,string note,bool value,Func<RoleSettings,bool,RoleSettings> setter){var sw=Ui.Switch(value);sw.Click+=(_,_)=>Set(r=>setter(r,sw.IsChecked==true));c.Add(Ui.Pair(Ui.Caption(name,note),sw));}
        void Directions(SettingCard c,string name,string note,Edge value,Func<RoleSettings,Edge,RoleSettings> setter)
        {
            var row=new StackPanel{Orientation=Orientation.Horizontal};var checks=new Dictionary<Edge,CheckBox>();
            foreach(var (edge,label) in new[]{(Edge.Top,"上"),(Edge.Bottom,"下"),(Edge.Left,"左"),(Edge.Right,"右")})
            {var b=new CheckBox{Content=label,IsChecked=value.HasFlag(edge),Style=(Style)Application.Current.FindResource("Direction"),Margin=new Thickness(4,0,0,0)};checks.Add(edge,b);row.Children.Add(b);b.Click+=(_,_)=>Set(r=>setter(r,checks.Where(x=>x.Value.IsChecked==true).Aggregate(Edge.None,(mask,x)=>mask|x.Key)));}
            c.Add(Ui.Pair(Ui.Caption(name,note),row));
        }
        var main=Section("主要设置","角色信息、外观和常用交互。");
        var identity=Card(main,"角色信息",defaults?"这些设置只影响新建角色。":$"素材包 · {pack?.Name}",details:true);
        if(!defaults){var name=new TextBox{Text=entry.Name,Width=220};void Rename(){if(string.IsNullOrWhiteSpace(name.Text)){name.Text=current().Name;return;}change(r=>r with{Name=name.Text.Trim()});}name.LostKeyboardFocus+=(_,_)=>Rename();name.KeyDown+=(_,e)=>{if(e.Key==System.Windows.Input.Key.Enter){Rename();e.Handled=true;}};identity.Add(Ui.Pair(Ui.Caption("名称","为角色取一个名字。"),name));}
        Number(identity,"召唤数量","同时出现在桌面上的实例数。",s.Count,1,30,1,"个",(r,v)=>r with{Count=(int)v});
        var appearance=Card(main,"外观","调整角色在桌面上的大小和透明度。");
        Number(appearance,"大小","相对于素材包基准尺寸。",s.Scale,.5,3,.05,"倍",(r,v)=>r with{Scale=v});
        Number(appearance,"不透明度","数值越小，角色越透明。",s.Opacity*100,5,100,1,"%",(r,v)=>r with{Opacity=v/100});
        Card(main,"移动总开关",Supports("standby/static_pool")?"关闭后使用静止待机动画。":"缺少静止待机池，强制开启。",s.Move||!Supports("standby/static_pool"),(r,v)=>r with{Move=v},Supports("standby/static_pool"),false);
        var input=Card(main,"交互方式","选择鼠标如何与桌宠交互。");
        var hit=new ComboBox{ItemsSource=new[]{"像素级命中","画布命中"},SelectedIndex=s.PixelHit?0:1,Width=160};hit.SelectionChanged+=(_,_)=>Set(r=>r with{PixelHit=hit.SelectedIndex==0});input.Add(Ui.Pair(Ui.Caption("命中方式","点击透明区域时的行为。"),hit));
        Flag(input,"点击穿透","按住 Ctrl 临时恢复交互。",s.ClickThrough,(r,v)=>r with{ClickThrough=v});
        var motion=Section("移动与物理","调整下落、抛掷和碰撞表现。");
        var gravity=Card(motion,"重力","让角色自然落向桌面边缘。",s.GravityEnabled,(r,v)=>r with{GravityEnabled=v});
        Number(gravity,"重力大小","数值越大，下落越快。",s.Gravity,0,10000,50,"px/s²",(r,v)=>r with{Gravity=v});Directions(gravity,"重力方向","选择重力作用的方向。",s.GravityEdges,(r,v)=>r with{GravityEdges=v});
        var thr=Card(motion,"抛掷",Supports("touch/roll")?"拖动后松开鼠标，将角色抛出去。":"素材包缺少抛掷动画。",s.Throw&&Supports("touch/roll"),(r,v)=>r with{Throw=v},Supports("touch/roll"));
        Number(thr,"抛掷系数","放大或减小松手时的速度。",s.ThrowCoefficient,0,5,.05,"倍",(r,v)=>r with{ThrowCoefficient=v});Number(thr,"最高速度","限制物理运动速度。",s.MaxSpeed,100,10000,50,"px/s",(r,v)=>r with{MaxSpeed=v});
        var bounce=Card(motion,"反弹","撞到边缘时弹回来。",s.BounceEnabled,(r,v)=>r with{BounceEnabled=v});Number(bounce,"反弹系数","越大，碰撞后保留的速度越多。",s.Bounce,0,1,.01,"",(r,v)=>r with{Bounce=v});Directions(bounce,"反弹方向","允许发生反弹的边缘。",s.BounceEdges,(r,v)=>r with{BounceEdges=v});
        var climbing=Section("吸附与攀岩","角色与屏幕边缘的交互。");
        var snap=Card(climbing,"吸附",Supports("snap/pool")?"拖到边缘附近时，可以让角色贴在边缘。":"素材包缺少吸附动画。",s.Snap&&Supports("snap/pool"),(r,v)=>r with{Snap=v},Supports("snap/pool"));
        Number(snap,"吸附距离","距离边缘多近时可以吸附。",s.SnapDistance,0,500,5,"px",(r,v)=>r with{SnapDistance=v});Number(snap,"吸附速度","允许吸附的松手速度上限。",s.SnapSpeed,0,5000,25,"px/s",(r,v)=>r with{SnapSpeed=v});Directions(snap,"吸附方向","选择可以吸附的边缘。",s.SnapEdges,(r,v)=>r with{SnapEdges=v});
        Card(climbing,"攀岩走壁","允许角色沿着屏幕边缘移动。",s.Climb,(r,v)=>r with{Climb=v},details:false);
        var leap=Card(climbing,"好动飞跃",Supports("climb/leap/leap")?"在攀岩时偶尔飞向另一侧，需要开启攀岩。":"素材包缺少飞跃动画。",s.Leap&&Supports("climb/leap/leap"),(r,v)=>r with{Leap=v},Supports("climb/leap/leap"),dependency:()=>current().Settings.Climb);
        Number(leap,"飞跃概率","越大，主动飞跃越频繁。",s.LeapProbability*100,0,100,1,"%",(r,v)=>r with{LeapProbability=v/100});Number(leap,"飞跃速度","飞向目标边缘的速度。",s.LeapSpeed,100,5000,25,"px/s",(r,v)=>r with{LeapSpeed=v});
        var feeding=Section("投食","管理食物的刷新、外观和进食行为。");bool foodSupported=Supports("feed/food");Func<bool> foodOn=()=>current().Settings.Feed&&foodSupported;
        Card(feeding,"投食总开关",foodSupported?"允许手动投食和自动刷新食物。":"素材包缺少食物动画。",s.Feed&&foodSupported,(r,v)=>r with{Feed=v},foodSupported,false);
        var auto=Card(feeding,"自动刷新食物","隔一段时间，桌面上会出现新食物。",s.AutoFeed,(r,v)=>r with{AutoFeed=v},foodSupported,dependency:foodOn);
        auto.Add(Ui.Pair(Ui.Caption("刷新间隔","在最短和最长时间之间随机等待。"),new RangeSlider(1,3600,s.FoodMinSeconds,s.FoodMaxSeconds,(lo,hi)=>Set(r=>r with{FoodMinSeconds=(int)lo,FoodMaxSeconds=(int)hi})),310));
        var food=Card(feeding,"食物属性","手动投食和自动刷新共用这些设置。",available:foodSupported,dependency:foodOn);
        Number(food,"数量上限","同一角色在桌面上最多有多少食物。",s.FoodLimit,1,100,1,"个",(r,v)=>r with{FoodLimit=(int)v});Number(food,"食物寿命","0 表示不会自动消失。",s.FoodLifetimeSeconds,0,86400,1,"秒",(r,v)=>r with{FoodLifetimeSeconds=(int)v});Number(food,"食物大小","对新生成的食物生效。",s.FoodScale,.1,5,.05,"倍",(r,v)=>r with{FoodScale=v});Number(food,"不透明度","调整食物的透明程度。",s.FoodOpacity*100,5,100,1,"%",(r,v)=>r with{FoodOpacity=v/100});Flag(food,"沿边缘刷新","让食物出现在活动范围的边缘。",s.FoodAtEdge,(r,v)=>r with{FoodAtEdge=v});
        var forage=Card(feeding,"自动觅食",Supports(PetEngine.ForagePath)?"角色沿边缘主动寻找食物，需要开启攀岩。":"素材包缺少觅食动画。",s.Forage,(r,v)=>r with{Forage=v},Supports(PetEngine.ForagePath),dependency:()=>foodOn()&&current().Settings.Climb);
        Number(forage,"觅食速度","调整寻找食物时的移动速度。",s.ForageMultiplier,.1,5,.05,"倍",(r,v)=>r with{ForageMultiplier=v});
        var grow=Card(feeding,"投喂变大","吃到食物后，角色会逐渐长大。",s.Grow,(r,v)=>r with{Grow=v},foodSupported,dependency:foodOn);
        Number(grow,"增长倍率","每次进食增加的基准尺寸比例。",s.FeedGrowth*100,0,100,1,"%",(r,v)=>r with{FeedGrowth=v/100});Number(grow,"最大大小","限制投喂后的最大尺寸。",s.MaxScale,.5,10,.05,"倍",(r,v)=>r with{MaxScale=v});
        var thinking=Section("碎碎念","控制角色在桌面上显示的文字。");Card(thinking,"碎碎念","开启后，按素材包中的文本策略播放。",s.Thinking,(r,v)=>r with{Thinking=v},details:false);
        var area=Section("活动范围","选择显示器、任务栏和四边活动边界。");
        var display=Card(area,"显示器","拖动角色时仍可跨屏。");var options=new[]{new Option("","自动 / 主显示器")}.Concat(DesktopPlatform.Screens.Select(x=>new Option(x.Id,x.Id+(x.Primary?"（主显示器）":"")))).ToList();if(!options.Any(x=>x.Id==s.MonitorId))options.Add(new Option(s.MonitorId,s.MonitorId+"（未连接）"));
        var monitor=new ComboBox{ItemsSource=options,DisplayMemberPath="Label",SelectedValuePath="Id",SelectedValue=s.MonitorId,Width=190};monitor.SelectionChanged+=(_,_)=>Set(r=>r with{MonitorId=monitor.SelectedValue as string??""});display.Add(Ui.Pair(Ui.Caption("首选显示器","召唤时优先使用。"),monitor));
        Flag(display,"包含任务栏","允许角色活动到任务栏所在区域。",s.FullScreen,(r,v)=>r with{FullScreen=v});
        var offsets=Card(area,"四边偏移","正值向内收缩，负值向外扩展。");Number(offsets,"左侧","",s.OffsetLeft,-500,500,1,"px",(r,v)=>r with{OffsetLeft=v});Number(offsets,"右侧","",s.OffsetRight,-500,500,1,"px",(r,v)=>r with{OffsetRight=v});Number(offsets,"顶部","",s.OffsetTop,-500,500,1,"px",(r,v)=>r with{OffsetTop=v});Number(offsets,"底部","",s.OffsetBottom,-500,500,1,"px",(r,v)=>r with{OffsetBottom=v});
        Select(0);
    }
}
