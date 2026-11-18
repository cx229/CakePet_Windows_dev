using CakePet.Core;
using P=CakePet.Core.Point;
using Frame=CakePet.Core.Frame;
namespace CakePet.Windows;
internal sealed class FoodWindow
{
    private readonly NativeLayer layer=new();
    private readonly FoodWorld world;
    private readonly FoodInstance food;
    private readonly WpfImageCache.FoodVisual visual;
    private P grabOffset,pressedAt;
    private bool held,moved,closingFromWorld;
    private Frame? rendered;
    private int lastW,lastH;
    public FoodWindow(FoodWorld world,FoodInstance food,WpfImageCache.FoodVisual visual)
    {
        this.world=world;this.food=food;this.visual=visual;
        layer.Down+=OnDown;layer.Move+=OnMove;layer.Up+=OnUp;
        layer.Menu+=()=>NativeMenu.Show(layer.Handle,new NativeMenu.Item[]{new("清除这条食物",()=>world.Remove(food)),new("清除本角色食物",()=>world.Clear(food.Owner))});
        layer.Lost+=()=>{if(held&&!closingFromWorld){held=false;world.Release(food,food.Center,true);}};
        layer.Closed+=()=>{if(!closingFromWorld){closingFromWorld=true;world.Remove(food);}};
        Render();
    }
    public void Show(){Render();layer.Show();}
    private void OnDown()
    {
        if(!world.Hold(food))return;
        held=true;moved=false;pressedAt=DesktopPlatform.Cursor();grabOffset=food.Center-pressedAt;
        if(!layer.Capture()){held=false;world.Release(food,pressedAt,true);}
    }
    private void OnMove()
    {
        if(!held)return;var pointer=DesktopPlatform.Cursor();
        if(!moved&&(pointer-pressedAt).Length<5)return;
        moved=true;var p=pointer+grabOffset;var b=DesktopPlatform.At(pointer).Work;
        p=new P(Math.Clamp(p.X,b.Left+food.Width/2,Math.Max(b.Left+food.Width/2,b.Right-food.Width/2)),
            Math.Clamp(p.Y,b.Top+food.Height/2,Math.Max(b.Top+food.Height/2,b.Bottom-food.Height/2)));
        world.Move(food,p,pointer);Render();
    }
    private void OnUp(){if(!held)return;held=false;layer.Release();world.Release(food,DesktopPlatform.Cursor(),allowConsume:moved);}
    internal void Render()
    {
        if(closingFromWorld)return;
        int w=Math.Max(1,(int)Math.Round(food.Width)),h=Math.Max(1,(int)Math.Round(food.Height));
        var x=food.Center.X-food.Width/2;var y=food.Center.Y-food.Height/2;var frame=food.Animation.Current;
        if((frame!=rendered||lastW!=w||lastH!=h)&&visual.Frames.TryGetValue(frame,out var bitmap))
        {layer.Paint(NativePetPixels.Image(bitmap,w,h),w,h,x,y,food.Opacity);rendered=frame;lastW=w;lastH=h;}
        else layer.Place(x,y,food.Opacity);
    }
    internal void CloseFromWorld(){if(closingFromWorld)return;closingFromWorld=true;held=false;layer.Release();layer.Dispose();}
}
