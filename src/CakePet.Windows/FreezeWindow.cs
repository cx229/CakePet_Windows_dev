using CakePet.Core;
using P=CakePet.Core.Point;
namespace CakePet.Windows;
internal sealed class FreezeWindow
{
    private readonly NativeLayer layer=new();
    private P position,offset;
    private readonly double side;
    private bool dragging,draggable=true;
    private long revision=-1;
    private P? lastPosition;
    public event EventHandler? Closed;
    public FreezeWindow(byte[] bitmap,P origin,double size)
    {
        position=origin;side=size;int pixels=Math.Max(1,(int)Math.Round(side));
        layer.Paint(bitmap,pixels,pixels,origin.X,origin.Y,1);
        layer.Menu+=()=>NativeMenu.Show(layer.Handle,new NativeMenu.Item[]{new("允许拖动",()=>draggable=!draggable,true,draggable),new("移除定格",Close)});
        layer.Down+=()=>{if(!draggable)return;offset=position-DesktopPlatform.Cursor();dragging=layer.Capture();};
        layer.Move+=()=>{if(!dragging)return;position=DesktopPlatform.Cursor()+offset;Refresh();};
        layer.Up+=()=>{dragging=false;revision=-1;layer.Release();Refresh();};
        layer.Lost+=()=>{dragging=false;revision=-1;};
        layer.Closed+=()=>Closed?.Invoke(this,EventArgs.Empty);
    }
    public void Show(){Refresh();layer.Show();}
    public void Close()=>layer.Dispose();
    public void Refresh()
    {
        _=DesktopPlatform.Screens;var current=DesktopPlatform.ScreenRevision;
        if(!dragging&&revision==current&&lastPosition==position)return;
        if(!dragging){var monitor=DesktopPlatform.At(position+new P(side/2,side/2));position=DesktopGeometry.Clamp(position,DesktopGeometry.Fit(monitor.Full,side,side));}
        layer.Place(position.X,position.Y,1);revision=current;lastPosition=position;
    }
}
