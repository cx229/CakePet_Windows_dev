using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CakePet.Core;
using P=CakePet.Core.Point;

namespace CakePet.Windows;

// A static image snapshot, independent from the original animation cursor.
internal sealed class FreezeWindow : Window
{
    private P position,offset;
    private readonly double side;
    private bool dragging,draggable=true;
    private long revision=-1;
    private P? lastPosition;
    public FreezeWindow(BitmapSource bitmap,P origin,double size)
    {
        position=origin;side=size;Width=Height=size;WindowStyle=WindowStyle.None;ResizeMode=ResizeMode.NoResize;
        AllowsTransparency=true;Background=Brushes.Transparent;Topmost=true;ShowInTaskbar=false;ShowActivated=false;
        Content=new Border{Background=Brushes.Transparent,Child=new Image{Source=bitmap,Stretch=Stretch.Fill}};
        SourceInitialized+=(_,_)=>{NativeStyles.NonActivating(this,false);Refresh();};
        DpiChanged+=(_,_)=>{revision=-1;};
        var menu=new ContextMenu();
        var move=new MenuItem{Header="允许拖动",IsCheckable=true,IsChecked=true};move.Click+=(_,_)=>draggable=move.IsChecked;
        var remove=new MenuItem{Header="移除定格"};remove.Click+=(_,_)=>Close();menu.Items.Add(move);menu.Items.Add(remove);ContextMenu=menu;
        MouseLeftButtonDown+=(_,e)=>{if(!draggable)return;offset=position-DesktopPlatform.Cursor();dragging=CaptureMouse();e.Handled=true;};
        MouseMove+=(_,_)=>{if(!dragging)return;position=DesktopPlatform.Cursor()+offset;Refresh();};
        MouseLeftButtonUp+=(_,_)=>{dragging=false;revision=-1;ReleaseMouseCapture();Refresh();};
        LostMouseCapture+=(_,_)=>{dragging=false;revision=-1;};
    }
    public void Refresh()
    {
        _=DesktopPlatform.Screens;var current=DesktopPlatform.ScreenRevision;
        if(!dragging&&revision==current&&lastPosition==position)return;
        if(!dragging){var monitor=DesktopPlatform.At(position+new P(side/2,side/2));position=DesktopGeometry.Clamp(position,DesktopGeometry.Fit(monitor.Full,side,side));}
        var dip=side/DesktopPlatform.DpiScale(this);
        if(Math.Abs(Width-dip)>.01||Math.Abs(Height-dip)>.01)Width=Height=dip;
        DesktopPlatform.Place(this,position.X,position.Y,side,side);revision=current;lastPosition=position;
    }
}
