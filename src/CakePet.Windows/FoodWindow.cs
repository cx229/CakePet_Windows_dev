using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CakePet.Core;
using P=CakePet.Core.Point;
using Frame=CakePet.Core.Frame;

namespace CakePet.Windows;

// No animation timer or pet business logic in the food window.
internal sealed class FoodWindow : Window
{
    private readonly FoodWorld world;
    private readonly FoodInstance food;
    private readonly WpfImageCache.FoodVisual visual;
    private readonly Image image=new(){Stretch=Stretch.Fill};
    private P grabOffset,pressedAt;
    private bool held,moved,closingFromWorld;
    private Frame? rendered;
    public FoodWindow(FoodWorld world,FoodInstance food,WpfImageCache.FoodVisual visual)
    {
        this.world=world;this.food=food;this.visual=visual;
        Title="食物";WindowStyle=WindowStyle.None;ResizeMode=ResizeMode.NoResize;
        AllowsTransparency=true;Background=Brushes.Transparent;Topmost=true;
        ShowActivated=false;ShowInTaskbar=false;Opacity=.7;
        Width=Math.Max(1,food.Width);Height=Math.Max(1,food.Height);
        Content=new Border{Background=Brushes.Transparent,Child=image};
        SourceInitialized+=(_,_)=>NativeStyles.NonActivating(this,false);
        var menu=new ContextMenu();
        var remove=new MenuItem{Header="清除这条食物"};remove.Click+=(_,_)=>world.Remove(food);
        var clear=new MenuItem{Header="清除本角色食物"};clear.Click+=(_,_)=>world.Clear(food.Owner);
        menu.Items.Add(remove);menu.Items.Add(clear);ContextMenu=menu;
        MouseLeftButtonDown+=OnDown;MouseMove+=OnMove;MouseLeftButtonUp+=OnUp;
        LostMouseCapture+=(_,_)=>{if(held&&!closingFromWorld){held=false;world.Release(food,food.Center,true);}};
        Closed+=(_,_)=>{if(!closingFromWorld){closingFromWorld=true;world.Remove(food);}};
        Render();
    }
    private P Screen(MouseEventArgs e)
    {
        var raw=PointToScreen(e.GetPosition(this));
        var p=PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice.Transform(raw)??raw;
        return new P(p.X,p.Y);
    }
    private void OnDown(object sender,MouseButtonEventArgs e)
    {
        if(!world.Hold(food))return;
        held=true;moved=false;pressedAt=Screen(e);grabOffset=food.Center-pressedAt;
        if(!CaptureMouse()){held=false;world.Release(food,pressedAt,true);}e.Handled=true;
    }
    private void OnMove(object sender,MouseEventArgs e)
    {
        if(!held)return;
        var pointer=Screen(e);
        if(!moved&&(pointer-pressedAt).Length<5)return;
        moved=true;var p=pointer+grabOffset;var b=PetWindow.WorkArea();
        p=new P(Math.Clamp(p.X,b.Left+Width/2,Math.Max(b.Left+Width/2,b.Right-Width/2)),
            Math.Clamp(p.Y,b.Top+Height/2,Math.Max(b.Top+Height/2,b.Bottom-Height/2)));
        world.Move(food,p,pointer);Render();e.Handled=true;
    }
    private void OnUp(object sender,MouseButtonEventArgs e)
    {
        if(!held)return;
        var p=Screen(e);held=false;ReleaseMouseCapture();
        world.Release(food,p,allowConsume:moved);e.Handled=true;
    }
    internal void Render()
    {
        if(closingFromWorld)return;
        Left=food.Center.X-Width/2;Top=food.Center.Y-Height/2;
        var frame=food.Animation.Current;
        if(frame!=rendered&&visual.Frames.TryGetValue(frame,out var bitmap)){image.Source=bitmap;rendered=frame;}
    }
    internal void CloseFromWorld()
    {
        if(closingFromWorld)return;
        closingFromWorld=true;held=false;if(IsMouseCaptured)ReleaseMouseCapture();Close();
    }
}
