using System.Windows;
using System.Windows.Controls;
using CakePet.Core;
namespace CakePet.Windows;

internal sealed class RoleSettingsWindow:Window
{
    public RoleEntry Result {get;private set;}
    public RoleSettingsWindow(RoleEntry entry,RolePackage pack)
    {
        Result=entry;Title="角色设置 · "+entry.Name;Width=520;Height=660;MinWidth=420;MinHeight=400;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        var root=new DockPanel{Margin=new Thickness(20)};
        var bottom=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,12,0,0)};
        DockPanel.SetDock(bottom,Dock.Bottom);root.Children.Add(bottom);
        var save=new Button{Content="保存",Padding=new Thickness(20,7,20,7),IsDefault=true};var cancel=new Button{Content="取消",Padding=new Thickness(20,7,20,7),IsCancel=true,Margin=new Thickness(8,0,0,0)};bottom.Children.Add(save);bottom.Children.Add(cancel);
        var panel=new StackPanel();root.Children.Add(new ScrollViewer{Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});Content=root;
        void Section(string title)=>panel.Children.Add(new TextBlock{Text=title,FontSize=18,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,14,0,8)});
        TextBox Field(string title,string value){panel.Children.Add(new TextBlock{Text=title,Margin=new Thickness(0,5,0,3)});var box=new TextBox{Text=value,Padding=new Thickness(6)};panel.Children.Add(box);return box;}
        CheckBox Flag(string title,bool value,bool available=true){var c=new CheckBox{Content=available?title:title+"（素材包不支持）",IsChecked=available&&value,IsEnabled=available,Margin=new Thickness(0,5,0,5)};panel.Children.Add(c);return c;}
        var s=entry.Settings.Normalize();
        Section("基本信息");var name=Field("名称",entry.Name);var count=Field("召唤数量（1–30）",s.Count.ToString());
        Section("外观与交互");var scale=Field("大小倍率（0.5–3）",s.Scale.ToString(System.Globalization.CultureInfo.InvariantCulture));var opacity=Field("不透明度（5–100%）",(s.Opacity*100).ToString("0"));
        var pixel=Flag("像素级命中",s.PixelHit);var pass=Flag("点击穿透（Ctrl 临时恢复）",s.ClickThrough);var thinking=Flag("碎碎念",s.Thinking);
        Section("移动与活动范围");var move=Flag("移动总开关",s.Move,pack.Supports("standby/static_pool"));if(!move.IsEnabled){move.IsChecked=true;move.Content="移动总开关（缺少静止待机池，强制开启）";}
        var climb=Flag("攀岩走壁",s.Climb);var leap=Flag("好动飞跃",s.Leap,pack.Supports("climb/leap/leap"));var thr=Flag("抛掷",s.Throw,pack.Supports("touch/roll"));var snap=Flag("吸附",s.Snap,pack.Supports("snap/pool"));var full=Flag("包含任务栏的活动范围",s.FullScreen);
        Section("投食");var feed=Flag("投食",s.Feed,pack.Supports("feed/food"));var auto=Flag("自动刷新食物",s.AutoFeed,pack.Supports("feed/food"));var forage=Flag("自动觅食",s.Forage,pack.Supports(PetEngine.ForagePath));var grow=Flag("投喂变大",s.Grow);
        save.Click+=(_,_)=>{
            if(string.IsNullOrWhiteSpace(name.Text)||!int.TryParse(count.Text,out var n)||n<1||n>30||!double.TryParse(scale.Text,System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out var z)||!double.IsFinite(z)||z<.5||z>3||!double.TryParse(opacity.Text,out var o)||!double.IsFinite(o)||o<5||o>100){MessageBox.Show(this,"请检查名称、数量、大小和不透明度。");return;}
            Result=entry with{Name=name.Text.Trim(),Settings=s with{Count=n,Scale=z,Opacity=o/100,PixelHit=pixel.IsChecked==true,ClickThrough=pass.IsChecked==true,Thinking=thinking.IsChecked==true,Move=move.IsChecked==true,Climb=climb.IsChecked==true,Leap=leap.IsChecked==true,Throw=thr.IsChecked==true,Snap=snap.IsChecked==true,FullScreen=full.IsChecked==true,Feed=feed.IsChecked==true,AutoFeed=auto.IsChecked==true,Forage=forage.IsChecked==true,Grow=grow.IsChecked==true}};DialogResult=true;
        };
    }
}
