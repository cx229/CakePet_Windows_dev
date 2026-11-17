using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using CakePet.Core;
namespace CakePet.Windows;
internal sealed record ThoughtMessage(string Key,string Name,string Text,ThinkingStyle Style,long Order);
internal sealed class ThinkingPresenter:IDisposable
{
    private sealed record Item(Border Bubble,TextBlock Text,ThoughtMessage Message);
    private readonly StackPanel panel=new(){HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center};
    private readonly Window window;
    private readonly Dictionary<string,Item> items=new();
    private ThoughtMessage[] previous=Array.Empty<ThoughtMessage>();
    private ThinkingLocation previousLocation=(ThinkingLocation)(-1);
    private long lastLayout;
    public ThinkingPresenter()
    {
        window=new Window{Width=400,Height=44,WindowStyle=WindowStyle.None,ResizeMode=ResizeMode.NoResize,AllowsTransparency=true,Background=Brushes.Transparent,ShowInTaskbar=false,ShowActivated=false,Topmost=true,IsHitTestVisible=false,Content=panel};
        window.SourceInitialized+=(_,_)=>NativeStyles.NonActivating(window,true);
    }
    public void Render(IEnumerable<ThoughtMessage> thoughts,ThinkingLocation location,bool single)
    {
        var entries=thoughts.OrderByDescending(x=>x.Order).ToArray();
        var now=Environment.TickCount64;bool changed=!entries.SequenceEqual(previous)||location!=previousLocation;
        if(!changed&&now-lastLayout<500)return;
        previous=entries;previousLocation=location;lastLayout=now;
        var taskbar=location==ThinkingLocation.PrimaryBottom?null:DesktopPlatform.Taskbar();
        bool barMode=taskbar is { } b&&b.Bar.Right-b.Bar.Left>b.Bar.Bottom-b.Bar.Top&&b.Bar.Bottom-b.Bar.Top>=20&&(location!=ThinkingLocation.TrayLeft||b.Tray.HasValue);
        panel.Orientation=barMode?Orientation.Horizontal:Orientation.Vertical;
        foreach(var key in items.Keys.Except(entries.Select(x=>x.Key)).ToArray())
        {
            var item=items[key];items.Remove(key);
            if(item.Message.Style.FlashOut&&window.IsVisible){var fade=new DoubleAnimation(0,TimeSpan.FromMilliseconds(160));fade.Completed+=(_,_)=>{panel.Children.Remove(item.Bubble);if(items.Count==0&&panel.Children.Count==0)window.Hide();};item.Bubble.BeginAnimation(UIElement.OpacityProperty,fade);}
            else panel.Children.Remove(item.Bubble);
        }
        foreach(var message in entries)
        {
            var style=message.Style.Normalize();
            if(!items.TryGetValue(message.Key,out var item))
            {
                var label=new TextBlock{TextAlignment=TextAlignment.Center,VerticalAlignment=VerticalAlignment.Center,TextTrimming=TextTrimming.CharacterEllipsis};
                var bubble=new Border{CornerRadius=new CornerRadius(7),Padding=new Thickness(9,3,9,3),Child=label,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center};
                item=new Item(bubble,label,message);items.Add(message.Key,item);panel.Children.Add(bubble);
                if(style.FlashIn)bubble.BeginAnimation(UIElement.OpacityProperty,new DoubleAnimation(0,1,TimeSpan.FromMilliseconds(160)));
            }
            item.Text.Text=single?message.Text:message.Name+"："+message.Text;item.Text.FontSize=style.Size;item.Text.Foreground=new SolidColorBrush(ThinkingStyleSettings.ColorOf(style.Color,style.Alpha));item.Text.TextWrapping=barMode?TextWrapping.NoWrap:TextWrapping.Wrap;
            item.Bubble.Background=style.Background?new SolidColorBrush(ThinkingStyleSettings.ColorOf(style.BackgroundColor,style.BackgroundAlpha)):Brushes.Transparent;
            item.Bubble.Margin=barMode?new Thickness(3,0,3+Math.Max(0,style.Offset),0):new Thickness(0,2,0,2+Math.Max(0,style.Offset));
            items[message.Key]=item with{Message=message};
        }
        if(entries.Length==0){if(panel.Children.Count==0&&window.IsVisible)window.Hide();return;}
        var work=DesktopPlatform.Primary.Work;var dpi=window.IsVisible?DesktopPlatform.DpiScale(window):1;
        if(barMode&&taskbar is {} data)
        {
            var bar=data.Bar;var right=location==ThinkingLocation.TrayLeft?data.Tray!.Value.Left-8:Math.Min(data.Tray?.Left??bar.Right,bar.Left+Math.Min(440,(bar.Right-bar.Left)*.35));
            var left=bar.Left+8;var available=right-left;
            if(available>=120)
            {
                foreach(var item in items.Values)item.Text.MaxWidth=Math.Max(60,available/dpi/entries.Length-24);
                panel.Measure(new Size(available/dpi,(bar.Bottom-bar.Top)/dpi));
                var width=Math.Min(available,Math.Max(80,panel.DesiredSize.Width*dpi));var height=Math.Min(bar.Bottom-bar.Top-2,Math.Max(20,panel.DesiredSize.Height*dpi));
                Place(location==ThinkingLocation.TrayLeft?right-width:left,bar.Top+(bar.Bottom-bar.Top-height)/2,width,height);return;
            }
        }
        panel.Orientation=Orientation.Vertical;var w=Math.Max(1,Math.Min(work.Right-work.Left-16,Math.Max(360,Math.Min(740,(work.Right-work.Left)*.6))));
        foreach(var item in items.Values){item.Text.MaxWidth=Math.Max(1,w/dpi-30);item.Text.TextWrapping=TextWrapping.Wrap;}
        panel.Measure(new Size(w/dpi,double.PositiveInfinity));
        var h=Math.Max(1,Math.Min(Math.Max(44,panel.DesiredSize.Height*dpi),Math.Max(44,(work.Bottom-work.Top)*.45)));
        Place(work.Left+(work.Right-work.Left-w)/2,work.Bottom-h-6,w,h);
    }
    private void Place(double x,double y,double width,double height)
    {
        if(width<=0||height<=0)return;if(!window.IsVisible)window.Show();
        var scale=DesktopPlatform.DpiScale(window);window.Width=width/scale;window.Height=height/scale;DesktopPlatform.Place(window,x,y,width,height);
    }
    public void Dispose()=>window.Close();
}
