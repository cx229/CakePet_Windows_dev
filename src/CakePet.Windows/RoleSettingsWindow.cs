using System.Windows;
using System.Windows.Controls;
using CakePet.Core;
namespace CakePet.Windows;

internal sealed class RoleSettingsWindow:Window
{
    private sealed record MonitorOption(string Id,string Label);
    public RoleEntry Result {get;private set;}
    public RoleSettingsWindow(RoleEntry entry,RolePackage? pack,bool defaults=false)
    {
        Result=entry;Title=defaults?"用户习惯 · 新角色默认设置":"角色设置 · "+entry.Name;Width=760;Height=660;MinWidth=620;MinHeight=400;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        var root=new DockPanel{Margin=new Thickness(20)};
        var bottom=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,12,0,0)};
        DockPanel.SetDock(bottom,Dock.Bottom);root.Children.Add(bottom);
        var save=new Button{Content="保存",Padding=new Thickness(20,7,20,7),IsDefault=true};var cancel=new Button{Content="取消",Padding=new Thickness(20,7,20,7),IsCancel=true,Margin=new Thickness(8,0,0,0)};bottom.Children.Add(save);bottom.Children.Add(cancel);
        var tabs=new TabControl();root.Children.Add(tabs);Content=root;
        var panel=new StackPanel();
        void Section(string title){panel=new StackPanel{Margin=new Thickness(16)};tabs.Items.Add(new TabItem{Header=title,Content=new ScrollViewer{Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto}});}
        bool Supports(string path)=>pack?.Supports(path)??true;
        var values=new Dictionary<string,(TextBox Box,double Min,double Max)>();
        TextBox Number(string title,double value,double min,double max){var box=Field($"{title}（{min}–{max}）",value.ToString(System.Globalization.CultureInfo.InvariantCulture));values[title]=(box,min,max);return box;}
        double Read(TextBox box)=>double.Parse(box.Text,System.Globalization.CultureInfo.InvariantCulture);
        Func<Edge> Directions(string title,Edge value){panel.Children.Add(new TextBlock{Text=title,Margin=new Thickness(0,10,0,4)});var row=new WrapPanel();panel.Children.Add(row);var checks=new Dictionary<Edge,CheckBox>();foreach(var (edge,label) in new[]{(Edge.Bottom,"下"),(Edge.Left,"左"),(Edge.Right,"右"),(Edge.Top,"上")}){var c=new CheckBox{Content=label,IsChecked=value.HasFlag(edge),Margin=new Thickness(0,0,18,0)};checks.Add(edge,c);row.Children.Add(c);}return ()=>checks.Where(x=>x.Value.IsChecked==true).Aggregate(Edge.None,(mask,x)=>mask|x.Key);}
        TextBox Field(string title,string value){panel.Children.Add(new TextBlock{Text=title,Margin=new Thickness(0,5,0,3)});var box=new TextBox{Text=value,Padding=new Thickness(6)};panel.Children.Add(box);return box;}
        CheckBox Flag(string title,bool value,bool available=true){var c=new CheckBox{Content=available?title:title+"（素材包不支持）",IsChecked=available&&value,IsEnabled=available,Margin=new Thickness(0,5,0,5)};panel.Children.Add(c);return c;}
        var s=entry.Settings.Normalize();
        Section("基本信息");var name=Field("名称",entry.Name);if(defaults)name.Visibility=Visibility.Collapsed;var count=Field("召唤数量（1–30）",s.Count.ToString());
        Section("外观与交互");var scale=Field("大小倍率（0.5–3）",s.Scale.ToString(System.Globalization.CultureInfo.InvariantCulture));var opacity=Field("不透明度（5–100%）",(s.Opacity*100).ToString("0"));
        var pixel=Flag("像素级命中",s.PixelHit);var pass=Flag("点击穿透（Ctrl 临时恢复）",s.ClickThrough);var thinking=Flag("碎碎念",s.Thinking);
        Section("移动与活动范围");var move=Flag("移动总开关",s.Move,Supports("standby/static_pool"));if(!move.IsEnabled){move.IsChecked=true;move.Content="移动总开关（缺少静止待机池，强制开启）";}
        var climb=Flag("攀岩走壁",s.Climb);var leap=Flag("好动飞跃",s.Leap,Supports("climb/leap/leap"));var thr=Flag("抛掷",s.Throw,Supports("touch/roll"));var snap=Flag("吸附",s.Snap,Supports("snap/pool"));var full=Flag("包含任务栏的活动范围",s.FullScreen);
        var gravityOn=Flag("重力",s.GravityEnabled);var bounceOn=Flag("反弹",s.BounceEnabled);
        var gravity=Number("重力加速度 px/s²",s.Gravity,0,10000);var speed=Number("最高速度 px/s",s.MaxSpeed,100,10000);var bounce=Number("反弹系数",s.Bounce,0,1);var coefficient=Number("抛掷系数",s.ThrowCoefficient,0,5);
        var gravityEdges=Directions("重力方向",s.GravityEdges);var bounceEdges=Directions("反弹方向",s.BounceEdges);
        Section("吸附与飞跃");var snapSpeed=Number("吸附速度 px/s",s.SnapSpeed,0,5000);var snapDistance=Number("吸附距离 px",s.SnapDistance,0,500);var snapEdges=Directions("吸附方向",s.SnapEdges);var leapChance=Number("飞跃概率",s.LeapProbability,0,1);var leapSpeed=Number("飞跃速度 px/s",s.LeapSpeed,100,5000);
        Section("活动边界");var left=Number("左侧偏移 px",s.OffsetLeft,-500,500);var right=Number("右侧偏移 px",s.OffsetRight,-500,500);var top=Number("顶部偏移 px",s.OffsetTop,-500,500);var bottomOffset=Number("底部偏移 px",s.OffsetBottom,-500,500);
        panel.Children.Add(new TextBlock{Text="首选显示器（拖动角色仍可跨屏）"});var monitor=new ComboBox{DisplayMemberPath="Label",SelectedValuePath="Id",Margin=new Thickness(0,6,0,6)};
        var options=new[]{new MonitorOption("","自动 / 主显示器")}.Concat(DesktopPlatform.Screens.Select(x=>new MonitorOption(x.Id,x.Id+(x.Primary?"（主显示器）":"")))).ToList();if(!options.Any(x=>x.Id==s.MonitorId))options.Add(new MonitorOption(s.MonitorId,s.MonitorId+"（当前未连接）"));monitor.ItemsSource=options;monitor.SelectedValue=s.MonitorId;panel.Children.Add(monitor);
        Section("投食");var feed=Flag("投食",s.Feed,Supports("feed/food"));var auto=Flag("自动刷新食物",s.AutoFeed,Supports("feed/food"));var forage=Flag("自动觅食",s.Forage,Supports(PetEngine.ForagePath));var grow=Flag("投喂变大",s.Grow);
        var foodMin=Number("最短刷新间隔 秒",s.FoodMinSeconds,1,3600);var foodMax=Number("最长刷新间隔 秒",s.FoodMaxSeconds,1,3600);var life=Number("食物寿命 秒（0 不过期）",s.FoodLifetimeSeconds,0,86400);var limit=Number("食物数量上限",s.FoodLimit,1,100);var foodScale=Number("食物大小倍率",s.FoodScale,.1,5);var foodOpacity=Number("食物不透明度",s.FoodOpacity,.05,1);var foodEdge=Flag("沿边缘刷新",s.FoodAtEdge);var growth=Number("每次投喂增长倍率",s.FeedGrowth,0,1);var maxScale=Number("最大大小倍率",s.MaxScale,.5,10);var forageSpeed=Number("觅食速度倍率",s.ForageMultiplier,.1,5);
        tabs.SelectedIndex=0;
        save.Click+=(_,_)=>{
            foreach(var item in values){var (box,min,max)=item.Value;if(!double.TryParse(box.Text,System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out var value)||!double.IsFinite(value)||value<min||value>max){MessageBox.Show(this,"请检查："+item.Key);return;}}
            if(Read(foodMin)>Read(foodMax)||new[]{foodMin,foodMax,life,limit}.Any(b=>Read(b)!=Math.Truncate(Read(b)))){MessageBox.Show(this,"刷新间隔、寿命与数量必须是有效整数，最短间隔不能超过最长间隔。");return;}
            if(string.IsNullOrWhiteSpace(name.Text)||!int.TryParse(count.Text,out var n)||n<1||n>30||!double.TryParse(scale.Text,System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out var z)||!double.IsFinite(z)||z<.5||z>3||!double.TryParse(opacity.Text,out var o)||!double.IsFinite(o)||o<5||o>100){MessageBox.Show(this,"请检查名称、数量、大小和不透明度。");return;}
            Result=entry with{Name=name.Text.Trim(),Settings=s with{Count=n,Scale=z,Opacity=o/100,PixelHit=pixel.IsChecked==true,ClickThrough=pass.IsChecked==true,Thinking=thinking.IsChecked==true,Move=move.IsChecked==true,Climb=climb.IsChecked==true,Leap=leap.IsChecked==true,Throw=thr.IsChecked==true,Snap=snap.IsChecked==true,FullScreen=full.IsChecked==true,Feed=feed.IsChecked==true,AutoFeed=auto.IsChecked==true,Forage=forage.IsChecked==true,Grow=grow.IsChecked==true,GravityEnabled=gravityOn.IsChecked==true,BounceEnabled=bounceOn.IsChecked==true,Gravity=Read(gravity),MaxSpeed=Read(speed),Bounce=Read(bounce),ThrowCoefficient=Read(coefficient),GravityEdges=gravityEdges(),BounceEdges=bounceEdges(),SnapEdges=snapEdges(),SnapSpeed=Read(snapSpeed),SnapDistance=Read(snapDistance),LeapProbability=Read(leapChance),LeapSpeed=Read(leapSpeed),OffsetLeft=Read(left),OffsetRight=Read(right),OffsetTop=Read(top),OffsetBottom=Read(bottomOffset),MonitorId=monitor.SelectedValue as string??"",FoodMinSeconds=(int)Read(foodMin),FoodMaxSeconds=(int)Read(foodMax),FoodLifetimeSeconds=(int)Read(life),FoodLimit=(int)Read(limit),FoodScale=Read(foodScale),FoodOpacity=Read(foodOpacity),FoodAtEdge=foodEdge.IsChecked==true,FeedGrowth=Read(growth),MaxScale=Read(maxScale),ForageMultiplier=Read(forageSpeed)}};DialogResult=true;
        };
    }
}
