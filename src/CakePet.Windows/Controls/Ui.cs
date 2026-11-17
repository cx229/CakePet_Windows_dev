using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Globalization;
namespace CakePet.Windows;
internal static class Ui
{
    public static Brush Brush(string key)=>(Brush)Application.Current.FindResource(key);
    public static TextBlock Text(string text,double size=14,bool muted=false)=>new(){Text=text,FontSize=size,Foreground=Brush(muted?"Muted":"Ink"),TextWrapping=TextWrapping.Wrap};
    public static Border Card(UIElement child)=>new(){Background=Brush("Surface"),BorderBrush=Brush("Line"),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(12),Padding=new Thickness(16),Margin=new Thickness(0,0,0,10),Child=child};
    public static Button Button(string text,Action action,bool primary=false)
    {var b=new Button{Content=text,Margin=new Thickness(0,0,8,0),HorizontalContentAlignment=HorizontalAlignment.Center};if(primary)b.Style=(Style)Application.Current.FindResource("PrimaryButton");b.Click+=(_,_)=>action();return b;}
    public static FrameworkElement NumberRow(string title,TextBox box,double min,double max,double step,Action changed)
    {
        var row=new Grid{Margin=new Thickness(0,4,0,4)};row.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(180)});row.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});row.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(88)});
        var label=Text(title);label.VerticalAlignment=VerticalAlignment.Center;row.Children.Add(label);
        var slider=new Slider{Minimum=min,Maximum=max,TickFrequency=step,SmallChange=step,LargeChange=step*5,IsSnapToTickEnabled=true,IsMoveToPointEnabled=true,Margin=new Thickness(10,0,12,0),Value=Math.Clamp(double.Parse(box.Text,CultureInfo.InvariantCulture),min,max)};
        Grid.SetColumn(slider,1);row.Children.Add(slider);Grid.SetColumn(box,2);row.Children.Add(box);
        bool syncing=false;
        slider.ValueChanged+=(_,_)=>{if(syncing)return;syncing=true;box.Text=slider.Value.ToString("0.###",CultureInfo.InvariantCulture);syncing=false;changed();};
        void Commit(){if(syncing)return;syncing=true;if(double.TryParse(box.Text,NumberStyles.Float,CultureInfo.InvariantCulture,out var v)&&double.IsFinite(v))slider.Value=Math.Clamp(v,min,max);box.Text=slider.Value.ToString("0.###",CultureInfo.InvariantCulture);syncing=false;changed();}
        box.LostKeyboardFocus+=(_,_)=>Commit();box.KeyDown+=(_,e)=>{if(e.Key==System.Windows.Input.Key.Enter){Commit();e.Handled=true;}};
        return Card(row);
    }
}
