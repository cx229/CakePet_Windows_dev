using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Globalization;
namespace CakePet.Windows;
internal static class Ui
{
    public static Brush Brush(string key)=>(Brush)Application.Current.FindResource(key);
    public static TextBlock Text(string text,double size=14,bool muted=false)=>new(){Text=text,FontSize=size,Foreground=Brush(muted?"Muted":"Ink"),TextWrapping=TextWrapping.Wrap};
    public static StackPanel Caption(string title,string note="",bool main=false)
    {
        var p=new StackPanel{VerticalAlignment=VerticalAlignment.Center};var t=Text(title,main?16:14);if(main)t.FontWeight=FontWeights.SemiBold;p.Children.Add(t);
        if(note.Length>0){var n=Text(note,12,true);n.Margin=new Thickness(0,4,0,0);p.Children.Add(n);}return p;
    }
    public static Border Card(UIElement child)=>new(){Background=Brush("Surface"),BorderBrush=Brush("Line"),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(7),Padding=new Thickness(14,8,14,8),Margin=new Thickness(0,0,0,8),Child=child};
    public static Button Button(string text,Action action,bool primary=false)
    {var b=new Button{Content=text,Margin=new Thickness(0,0,8,0),HorizontalAlignment=HorizontalAlignment.Left,HorizontalContentAlignment=HorizontalAlignment.Center};if(primary)b.Style=(Style)Application.Current.FindResource("PrimaryButton");b.Click+=(_,_)=>action();return b;}
    public static Button Nav(string text,Action action){var b=Button(text,action);b.Style=(Style)Application.Current.FindResource("NavButton");b.HorizontalAlignment=HorizontalAlignment.Stretch;b.HorizontalContentAlignment=HorizontalAlignment.Left;b.Margin=new Thickness(0,2,0,2);return b;}
    public static Button Light(string text,Action action){var b=Button(text,action);b.Style=(Style)Application.Current.FindResource("LightButton");return b;}
    public static Grid Pair(FrameworkElement left,FrameworkElement right,double width=0)
    {
        var g=new Grid{MinHeight=48};g.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});g.ColumnDefinitions.Add(new ColumnDefinition{Width=width>0?new GridLength(width):GridLength.Auto});
        left.Margin=new Thickness(0,0,16,0);left.VerticalAlignment=VerticalAlignment.Center;g.Children.Add(left);right.VerticalAlignment=VerticalAlignment.Center;Grid.SetColumn(right,1);g.Children.Add(right);return g;
    }
    public static CheckBox Switch(bool value,bool enabled=true)=>new(){IsChecked=value,IsEnabled=enabled,Width=44,HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Center};
    public static FrameworkElement NumberRow(string title,string note,double value,double min,double max,double step,string unit,Action<double> changed)
    {
        var right=new Grid();right.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});right.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(76)});right.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
        var box=new TextBox{Text=value.ToString("0.###",CultureInfo.InvariantCulture),HorizontalContentAlignment=HorizontalAlignment.Right};
        var slider=new Slider{Minimum=min,Maximum=max,TickFrequency=step,SmallChange=step,LargeChange=step*5,IsSnapToTickEnabled=true,IsMoveToPointEnabled=true,Margin=new Thickness(0,0,12,0),Value=Math.Clamp(value,min,max)};
        right.Children.Add(slider);Grid.SetColumn(box,1);right.Children.Add(box);if(unit.Length>0){var u=Text(unit,11,true);u.Margin=new Thickness(6,0,0,0);u.VerticalAlignment=VerticalAlignment.Center;Grid.SetColumn(u,2);right.Children.Add(u);}
        bool syncing=false;double last=slider.Value;
        void Publish(){if(last==slider.Value)return;last=slider.Value;changed(last);}
        slider.ValueChanged+=(_,_)=>{if(syncing)return;syncing=true;box.Text=slider.Value.ToString("0.###",CultureInfo.InvariantCulture);syncing=false;Publish();};
        void Commit(){if(syncing)return;syncing=true;if(double.TryParse(box.Text,NumberStyles.Float,CultureInfo.InvariantCulture,out var v)&&double.IsFinite(v))slider.Value=Math.Clamp(v,min,max);box.Text=slider.Value.ToString("0.###",CultureInfo.InvariantCulture);syncing=false;Publish();}
        box.LostKeyboardFocus+=(_,_)=>Commit();box.KeyDown+=(_,e)=>{if(e.Key==System.Windows.Input.Key.Enter){Commit();e.Handled=true;}};
        var row=Pair(Caption(title,note),right,310);
        row.SizeChanged+=(_,_)=>{bool narrow=row.ActualWidth<400;row.ColumnDefinitions[1].Width=narrow?new GridLength(0):new GridLength(Math.Clamp(row.ActualWidth*.52,200,430));if(narrow){if(row.RowDefinitions.Count==0){row.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});row.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});}Grid.SetColumn(right,0);Grid.SetRow(right,1);right.Margin=new Thickness(0,5,0,4);}else{Grid.SetColumn(right,1);Grid.SetRow(right,0);right.Margin=new Thickness(0);}};
        return row;
    }
}
internal sealed class SettingCard:Border
{
    private readonly StackPanel body=new();private readonly StackPanel content=new();private readonly TextBlock remark;private readonly TextBlock heading;private readonly Button? arrow;private readonly CheckBox? master;private bool expanded;private bool requestedExpanded;private bool permitted=true;
    public SettingCard(string title,string note,bool? enabled=null,Action<bool>? toggle=null,bool available=true,bool collapsible=true,bool initiallyExpanded=true)
    {
        Background=Ui.Brush("Surface");BorderBrush=Ui.Brush("Line");BorderThickness=new Thickness(1);CornerRadius=new CornerRadius(7);Padding=new Thickness(14,8,14,8);Margin=new Thickness(0,0,0,8);
        var label=Ui.Caption(title,note,true);heading=(TextBlock)label.Children[0];if(label.Children.Count==1)label.Children.Add(Ui.Text("",12,true));remark=(TextBlock)label.Children[1];
        var controls=new StackPanel{Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center};
        if(enabled.HasValue){master=Ui.Switch(enabled.Value,available);master.Click+=(_,_)=>{toggle?.Invoke(master.IsChecked==true);SetExpanded(master.IsChecked==true);};controls.Children.Add(master);}
        if(collapsible){arrow=Ui.Light("⌄",()=>SetExpanded(!expanded));arrow.Width=30;arrow.Padding=new Thickness(4);arrow.Margin=new Thickness(8,0,0,0);arrow.Background=System.Windows.Media.Brushes.Transparent;controls.Children.Add(arrow);}
        var header=Ui.Pair(label,controls);if(collapsible){label.Cursor=System.Windows.Input.Cursors.Hand;label.MouseLeftButtonUp+=(_,e)=>{SetExpanded(!expanded);e.Handled=true;};}content.Children.Add(header);content.Children.Add(body);Child=content;SetExpanded(initiallyExpanded&&enabled!=false);SetAvailable(available);
    }
    public void Add(FrameworkElement row){body.Children.Add(new Border{Height=1,Background=Ui.Brush("Line"),Margin=new Thickness(0,4,0,4)});body.Children.Add(row);}
    public void SetExpanded(bool value){requestedExpanded=value;UpdateFold();}
    private void UpdateFold(){expanded=permitted&&requestedExpanded;body.Visibility=expanded?Visibility.Visible:Visibility.Collapsed;if(arrow!=null){arrow.Content=expanded?"⌃":"⌄";arrow.IsEnabled=permitted;}}
    public void ForceOn(string message){SetWarning(message);if(master!=null){master.IsChecked=true;master.IsEnabled=false;}}
    public void SetWarning(string message){heading.Foreground=Brushes.IndianRed;remark.Foreground=Brushes.IndianRed;remark.Text=message;}
    public void SetAvailable(bool value){permitted=value;IsEnabled=value;Opacity=value?1:.8;UpdateFold();}
}
