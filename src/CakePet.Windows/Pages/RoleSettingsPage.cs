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
    internal Guid RoleId=>current().Id;
    internal int SelectedSection {get;private set;}
    public RoleSettingsPage(Func<RoleEntry> current,RolePackage? pack,Action<Func<RoleEntry,RoleEntry>> change,Action back,bool defaults=false,bool globalThinking=true,Action<string>? applyHabits=null,Action? exportHabits=null,Action? importHabits=null,Action? resetHabits=null,int initialSection=0,Action<int>? reopen=null)
    {
        this.current=current;this.change=change;
        var entry=current();var s=entry.Settings.Normalize();
        bool Supports(string path)=>pack?.Supports(path)??true;
        var petName=defaults?"角色":string.IsNullOrWhiteSpace(pack?.ShortName)?entry.Name:pack.ShortName;
        var foodName=string.IsNullOrWhiteSpace(pack?.FoodName)?"食物":pack.FoodName;
        bool MoveOn()=>current().Settings.Move||!Supports("standby/static_pool");
        bool FoodOn()=>current().Settings.Feed&&Supports("feed/food");
        var root=new Grid();root.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(205)});root.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});Content=root;
        var nav=new StackPanel{Margin=new Thickness(10,16,10,12)};var sidebar=new Border{Background=Ui.Brush("Sidebar"),Child=new ScrollViewer{Content=nav,VerticalScrollBarVisibility=ScrollBarVisibility.Auto}};root.Children.Add(sidebar);
        nav.Children.Add(Ui.Light("‹ 返回",back));var title=Ui.Caption(defaults?"用户习惯":entry.Name,defaults?"新角色默认设置":"角色设置",true);title.Margin=new Thickness(4,20,0,20);nav.Children.Add(title);
        if(pack!=null&&!defaults){try{var id=pack.Candidates("standby/static_pool").Concat(pack.Candidates("standby/pool")).First();var image=new Image{Source=new WpfImageCache(pack).Get(pack.Modes[id].Frames[0]),Width=94,Height=82,Stretch=System.Windows.Media.Stretch.Uniform};var preview=Ui.Card(image);preview.Margin=new Thickness(4,0,4,12);nav.Children.Add(preview);}catch{}}
        var host=new ContentControl{Margin=new Thickness(22,20,22,12)};Grid.SetColumn(host,1);root.Children.Add(host);
        if(defaults&&applyHabits!=null)
        {
            var more=Ui.Light("用户习惯 ⋯",()=>{});var menu=new ContextMenu();
            void Menu(string name,Action? action){if(action==null)return;var item=new MenuItem{Header=name};item.Click+=(_,_)=>action();menu.Items.Add(item);}
            Menu("应用全部到已有角色",()=>applyHabits("全部"));Menu("导出用户习惯",exportHabits);Menu("导入用户习惯",importHabits);Menu("恢复软件默认",resetHabits);
            more.ContextMenu=menu;more.Click+=(_,_)=>menu.IsOpen=true;nav.Children.Add(more);
        }
        int selectedSection=0;var sections=new List<FrameworkElement>();var buttons=new List<Button>();var summaries=new List<Action>();
        void Select(int index){selectedSection=index;SelectedSection=index;host.Content=sections[index];for(int i=0;i<buttons.Count;i++)buttons[i].Background=Ui.Brush(i==index?"AccentSoft":"Sidebar");}
        StackPanel Section(string name,string note)
        {
            var panel=new StackPanel{MaxWidth=940};var heading=Ui.Caption(name,note,true);((TextBlock)heading.Children[0]).FontSize=24;heading.Margin=new Thickness(0,0,0,16);
            var layout=new DockPanel();var header=defaults&&applyHabits!=null&&name!="设置阈值"?Ui.Pair(heading,Ui.Light("应用本项到已有角色",()=>applyHabits(name))):(FrameworkElement)heading;DockPanel.SetDock(header,Dock.Top);layout.Children.Add(header);layout.Children.Add(new ScrollViewer{Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});
            int index=sections.Count;sections.Add(layout);var b=Ui.Nav(name,()=>Select(index));buttons.Add(b);nav.Children.Add(b);return panel;
        }
        void Set(Func<RoleSettings,RoleSettings> patch){change(r=>r with{Settings=patch(r.Settings)});foreach(var refresh in dependencies)refresh();foreach(var refresh in summaries)refresh();}
        void Summary(Func<string> state){var b=buttons[^1];var label=b.Content.ToString()??"";var text=Ui.Text(label,13);var value=Ui.Text("",11,true);var row=Ui.Pair(text,value);row.MinHeight=0;b.Content=row;b.HorizontalContentAlignment=HorizontalAlignment.Stretch;void Refresh(){value.Text=state();value.Foreground=value.Text.Contains("强制")?System.Windows.Media.Brushes.IndianRed:Ui.Brush("Muted");}summaries.Add(Refresh);Refresh();}
        void Group(StackPanel page,string title,Func<bool>? visible=null){var t=Ui.Text(title,12,true);t.Margin=new Thickness(2,8,0,6);page.Children.Add(t);if(visible!=null){void Refresh(){t.Visibility=visible()?Visibility.Visible:Visibility.Collapsed;}dependencies.Add(Refresh);Refresh();}}
        SettingCard Card(StackPanel page,string name,string note,bool? enabled=null,Func<RoleSettings,bool,RoleSettings>? setter=null,bool available=true,bool details=true,bool expanded=true,Func<bool>? dependency=null)
        {
            var c=new SettingCard(name,note,enabled,setter==null?null:v=>Set(r=>setter(r,v)),available,details,expanded);page.Children.Add(c);
            if(!available)c.SetWarning(note);
            if(dependency!=null){void Refresh(){c.Visibility=dependency()?Visibility.Visible:Visibility.Collapsed;c.SetAvailable(available);}dependencies.Add(Refresh);Refresh();}return c;
        }
        void Number(SettingCard c,string name,string note,double value,double min,double max,double step,string unit,Func<RoleSettings,double,RoleSettings> setter,string? key=null,double factor=1)
        {
            if(key!=null&&SettingLimits.Specs.FirstOrDefault(x=>x.Key==key) is NumericSetting spec){var limit=SettingLimits.Get(current().Settings,spec);min=limit.Min*factor;max=limit.Max*factor;}
            var control=Ui.NumberRow(name,note,value,min,max,step,unit,v=>Set(r=>setter(r,v)));if(defaults&&applyHabits!=null&&key!=null){var block=new DockPanel();var apply=Ui.Light("应用本项",()=>applyHabits("item:"+key));apply.HorizontalAlignment=HorizontalAlignment.Right;DockPanel.SetDock(apply,Dock.Top);block.Children.Add(apply);block.Children.Add(control);c.Add(block);}else c.Add(control);
        }
        void Flag(SettingCard c,string name,string note,bool value,Func<RoleSettings,bool,RoleSettings> setter){var sw=Ui.Switch(value);sw.Click+=(_,_)=>Set(r=>setter(r,sw.IsChecked==true));c.Add(Ui.Pair(Ui.Caption(name,note),sw));}
        void Directions(SettingCard c,string name,string note,Edge value,Func<RoleSettings,Edge,RoleSettings> setter,bool exclusive=false)
        {
            var row=new StackPanel{Orientation=Orientation.Horizontal};var checks=new Dictionary<Edge,CheckBox>();
            foreach(var (edge,label) in new[]{(Edge.Top,"上"),(Edge.Bottom,"下"),(Edge.Left,"左"),(Edge.Right,"右")})
            {var b=new CheckBox{Content=label,IsChecked=value.HasFlag(edge),Style=(Style)Application.Current.FindResource("Direction"),Margin=new Thickness(4,0,0,0)};checks.Add(edge,b);row.Children.Add(b);b.Click+=(_,_)=>{if(exclusive&&b.IsChecked==true){var opposite=edge switch{Edge.Top=>Edge.Bottom,Edge.Bottom=>Edge.Top,Edge.Left=>Edge.Right,_=>Edge.Left};if(checks.TryGetValue(opposite,out var other))other.IsChecked=false;}Set(r=>setter(r,checks.Where(x=>x.Value.IsChecked==true).Aggregate(Edge.None,(mask,x)=>mask|x.Key)));};}
            c.Add(Ui.Pair(Ui.Caption(name,note),row));
        }
        var main=Section("显示与交互","大小、透明度和鼠标交互。");Group(main,"显示");
        var identity=Card(main,"角色信息",defaults?"这些设置只影响新建角色。":$"素材包 · {pack?.Name}",details:true);
        if(!defaults){var name=new TextBox{Text=entry.Name,Width=220};void Rename(){if(string.IsNullOrWhiteSpace(name.Text)){name.Text=current().Name;return;}change(r=>r with{Name=name.Text.Trim()});}name.LostKeyboardFocus+=(_,_)=>Rename();name.KeyDown+=(_,e)=>{if(e.Key==System.Windows.Input.Key.Enter){Rename();e.Handled=true;}};identity.Add(Ui.Pair(Ui.Caption("名称","为角色取一个名字。"),name));}
        Number(identity,"召唤数量","同时出现在桌面上的实例数。",s.Count,1,30,1,"个",(r,v)=>r with{Count=(int)v},"Count",1);
        var appearance=Card(main,"显示","调整桌面上「"+petName+"」的大小和透明度。");
        Number(appearance,"大小","「"+petName+"」可大可大可大",s.Scale,.5,3,.05,"倍",(r,v)=>r with{Scale=v},"Scale",1);
        Number(appearance,"透明度","「"+petName+"」的可见程度",s.Opacity*100,5,100,1,"%",(r,v)=>r with{Opacity=v/100},"Opacity",100);
        var input=Card(main,"显示-交互","选择鼠标如何与桌宠交互。");
        var hit=new ComboBox{ItemsSource=new[]{"像素级命中","画布命中"},SelectedIndex=s.PixelHit?0:1,Width=160};hit.SelectionChanged+=(_,_)=>Set(r=>r with{PixelHit=hit.SelectedIndex==0});input.Add(Ui.Pair(Ui.Caption("命中模式","决定透明区域能否被点击"),hit));
        Flag(input,"点击穿透","开启后无法点击「"+petName+"」，按住 Ctrl 临时恢复",s.ClickThrough,(r,v)=>r with{ClickThrough=v});
        var motion=Section("移动","调整「"+petName+"」在桌面上的移动行为");Summary(()=>!Supports("standby/static_pool")?"强制开启":MoveOn()?"已开启":"已关闭");Group(motion,"动动动");
        var move=Card(motion,"移动","关闭后，「"+petName+"」将会静悄悄地",MoveOn(),(r,v)=>r with{Move=v});if(!Supports("standby/static_pool"))move.ForceOn("素材包缺少静止待机池，移动强制开启");
        Number(move,"最大速度","「"+petName+"」能跑多快",s.MaxSpeed,100,10000,50,"px/s",(r,v)=>r with{MaxSpeed=v},"MaxSpeed",1);
        move.AddGroup(Ui.Text("重重重",12,true));
        var gravity=Card(motion,"重力","万有引力，来自地心的吸引",s.GravityEnabled,(r,v)=>r with{GravityEnabled=v},dependency:MoveOn);
        motion.Children.Remove(gravity);move.AddChild(gravity);
        Number(gravity,"重力强度","重力越大，下坠越快",s.Gravity,0,10000,50,"px/s²",(r,v)=>r with{Gravity=v},"Gravity",1);Directions(gravity,"四边重力","上下互斥，左右互斥",s.GravityEdges,(r,v)=>r with{GravityEdges=v},true);
        move.AddGroup(Ui.Text("弹弹弹",12,true));
        var bounce=Card(motion,"边缘反弹","「"+petName+"」撞墙会反弹",s.BounceEnabled,(r,v)=>r with{BounceEnabled=v},dependency:MoveOn);motion.Children.Remove(bounce);move.AddChild(bounce);Number(bounce,"反弹系数","撞墙后弹回的力道",s.Bounce,0,1,.01,"",(r,v)=>r with{Bounce=v},"Bounce",1);Directions(bounce,"四边反弹","触碰边界时触发反弹",s.BounceEdges,(r,v)=>r with{BounceEdges=v});
        move.AddGroup(Ui.Text("抛抛抛",12,true));
        var thr=Card(motion,"抛掷",Supports("touch/roll")?"拖动「"+petName+"」，抛出去！":"素材包缺少抛掷动画",s.Throw&&Supports("touch/roll"),(r,v)=>r with{Throw=v},Supports("touch/roll"),dependency:MoveOn);
        motion.Children.Remove(thr);move.AddChild(thr);
        Number(thr,"抛掷系数","抛出去的速度倍率",s.ThrowCoefficient,0,5,.05,"倍",(r,v)=>r with{ThrowCoefficient=v},"ThrowCoefficient",1);
        move.AddGroup(Ui.Text("攀攀攀",12,true));
        var climb=Card(motion,"攀岩走壁","无视重力，踏壁而行，如履平地！",s.Climb,(r,v)=>r with{Climb=v},dependency:MoveOn);
        motion.Children.Remove(climb);move.AddChild(climb);
        var forage=Card(motion,"自动觅食",Supports(PetEngine.ForagePath)?"「"+petName+"」会沿着边缘自己找吃的":"素材包缺少觅食动画",s.Forage&&Supports(PetEngine.ForagePath),(r,v)=>r with{Forage=v},Supports(PetEngine.ForagePath),dependency:()=>MoveOn()&&current().Settings.Climb);
        Number(forage,"觅食速度加成","加成后「"+petName+"」将飞速奔向食物",s.ForageMultiplier,.1,5,.05,"倍",(r,v)=>r with{ForageMultiplier=v},"ForageMultiplier",1);
        var leap=Card(motion,"飞跃",Supports("climb/leap/leap")?"起跳，起飞！":"素材包缺少飞跃动画",s.Leap&&Supports("climb/leap/leap"),(r,v)=>r with{Leap=v},Supports("climb/leap/leap"),dependency:()=>MoveOn()&&current().Settings.Climb);
        motion.Children.Remove(forage);motion.Children.Remove(leap);climb.AddChild(forage);climb.AddChild(leap);
        Number(leap,"飞跃概率","概率越高，越容易起飞",s.LeapProbability*100,0,100,1,"%",(r,v)=>r with{LeapProbability=v/100},"LeapProbability",100);Number(leap,"飞跃速度","飞向目标边缘的速度。",s.LeapSpeed,100,5000,25,"px/s",(r,v)=>r with{LeapSpeed=v},"LeapSpeed",1);
        var climbing=Section("吸附边缘","贴在边缘休息一会儿");Summary(()=>!Supports("snap/pool")?"强制关闭":current().Settings.Snap?"已开启":"已关闭");
        var snap=Card(climbing,"吸附边缘",Supports("snap/pool")?"拖到屏幕边缘松手，会吸附边缘。按住鼠标拖出。":"素材包缺少吸附动画",s.Snap&&Supports("snap/pool"),(r,v)=>r with{Snap=v},Supports("snap/pool"));
        Number(snap,"吸附距离","数值越大，越容易被吸附在边缘",s.SnapDistance,0,500,5,"px",(r,v)=>r with{SnapDistance=v},"SnapDistance",1);Number(snap,"吸附速度","允许吸附的松手速度上限。",s.SnapSpeed,0,5000,25,"px/s",(r,v)=>r with{SnapSpeed=v},"SnapSpeed",1);Directions(snap,"四边吸附","选择可以贴在哪些边缘",s.SnapEdges,(r,v)=>r with{SnapEdges=v});
        var feeding=Section("投食","给「"+petName+"」喂点吃的");bool foodSupported=Supports("feed/food");Func<bool> foodOn=FoodOn;Summary(()=>!foodSupported?"强制关闭":FoodOn()?"已开启":"已关闭");Group(feeding,"吃吃吃");
        var feedMaster=Card(feeding,"投食",foodSupported?"桌面上刷新食物，可以拖动喂给「"+petName+"」":"素材包缺少食物动画",s.Feed&&foodSupported,(r,v)=>r with{Feed=v},foodSupported);
        Group(feeding,"显示",FoodOn);
        var food=Card(feeding,"食物显示","手动投食和随机掉落的食物共用这些设置",available:foodSupported,dependency:foodOn);
        Number(food,"大小","「"+foodName+"」的大小（新生成时生效）",s.FoodScale,.1,5,.05,"倍",(r,v)=>r with{FoodScale=v},"FoodScale",1);Number(food,"透明度","「"+foodName+"」的可见程度",s.FoodOpacity*100,5,100,1,"%",(r,v)=>r with{FoodOpacity=v/100},"FoodOpacity",100);
        Group(feeding,"刷新范围",FoodOn);var scope=Card(feeding,"刷新范围","决定食物会在哪里出现",dependency:foodOn);Flag(scope,"边缘刷新","开启后「"+foodName+"」出现在边缘",s.FoodAtEdge,(r,v)=>r with{FoodAtEdge=v});
        Group(feeding,"投食变大",FoodOn);var grow=Card(feeding,"投喂变大","投食成功后「"+petName+"」会变大",s.Grow,(r,v)=>r with{Grow=v},foodSupported,dependency:foodOn);
        Number(grow,"变大系数","每次投喂后胖一圈",s.FeedGrowth*100,0,100,1,"%",(r,v)=>r with{FeedGrowth=v/100},"FeedGrowth",100);Number(grow,"最大大小","长大到这个大小就不会再变大",s.MaxScale,.5,10,.05,"倍",(r,v)=>r with{MaxScale=v},"MaxScale",1);
        Group(feeding,"随机掉落",FoodOn);var auto=Card(feeding,"自动刷新食物","隔一段时间，桌面上会出现「"+foodName+"」",s.AutoFeed,(r,v)=>r with{AutoFeed=v},foodSupported,dependency:foodOn);
        auto.Add(Ui.Pair(Ui.Caption("频率","「"+foodName+"」的刷新间隔（秒）"),new RangeSlider(1,3600,s.FoodMinSeconds,s.FoodMaxSeconds,(lo,hi)=>Set(r=>r with{FoodMinSeconds=(int)lo,FoodMaxSeconds=(int)hi})),310));
        var limit=Card(feeding,"最大数量","手动投食与随机掉落共同遵守数量上限",dependency:foodOn);Number(limit,"最大数量","同屏「"+foodName+"」的上限",s.FoodLimit,1,100,1,"个",(r,v)=>r with{FoodLimit=(int)v},"FoodLimit",1);
        Group(feeding,"自动过期",FoodOn);var expiry=Card(feeding,"自动过期","一段时间后「"+foodName+"」会消失",dependency:foodOn);
        Number(expiry,"过期时间","0 表示不会自动消失",s.FoodLifetimeSeconds,0,86400,1,"秒",(r,v)=>r with{FoodLifetimeSeconds=(int)v},"FoodLifetimeSeconds",1);
        foreach(var child in feeding.Children.OfType<FrameworkElement>().SkipWhile(x=>!ReferenceEquals(x,feedMaster)).Skip(1).ToArray()){feeding.Children.Remove(child);if(child is SettingCard setting)feedMaster.AddChild(setting);else feedMaster.AddGroup(child);}
        var thinking=Section("碎碎念","「"+petName+"」的碎碎念");Summary(()=>!globalThinking?"强制关闭":current().Settings.Thinking?"已开启":"已关闭");var thinkingSwitch=Card(thinking,"碎碎念总开关","开启后，按素材包中的文本策略播放。",globalThinking&&s.Thinking,(r,v)=>r with{Thinking=v},available:globalThinking);if(!globalThinking)thinkingSwitch.SetWarning("全局设置 > 碎碎念已关闭");
        var rest=Card(thinking,"休息间隔","轮播消息结束后随机等待一段时间。",dependency:()=>globalThinking&&current().Settings.Thinking);
        rest.Add(Ui.Pair(Ui.Caption("频率","每次休息的最短与最长时间。"),new RangeSlider(1,300,s.ThoughtRestMin,s.ThoughtRestMax,(lo,hi)=>Set(r=>r with{ThoughtRestMin=(int)lo,ThoughtRestMax=(int)hi})),310));
        ThinkingStyleSettings.Add(thinking,()=>current().Settings.ThoughtStyle,patch=>Set(r=>r with{ThoughtStyle=patch(r.ThoughtStyle)}),pack?.Texts.Values.FirstOrDefault(x=>!string.IsNullOrWhiteSpace(x))??"碎碎念预览",()=>globalThinking&&current().Settings.Thinking,dependencies,()=>current().Settings,defaults&&applyHabits!=null?key=>applyHabits("item:"+key):null);
        foreach(var child in thinking.Children.OfType<FrameworkElement>().Where(x=>!ReferenceEquals(x,thinkingSwitch)).ToArray()){thinking.Children.Remove(child);if(child is SettingCard setting)thinkingSwitch.AddChild(setting);else thinkingSwitch.AddGroup(child);}
        var area=Section("屏幕适应","选择「"+petName+"」可以活动的屏幕范围");
        var display=Card(area,"显示器","拖动角色时仍可跨屏。");var options=new[]{new Option("","自动 / 主显示器")}.Concat(DesktopPlatform.Screens.Select(x=>new Option(x.Id,x.Id+(x.Primary?"（主显示器）":"")))).ToList();if(!options.Any(x=>x.Id==s.MonitorId))options.Add(new Option(s.MonitorId,s.MonitorId+"（未连接）"));
        var monitor=new ComboBox{ItemsSource=options,DisplayMemberPath="Label",SelectedValuePath="Id",SelectedValue=s.MonitorId,Width=190};monitor.SelectionChanged+=(_,_)=>Set(r=>r with{MonitorId=monitor.SelectedValue as string??""});display.Add(Ui.Pair(Ui.Caption("首选显示器","召唤时优先使用。"),monitor));
        var multi=new ComboBox{ItemsSource=new[]{"跨屏幕","关闭"},SelectedIndex=s.ScreenConnect?0:1,Width=160};multi.SelectionChanged+=(_,_)=>Set(r=>r with{ScreenConnect=multi.SelectedIndex==0});display.Add(Ui.Pair(Ui.Caption("多屏幕策略","跨屏幕时沿多个屏幕的活动区域移动；关闭后仍可手动拖到其他屏幕。"),multi));
        Flag(display,"左右循环","从最左侧出去，从最右侧回来，反之亦然。",s.HorizontalWrap,(r,v)=>r with{HorizontalWrap=v});
        Flag(display,"包含任务栏","允许角色活动到任务栏所在区域。",s.FullScreen,(r,v)=>r with{FullScreen=v});
        var offsets=Card(area,"四边偏移","正值向内收缩，负值向外扩展。");Number(offsets,"左侧","",s.OffsetLeft,-500,500,1,"px",(r,v)=>r with{OffsetLeft=v},"OffsetLeft",1);Number(offsets,"右侧","",s.OffsetRight,-500,500,1,"px",(r,v)=>r with{OffsetRight=v},"OffsetRight",1);Number(offsets,"顶部","",s.OffsetTop,-500,500,1,"px",(r,v)=>r with{OffsetTop=v},"OffsetTop",1);Number(offsets,"底部","",s.OffsetBottom,-500,500,1,"px",(r,v)=>r with{OffsetBottom=v},"OffsetBottom",1);
        var random=Section("随机模式","「"+petName+"」的行为会偷偷改变");Summary(()=>current().Settings.RandomMode.Enabled?"已开启":"已关闭");Group(random,"变变变");
        var randomConfig=s.RandomMode;var randomMaster=Card(random,"随机模式","开启后，选中的设置会隔一段时间随机改变",randomConfig.Enabled,(r,v)=>r with{RandomMode=r.RandomMode with{Enabled=v}});
        bool RandomOn()=>current().Settings.RandomMode.Enabled;
        Group(random,"随机项",RandomOn);var randomItems=Card(random,"随机项","只改变勾选的设置",dependency:RandomOn);
        foreach(var (flag,label,note) in new (RandomItems,string,string)[]{(RandomItems.Scale,"大小","0.6–2 倍"),(RandomItems.Opacity,"透明度","30%–100%"),(RandomItems.GravityEnabled,"重力","随机开启或关闭"),(RandomItems.MaxSpeed,"最大速度","500–6000 px/s"),(RandomItems.Gravity,"重力强度","250–3500 px/s²"),(RandomItems.Bounce,"反弹系数","0.45–0.95"),(RandomItems.VerticalGravity,"四边重力 · 上下",""),(RandomItems.HorizontalGravity,"四边重力 · 左右",""),(RandomItems.BounceTop,"四边反弹 · 上",""),(RandomItems.BounceBottom,"四边反弹 · 下",""),(RandomItems.BounceLeft,"四边反弹 · 左",""),(RandomItems.BounceRight,"四边反弹 · 右","")})
            Flag(randomItems,label,note,randomConfig.Items.HasFlag(flag),(r,v)=>r with{RandomMode=r.RandomMode with{Items=v?r.RandomMode.Items|flag:r.RandomMode.Items&~flag}});
        Group(random,"周期",RandomOn);var period=Card(random,"周期","两次随机之间的等待时间",dependency:RandomOn);period.Add(Ui.Pair(Ui.Caption("随机间隔","在最短和最长时间之间随机等待"),new RangeSlider(1,144,randomConfig.MinMinutes,randomConfig.MaxMinutes,(lo,hi)=>Set(r=>r with{RandomMode=r.RandomMode with{MinMinutes=(int)lo,MaxMinutes=(int)hi}}),"分钟"),310));
        foreach(var child in random.Children.OfType<FrameworkElement>().SkipWhile(x=>!ReferenceEquals(x,randomMaster)).Skip(1).ToArray()){random.Children.Remove(child);if(child is SettingCard setting)randomMaster.AddChild(setting);else randomMaster.AddGroup(child);}
        var limitsPage=Section("设置阈值",defaults?"新角色可以调整的数值范围":"限制当前角色设置中可调整的范围");
        foreach(var group in SettingLimits.Specs.GroupBy(x=>x.Group))
        {
            Group(limitsPage,group.Key);var limitsCard=Card(limitsPage,group.Key,"修改范围后，当前值会被限制在新范围内。",expanded:false);
            foreach(var spec in group)
            {
                var threshold=SettingLimits.Get(current().Settings,spec);var values=new StackPanel{Orientation=Orientation.Horizontal};double factor=spec.Key is "Opacity" or "FoodOpacity" or "FeedGrowth" or "LeapProbability" or "ThoughtStyle.Alpha" or "ThoughtStyle.BackgroundAlpha"?100:1;var lo=Ui.NumericBox(threshold.Min*factor);var hi=Ui.NumericBox(threshold.Max*factor);values.Children.Add(lo);values.Children.Add(Ui.Text(" — "));values.Children.Add(hi);
                values.Children.Add(Ui.Light("应用",()=>{if(!double.TryParse(lo.Text,System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out var a)||!double.TryParse(hi.Text,System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out var b)||!double.IsFinite(a)||!double.IsFinite(b)||a/factor<spec.Min||b/factor>spec.Max||(b-a)/factor<spec.Step-1e-9){MessageBox.Show("请输入有效范围："+spec.Min+"–"+spec.Max);return;}Set(r=>{var map=new Dictionary<string,SettingLimit>(r.Limits??new());map[spec.Key]=new(a/factor,b/factor);return SettingLimits.Clamp(r with{Limits=map});});reopen?.Invoke(selectedSection);}));
                limitsCard.Add(Ui.Pair(Ui.Caption(spec.Label,(spec.Min*factor)+"–"+(spec.Max*factor)+" "+(factor==100?"%":spec.Unit)),values));
            }
        }
        Select(Math.Clamp(initialSection,0,sections.Count-1));
    }
}
