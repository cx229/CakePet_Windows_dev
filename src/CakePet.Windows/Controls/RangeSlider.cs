using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Globalization;
namespace CakePet.Windows;
// Both handles share one track. The ends never cross; the value pair is committed together.
internal sealed class RangeSlider:Grid
{
    private readonly Canvas track=new(){Height=28,MinWidth=120,Background=Brushes.Transparent};
    private readonly Border rail=new(){Height=5,CornerRadius=new CornerRadius(3),Background=Ui.Brush("Line")};
    private readonly Border fill=new(){Height=5,CornerRadius=new CornerRadius(3),Background=Ui.Brush("Accent")};
    private readonly Thumb lower=Handle(),upper=Handle();
    private readonly TextBox lowBox=new(),highBox=new();
    private readonly double min,max;private double lo,hi;private readonly Action<double,double> changed;
    private static Thumb Handle()=>new(){Width=18,Height=18,Cursor=System.Windows.Input.Cursors.SizeWE,Template=(ControlTemplate)Application.Current.FindResource("RangeThumb")};
    public RangeSlider(double min,double max,double low,double high,Action<double,double> changed,string unit="秒")
    {
        this.min=min;this.max=max;lo=low;hi=high;this.changed=changed;
        ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
        track.Children.Add(rail);track.Children.Add(fill);track.Children.Add(lower);track.Children.Add(upper);Children.Add(track);
        var values=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(10,0,0,0),VerticalAlignment=VerticalAlignment.Center};
        lowBox.Width=58;highBox.Width=58;values.Children.Add(lowBox);values.Children.Add(Ui.Text(" — "));values.Children.Add(highBox);values.Children.Add(Ui.Text(" "+unit,12,true));SetColumn(values,1);Children.Add(values);
        track.SizeChanged+=(_,_)=>Draw();lower.DragDelta+=(_,e)=>Move(true,e.HorizontalChange);upper.DragDelta+=(_,e)=>Move(false,e.HorizontalChange);
        lower.KeyDown+=(_,e)=>Key(true,e);upper.KeyDown+=(_,e)=>Key(false,e);
        lower.Focusable=upper.Focusable=true;lowBox.LostKeyboardFocus+=(_,_)=>Commit(true);highBox.LostKeyboardFocus+=(_,_)=>Commit(false);
        lowBox.KeyDown+=(_,e)=>{if(e.Key==System.Windows.Input.Key.Enter){Commit(true);e.Handled=true;}};highBox.KeyDown+=(_,e)=>{if(e.Key==System.Windows.Input.Key.Enter){Commit(false);e.Handled=true;}};
        Draw();
    }
    private double WidthOfTrack=>Math.Max(1,track.ActualWidth-18);
    private double X(double v)=>9+(v-min)/(max-min)*WidthOfTrack;
    private void Draw(){var a=X(lo);var b=X(hi);Canvas.SetLeft(rail,9);Canvas.SetTop(rail,12);rail.Width=WidthOfTrack;Canvas.SetLeft(fill,a);Canvas.SetTop(fill,12);fill.Width=Math.Max(0,b-a);Canvas.SetLeft(lower,a-9);Canvas.SetTop(lower,5);Canvas.SetLeft(upper,b-9);Canvas.SetTop(upper,5);lowBox.Text=lo.ToString("0",CultureInfo.InvariantCulture);highBox.Text=hi.ToString("0",CultureInfo.InvariantCulture);}
    private void Set(bool low,double value){var a=lo;var b=hi;value=Math.Round(value);if(low)lo=Math.Clamp(value,min,hi);else hi=Math.Clamp(value,lo,max);Draw();if(a!=lo||b!=hi)changed(lo,hi);}
    private void Move(bool low,double dx)=>Set(low,(low?lo:hi)+dx/WidthOfTrack*(max-min));
    private void Commit(bool low){var box=low?lowBox:highBox;if(double.TryParse(box.Text,NumberStyles.Float,CultureInfo.InvariantCulture,out var v)&&double.IsFinite(v))Set(low,v);else Draw();}
    private void Key(bool low,System.Windows.Input.KeyEventArgs e){if(e.Key is System.Windows.Input.Key.Left or System.Windows.Input.Key.Right){Set(low,(low?lo:hi)+(e.Key==System.Windows.Input.Key.Left?-1:1));e.Handled=true;}}
}
