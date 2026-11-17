using System.Windows;
using System.Windows.Controls;
using CakePet.Core;
namespace CakePet.Windows;

internal sealed class RoleSettingsPage:UserControl
{
    private sealed record MonitorOption(string Id,string Label);
    public RoleEntry Result {get;private set;}
    public RoleSettingsPage(RoleEntry entry,RolePackage? pack,Action<RoleEntry> changed,Action back,bool defaults=false)
    {
        Result=entry;
        var root=new Grid();root.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(178)});root.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});Content=root;
        var nav=new StackPanel{Margin=new Thickness(12,18,12,12)};root.Children.Add(nav);
        nav.Children.Add(Ui.Button("‹ 返回",back));var title=Ui.Text(defaults?"新角色默认设置":entry.Name,20);title.Margin=new Thickness(0,18,0,14);nav.Children.Add(title);
        var host=new ContentControl{Margin=new Thickness(18,12,18,12)};Grid.SetColumn(host,1);root.Children.Add(host);
        var panel=new StackPanel();var sections=new List<(string Name,StackPanel Panel)>();var navButtons=new List<Button>();
        void Select(int i){if(host.Content is ScrollViewer old)old.Content=null;host.Content=new ScrollViewer{Content=sections[i].Panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};for(int n=0;n<navButtons.Count;n++)navButtons[n].Background=Ui.Brush(n==i?"AccentSoft":"Canvas");}
        void Section(string title){panel=new StackPanel{MaxWidth=850};var header=Ui.Text(title,24);header.Margin=new Thickness(0,0,0,16);panel.Children.Add(header);int index=sections.Count;sections.Add((title,panel));var b=Ui.Button(title,()=>Select(index));b.HorizontalContentAlignment=HorizontalAlignment.Left;b.Margin=new Thickness(0,3,0,3);nav.Children.Add(b);navButtons.Add(b);}
        Action notify=()=>{};
        StackPanel? group=null;
        StackPanel Target()=>group??panel;
        void Group(string title,string note,CheckBox? toggle=null){var content=new StackPanel();if(toggle!=null){content.Children.Add(toggle);}else{var heading=Ui.Text(title,16);heading.FontWeight=FontWeights.SemiBold;content.Children.Add(heading);}if(note.Length>0){var remark=Ui.Text(note,12,true);remark.Margin=new Thickness(0,4,0,10);content.Children.Add(remark);}group=new StackPanel();content.Children.Add(group);panel.Children.Add(Ui.Card(content));if(toggle!=null){var children=group;void Update()=>children.Visibility=toggle.IsChecked==true?Visibility.Visible:Visibility.Collapsed;toggle.Checked+=(_,_)=>Update();toggle.Unchecked+=(_,_)=>Update();Update();}}
        void EndGroup()=>group=null;
        bool Supports(string path)=>pack?.Supports(path)??true;
        var values=new Dictionary<string,(TextBox Box,double Min,double Max)>();
        TextBox Number(string title,double value,double min,double max){var box=new TextBox{Text=value.ToString(System.Globalization.CultureInfo.InvariantCulture)};values[title]=(box,min,max);double step=max-min<=1?.01:max-min<=10?.1:max-min<=100?1:max-min<=1000?5:10;if(title.Contains("秒")||title.Contains("数量"))step=1;var row=Ui.NumberRow(title,box,min,max,step,()=>notify());Target().Children.Add(group==null?Ui.Card(row):row);return box;}
        double Read(TextBox box)=>double.Parse(box.Text,System.Globalization.CultureInfo.InvariantCulture);
        Func<Edge> Directions(string title,Edge value){Target().Children.Add(Ui.Text(title));var row=new WrapPanel{Margin=new Thickness(0,6,0,8)};Target().Children.Add(row);var checks=new Dictionary<Edge,CheckBox>();foreach(var (edge,label) in new[]{(Edge.Bottom,"下"),(Edge.Left,"左"),(Edge.Right,"右"),(Edge.Top,"上")}){var c=new CheckBox{Content=label,IsChecked=value.HasFlag(edge),Margin=new Thickness(0,0,18,0)};c.Style=(Style)Application.Current.FindResource("Direction");c.Click+=(_,_)=>notify();checks.Add(edge,c);row.Children.Add(c);}return ()=>checks.Where(x=>x.Value.IsChecked==true).Aggregate(Edge.None,(mask,x)=>mask|x.Key);}
        TextBox Field(string title,string value){var group=new StackPanel();group.Children.Add(Ui.Text(title));var box=new TextBox{Text=value,Margin=new Thickness(0,8,0,0)};group.Children.Add(box);panel.Children.Add(Ui.Card(group));box.LostKeyboardFocus+=(_,_)=>notify();return box;}
        CheckBox Flag(string title,bool value,bool available=true,bool ownCard=true){var c=new CheckBox{Content=available?title:title+"（素材包不支持）",IsChecked=available&&value,IsEnabled=available,FontSize=14};if(ownCard)Target().Children.Add(group==null?Ui.Card(c):c);c.Click+=(_,_)=>notify();return c;}
        var s=entry.Settings.Normalize();
        Section("基本信息");var name=defaults?new TextBox{Text=entry.Name}:Field("名称",entry.Name);var count=Number("召唤数量",s.Count,1,30);
        Section("外观与交互");var scale=Number("大小倍率",s.Scale,.5,3);var opacity=Number("不透明度 %",s.Opacity*100,5,100);
        var pixel=Flag("像素级命中",s.PixelHit);var pass=Flag("点击穿透（Ctrl 临时恢复）",s.ClickThrough);var thinking=Flag("碎碎念",s.Thinking);
        Section("移动与活动范围");var move=Flag("移动总开关",s.Move,Supports("standby/static_pool"));if(!move.IsEnabled){move.IsChecked=true;move.Content="移动总开关（缺少静止待机池，强制开启）";}
        var climb=Flag("攀岩走壁",s.Climb);var leap=Flag("好动飞跃",s.Leap,Supports("climb/leap/leap"));var thr=Flag("抛掷",s.Throw,Supports("touch/roll"));var snap=Flag("吸附",s.Snap,Supports("snap/pool"));var full=Flag("包含任务栏的活动范围",s.FullScreen);
        var gravityOn=Flag("重力",s.GravityEnabled,ownCard:false);Group("重力","调整角色下落的力度和方向。",gravityOn);
        var gravity=Number("重力加速度",s.Gravity,0,10000);var gravityEdges=Directions("重力方向",s.GravityEdges);EndGroup();
        Group("抛掷","控制松手后的速度。");var speed=Number("最高速度",s.MaxSpeed,100,10000);var coefficient=Number("抛掷系数",s.ThrowCoefficient,0,5);EndGroup();
        var bounceOn=Flag("反弹",s.BounceEnabled,ownCard:false);Group("反弹","碰到边缘时弹回来。",bounceOn);var bounce=Number("反弹系数",s.Bounce,0,1);var bounceEdges=Directions("反弹方向",s.BounceEdges);EndGroup();
        Section("吸附与飞跃");Group("吸附","控制角色靠近边缘时的吸附条件。");var snapSpeed=Number("吸附速度 px/s",s.SnapSpeed,0,5000);var snapDistance=Number("吸附距离 px",s.SnapDistance,0,500);var snapEdges=Directions("吸附方向",s.SnapEdges);EndGroup();Group("好动飞跃","调整角色主动飞跃的频率与速度。");var leapChance=Number("飞跃概率",s.LeapProbability,0,1);var leapSpeed=Number("飞跃速度 px/s",s.LeapSpeed,100,5000);EndGroup();
        Section("活动边界");Group("四边偏移","正值向内收缩，负值向外扩展。");var left=Number("左侧偏移 px",s.OffsetLeft,-500,500);var right=Number("右侧偏移 px",s.OffsetRight,-500,500);var top=Number("顶部偏移 px",s.OffsetTop,-500,500);var bottomOffset=Number("底部偏移 px",s.OffsetBottom,-500,500);EndGroup();
        panel.Children.Add(new TextBlock{Text="首选显示器（拖动角色仍可跨屏）"});var monitor=new ComboBox{DisplayMemberPath="Label",SelectedValuePath="Id",Margin=new Thickness(0,6,0,6)};
        var options=new[]{new MonitorOption("","自动 / 主显示器")}.Concat(DesktopPlatform.Screens.Select(x=>new MonitorOption(x.Id,x.Id+(x.Primary?"（主显示器）":"")))).ToList();if(!options.Any(x=>x.Id==s.MonitorId))options.Add(new MonitorOption(s.MonitorId,s.MonitorId+"（当前未连接）"));monitor.ItemsSource=options;monitor.SelectedValue=s.MonitorId;panel.Children.Add(monitor);monitor.SelectionChanged+=(_,_)=>notify();
        Section("投食");var feed=Flag("投食",s.Feed,Supports("feed/food"));var auto=Flag("自动刷新食物",s.AutoFeed,Supports("feed/food"));var forage=Flag("自动觅食",s.Forage,Supports(PetEngine.ForagePath));var grow=Flag("投喂变大",s.Grow);
        Group("食物刷新","修改刷新间隔、数量和寿命。");var foodMin=Number("最短刷新间隔 秒",s.FoodMinSeconds,1,3600);var foodMax=Number("最长刷新间隔 秒",s.FoodMaxSeconds,1,3600);var life=Number("食物寿命 秒（0 不过期）",s.FoodLifetimeSeconds,0,86400);var limit=Number("食物数量上限",s.FoodLimit,1,100);EndGroup();Group("食物外观","大小和寿命应用于新生成的食物。");var foodScale=Number("食物大小倍率",s.FoodScale,.1,5);var foodOpacity=Number("食物不透明度",s.FoodOpacity,.05,1);var foodEdge=Flag("沿边缘刷新",s.FoodAtEdge);EndGroup();Group("投喂成长与觅食","控制喂食后的增长和觅食速度。");var growth=Number("每次投喂增长倍率",s.FeedGrowth,0,1);var maxScale=Number("最大大小倍率",s.MaxScale,.5,10);var forageSpeed=Number("觅食速度倍率",s.ForageMultiplier,.1,5);EndGroup();
        Select(0);
        notify=()=>{
            foreach(var item in values){var (box,min,max)=item.Value;if(!double.TryParse(box.Text,System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out var value)||!double.IsFinite(value)||value<min||value>max){return;}}
            if(Read(foodMin)>Read(foodMax))return;
            if(string.IsNullOrWhiteSpace(name.Text)||!int.TryParse(count.Text,out var n)||n<1||n>30||!double.TryParse(scale.Text,System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out var z)||!double.IsFinite(z)||z<.5||z>3||!double.TryParse(opacity.Text,out var o)||!double.IsFinite(o)||o<5||o>100){return;}
            Result=entry with{Name=name.Text.Trim(),Settings=s with{Count=n,Scale=z,Opacity=o/100,PixelHit=pixel.IsChecked==true,ClickThrough=pass.IsChecked==true,Thinking=thinking.IsChecked==true,Move=move.IsChecked==true,Climb=climb.IsChecked==true,Leap=leap.IsChecked==true,Throw=thr.IsChecked==true,Snap=snap.IsChecked==true,FullScreen=full.IsChecked==true,Feed=feed.IsChecked==true,AutoFeed=auto.IsChecked==true,Forage=forage.IsChecked==true,Grow=grow.IsChecked==true,GravityEnabled=gravityOn.IsChecked==true,BounceEnabled=bounceOn.IsChecked==true,Gravity=Read(gravity),MaxSpeed=Read(speed),Bounce=Read(bounce),ThrowCoefficient=Read(coefficient),GravityEdges=gravityEdges(),BounceEdges=bounceEdges(),SnapEdges=snapEdges(),SnapSpeed=Read(snapSpeed),SnapDistance=Read(snapDistance),LeapProbability=Read(leapChance),LeapSpeed=Read(leapSpeed),OffsetLeft=Read(left),OffsetRight=Read(right),OffsetTop=Read(top),OffsetBottom=Read(bottomOffset),MonitorId=monitor.SelectedValue as string??"",FoodMinSeconds=(int)Read(foodMin),FoodMaxSeconds=(int)Read(foodMax),FoodLifetimeSeconds=(int)Read(life),FoodLimit=(int)Read(limit),FoodScale=Read(foodScale),FoodOpacity=Read(foodOpacity),FoodAtEdge=foodEdge.IsChecked==true,FeedGrowth=Read(growth),MaxScale=Read(maxScale),ForageMultiplier=Read(forageSpeed)}};changed(Result);
        };
    }
}
