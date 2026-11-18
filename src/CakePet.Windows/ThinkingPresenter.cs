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
    private readonly Grid panel=new(){ClipToBounds=true};
    private Window? window;
    private readonly Dictionary<string,Item> items=new();
    private ThoughtMessage[] previous=Array.Empty<ThoughtMessage>();
    private ThinkingLocation previousLocation=(ThinkingLocation)(-1);
    private ThinkingDisplayPolicy previousPolicy=ThinkingDisplayPolicy.None;
    private long lastLayout;
    private Window EnsureWindow()
    {
        if(window!=null)return window;
        window=new Window{Width=400,Height=44,WindowStyle=WindowStyle.None,ResizeMode=ResizeMode.NoResize,AllowsTransparency=true,Background=Brushes.Transparent,ShowInTaskbar=false,ShowActivated=false,Topmost=true,IsHitTestVisible=false,Content=panel};
        window.SourceInitialized+=(_,_)=>{if(window!=null)NativeStyles.NonActivating(window,true);};return window;
    }
    public void Clear(){items.Clear();panel.Children.Clear();previous=Array.Empty<ThoughtMessage>();if(window?.IsVisible==true)window.Hide();}
    public void Render(IEnumerable<ThoughtMessage> thoughts,ThinkingLocation location,ThinkingDisplayPolicy policy)
    {
        if(policy==ThinkingDisplayPolicy.None){if(window?.IsVisible==true||items.Count>0)Clear();previousPolicy=policy;return;}
        var entries=thoughts.OrderByDescending(x=>x.Order).ToArray();
        if(policy==ThinkingDisplayPolicy.Latest)entries=entries.Take(1).ToArray();
        var now=Environment.TickCount64;bool policyChanged=policy!=previousPolicy;
        if(!policyChanged&&entries.SequenceEqual(previous)&&location==previousLocation&&now-lastLayout<500)return;
        if(policyChanged)Clear();
        previous=entries;previousLocation=location;previousPolicy=policy;lastLayout=now;
        foreach(var key in items.Keys.Except(entries.Select(x=>x.Key)).ToArray())
        {
            var item=items[key];items.Remove(key);
            if(!policyChanged&&(policy==ThinkingDisplayPolicy.Overlap||entries.Length==0)&&item.Message.Style.FlashOut&&window?.IsVisible==true)
            {
                var fade=new DoubleAnimation(0,TimeSpan.FromMilliseconds(160));fade.Completed+=(_,_)=>{panel.Children.Remove(item.Bubble);if(items.Count==0&&panel.Children.Count==0&&window?.IsVisible==true)window.Hide();};item.Bubble.BeginAnimation(UIElement.OpacityProperty,fade);
            }
            else panel.Children.Remove(item.Bubble);
        }
        if(policy==ThinkingDisplayPolicy.Latest&&entries.Length>0)
            foreach(var child in panel.Children.OfType<Border>().Where(c=>!items.Values.Any(i=>i.Bubble==c)).ToArray())panel.Children.Remove(child);
        for(int i=0;i<entries.Length;i++)
        {
            var message=entries[i];var style=message.Style.Normalize();
            if(!items.TryGetValue(message.Key,out var item))
            {
                var label=new TextBlock{TextAlignment=TextAlignment.Center,VerticalAlignment=VerticalAlignment.Center,TextTrimming=TextTrimming.CharacterEllipsis};
                var bubble=new Border{CornerRadius=new CornerRadius(7),Padding=new Thickness(9,3,9,3),Child=label};
                item=new Item(bubble,label,message);items.Add(message.Key,item);panel.Children.Add(bubble);
                if(style.FlashIn)bubble.BeginAnimation(UIElement.OpacityProperty,new DoubleAnimation(0,1,TimeSpan.FromMilliseconds(160)));
            }
            item.Text.Text=message.Text;item.Text.FontSize=style.Size;item.Text.Foreground=new SolidColorBrush(ThinkingStyleSettings.ColorOf(style.Color,style.Alpha));
            item.Bubble.Background=style.Background?new SolidColorBrush(ThinkingStyleSettings.ColorOf(style.BackgroundColor,style.BackgroundAlpha)):Brushes.Transparent;
            Panel.SetZIndex(item.Bubble,entries.Length-i);items[message.Key]=item with{Message=message};
        }
        if(entries.Length==0){if(panel.Children.Count==0&&window?.IsVisible==true)window.Hide();return;}
        var activeWindow=EnsureWindow();
        var work=DesktopPlatform.Primary.Work;var dpi=activeWindow.IsVisible?DesktopPlatform.DpiScale(activeWindow):1;
        var taskbar=location==ThinkingLocation.PrimaryBottom?null:DesktopPlatform.Taskbar();
        if(taskbar is {} data&&data.Bar.Right-data.Bar.Left>data.Bar.Bottom-data.Bar.Top&&data.Bar.Bottom-data.Bar.Top>=20&&(location!=ThinkingLocation.TrayLeft||data.Tray.HasValue))
        {
            var bar=data.Bar;var right=location==ThinkingLocation.TrayLeft?data.Tray!.Value.Left-8:Math.Min(data.Tray?.Left??bar.Right,bar.Left+Math.Min(440,(bar.Right-bar.Left)*.35));
            var left=bar.Left+8;var available=right-left;
            if(available>=120)
            {
                var w=Math.Min(available,740*dpi);var h=bar.Bottom-bar.Top-4;
                foreach(var item in items.Values)
                {
                    var offset=Math.Clamp(item.Message.Style.Offset,0,Math.Max(0,w/dpi-80));
                    item.Bubble.HorizontalAlignment=location==ThinkingLocation.TrayLeft?HorizontalAlignment.Right:HorizontalAlignment.Left;
                    item.Bubble.VerticalAlignment=VerticalAlignment.Center;item.Bubble.Margin=location==ThinkingLocation.TrayLeft?new Thickness(0,0,offset,0):new Thickness(offset,0,0,0);
                    item.Text.MaxWidth=Math.Max(1,w/dpi-offset-18);item.Text.TextWrapping=TextWrapping.NoWrap;
                }
                Place(location==ThinkingLocation.TrayLeft?right-w:left,bar.Top+2,w,h);return;
            }
        }
        var width=Math.Max(1,Math.Min(work.Right-work.Left-16,740*dpi));
        var maxHeight=Math.Max(1,(work.Bottom-work.Top)*.6);
        foreach(var item in items.Values)
        {
            item.Bubble.HorizontalAlignment=HorizontalAlignment.Center;item.Bubble.VerticalAlignment=VerticalAlignment.Bottom;
            item.Bubble.Margin=new Thickness(0,0,0,Math.Clamp(item.Message.Style.Offset,0,Math.Max(0,maxHeight/dpi-60)));
            item.Text.MaxWidth=Math.Max(1,width/dpi-18);item.Text.TextWrapping=TextWrapping.Wrap;
        }
        panel.Measure(new Size(width/dpi,double.PositiveInfinity));
        var height=Math.Max(1,Math.Min(maxHeight,Math.Max(44,panel.DesiredSize.Height*dpi)));
        Place(work.Left+(work.Right-work.Left-width)/2,work.Bottom-height-6,width,height);
    }
    private void Place(double x,double y,double width,double height)
    {
        if(width<=0||height<=0)return;var active=EnsureWindow();if(!active.IsVisible)active.Show();
        var scale=DesktopPlatform.DpiScale(active);active.Width=width/scale;active.Height=height/scale;DesktopPlatform.Place(active,x,y,width,height);
    }
    public void Dispose(){window?.Close();window=null;}
}
