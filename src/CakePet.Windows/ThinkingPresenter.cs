using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CakePet.Core;
namespace CakePet.Windows;

// A single presentation surface. Role timelines remain independent.
internal sealed class ThinkingPresenter:IDisposable
{
    private readonly TextBlock text=new(){FontSize=14,Foreground=Brushes.White,TextWrapping=TextWrapping.Wrap,TextAlignment=TextAlignment.Center,VerticalAlignment=VerticalAlignment.Center,TextTrimming=TextTrimming.CharacterEllipsis};
    private readonly Border panel;
    private readonly Window window;
    private string previous="";
    private ThinkingLocation previousLocation=(ThinkingLocation)(-1);
    private long lastLayout;
    public ThinkingPresenter()
    {
        panel=new Border{Background=new SolidColorBrush(Color.FromArgb(190,35,35,35)),CornerRadius=new CornerRadius(8),Padding=new Thickness(10,4,10,4),Child=text};
        window=new Window{Width=400,Height=44,WindowStyle=WindowStyle.None,ResizeMode=ResizeMode.NoResize,AllowsTransparency=true,Background=Brushes.Transparent,ShowInTaskbar=false,ShowActivated=false,Topmost=true,IsHitTestVisible=false,Content=panel};
        window.SourceInitialized+=(_,_)=>NativeStyles.NonActivating(window,true);
    }
    public void Render(IEnumerable<(string Name,string Text)> thoughts,ThinkingLocation location,bool single)
    {
        var entries=thoughts.ToArray();if(entries.Length==0){if(window.IsVisible)window.Hide();previous="";return;}
        var value=single?entries[0].Text:string.Join(location==ThinkingLocation.PrimaryBottom?"\n":"  ·  ",entries.Select(e=>e.Name+"："+e.Text));
        var now=Environment.TickCount64;if(value==previous&&location==previousLocation&&now-lastLayout<500&&window.IsVisible)return;
        previous=value;previousLocation=location;lastLayout=now;text.Text=value;
        var primary=DesktopPlatform.Primary;var work=primary.Work;
        var dpi=window.IsVisible?DesktopPlatform.DpiScale(window):1;
        var taskbar=location==ThinkingLocation.PrimaryBottom?null:DesktopPlatform.Taskbar();
        if(taskbar is { } data&&data.Bar.Right-data.Bar.Left>data.Bar.Bottom-data.Bar.Top&&data.Bar.Bottom-data.Bar.Top>=20&&
           (location!=ThinkingLocation.TrayLeft||data.Tray.HasValue))
        {
            var bar=data.Bar;var tray=data.Tray;
            // Leave the notification icons untouched. The overlay is click-through.
            var right=location==ThinkingLocation.TrayLeft?tray!.Value.Left-8:Math.Min(tray?.Left??bar.Right,bar.Left+Math.Min(440,(bar.Right-bar.Left)*.35));
            var leftLimit=bar.Left+8;var available=right-leftLimit;
            if(available>=120)
            {
                text.TextWrapping=TextWrapping.NoWrap;text.TextAlignment=location==ThinkingLocation.TrayLeft?TextAlignment.Right:TextAlignment.Left;
                panel.Background=new SolidColorBrush(Color.FromArgb(190,35,35,35));panel.CornerRadius=new CornerRadius(6);
                text.Measure(new Size(double.PositiveInfinity,double.PositiveInfinity));
                var width=Math.Min(Math.Min(available,650*dpi),(text.DesiredSize.Width+24)*dpi);var height=Math.Min(bar.Bottom-bar.Top-4,34*dpi);
                Place(location==ThinkingLocation.TrayLeft?right-width:leftLimit,bar.Top+(bar.Bottom-bar.Top-height)/2,width,height);return;
            }
        }
        // Explorer restart, hidden/vertical taskbar or missing tray: safe primary work-area fallback.
        text.TextWrapping=TextWrapping.Wrap;text.TextAlignment=TextAlignment.Center;panel.CornerRadius=new CornerRadius(8);
        var w=Math.Min(work.Right-work.Left-16,Math.Max(360,Math.Min(740,(work.Right-work.Left)*.6)));
        var h=Math.Min(Math.Max(44,entries.Length*26+14)*dpi,Math.Max(44,(work.Bottom-work.Top)*.3));
        Place(work.Left+(work.Right-work.Left-w)/2,work.Bottom-h-6,w,h);
    }
    private void Place(double x,double y,double width,double height)
    {
        if(width<=0||height<=0)return;
        if(!window.IsVisible)window.Show();
        var scale=DesktopPlatform.DpiScale(window);window.Width=width/scale;window.Height=height/scale;
        DesktopPlatform.Place(window,x,y,width,height);
    }
    public void Dispose()=>window.Close();
}
