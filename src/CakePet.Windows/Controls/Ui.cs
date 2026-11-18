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
    // Pair is a generic two-column layout. SettingsRow owns the responsive parameter layout.
    public static Grid Pair(FrameworkElement left,FrameworkElement right,double width=0)
    {
        var g=new Grid{MinHeight=36};g.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});g.ColumnDefinitions.Add(new ColumnDefinition{Width=width>0?new GridLength(width):GridLength.Auto});
        left.Margin=new Thickness(0,0,12,0);left.VerticalAlignment=VerticalAlignment.Center;g.Children.Add(left);right.VerticalAlignment=VerticalAlignment.Center;Grid.SetColumn(right,1);g.Children.Add(right);
        if(right is RangeSlider)Adapt(g,left,right,330,540);
        return g;
    }
    private static void Adapt(Grid row,FrameworkElement left,FrameworkElement right,double preferred,double breakpoint)
    {
        row.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});row.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
        void Layout(){if(row.ActualWidth<=0)return;bool narrow=row.ActualWidth<breakpoint;row.ColumnDefinitions[1].Width=narrow?new GridLength(0):new GridLength(Math.Min(preferred,Math.Max(180,row.ActualWidth*.56)));Grid.SetColumn(right,narrow?0:1);Grid.SetRow(right,narrow?1:0);right.Margin=narrow?new Thickness(0,5,0,3):new Thickness(0);left.Margin=narrow?new Thickness(0):new Thickness(0,12,0,0);}
        row.SizeChanged+=(_,_)=>Layout();row.Loaded+=(_,_)=>Layout();
    }
    public static Grid SettingsRow(string title,string note,FrameworkElement control,double preferred=330,double breakpoint=540)
    {
        var label=Caption(title,note);var row=Pair(label,control,preferred);if(control is not RangeSlider)Adapt(row,label,control,preferred,breakpoint);return row;
    }
    public static TextBox NumericBox(double value,double width=68)=>new(){Text=value.ToString("0.###",CultureInfo.InvariantCulture),Width=width,Padding=new Thickness(5,4,5,4),MinHeight=26,HorizontalContentAlignment=HorizontalAlignment.Right,VerticalContentAlignment=VerticalAlignment.Center};
    public static CheckBox Switch(bool value,bool enabled=true)=>new(){IsChecked=value,IsEnabled=enabled,Width=44,HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Center};
    public static FrameworkElement NumberRow(string title,string note,double value,double min,double max,double step,string unit,Action<double> changed,Func<double>? liveValue=null,Action<Action>? registerRefresh=null)
    {
        var right=new Grid();right.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});right.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(68)});right.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
        var box=NumericBox(value,68);box.HorizontalAlignment=HorizontalAlignment.Stretch;
        var slider=new Slider{Minimum=min,Maximum=max,TickFrequency=step,SmallChange=step,LargeChange=step*5,IsSnapToTickEnabled=true,IsMoveToPointEnabled=true,Margin=new Thickness(0,0,12,0),Value=Math.Clamp(value,min,max)};
        right.Children.Add(slider);Grid.SetColumn(box,1);right.Children.Add(box);if(unit.Length>0){var u=Text(unit,11,true);u.Margin=new Thickness(6,0,0,0);u.VerticalAlignment=VerticalAlignment.Center;Grid.SetColumn(u,2);right.Children.Add(u);}
        bool syncing=false;double last=slider.Value;
        void Publish(){if(last==slider.Value)return;last=slider.Value;changed(last);}
        slider.ValueChanged+=(_,_)=>{if(syncing)return;syncing=true;box.Text=slider.Value.ToString("0.###",CultureInfo.InvariantCulture);syncing=false;Publish();};
        void Commit(){if(syncing)return;syncing=true;if(double.TryParse(box.Text,NumberStyles.Float,CultureInfo.InvariantCulture,out var v)&&double.IsFinite(v))slider.Value=Math.Clamp(v,min,max);box.Text=slider.Value.ToString("0.###",CultureInfo.InvariantCulture);syncing=false;Publish();}
        box.LostKeyboardFocus+=(_,_)=>Commit();box.KeyDown+=(_,e)=>{if(e.Key==System.Windows.Input.Key.Enter){Commit();e.Handled=true;}};
        var row=SettingsRow(title,note,right,360,470);
        if(liveValue!=null&&registerRefresh!=null)registerRefresh(()=>{
            if(box.IsKeyboardFocusWithin||slider.IsMouseCaptureWithin||slider.IsKeyboardFocusWithin)return;
            double actual=liveValue();if(!double.IsFinite(actual))return;
            syncing=true;slider.Value=Math.Clamp(actual,min,max);last=slider.Value;box.Text=actual.ToString("0.###",CultureInfo.InvariantCulture);syncing=false;
        });
        return row;
    }
}
internal sealed class SettingCard:Border
{
    private readonly StackPanel body=new();private readonly StackPanel content=new();private readonly TextBlock remark;private readonly TextBlock heading;private readonly Button? arrow;private readonly CheckBox? master;private bool expanded;private bool requestedExpanded;private bool permitted=true;private bool forcedOn;private readonly bool collapsible;private string? warning;
    public SettingCard(string title,string note,bool? enabled=null,Action<bool>? toggle=null,bool available=true,bool collapsible=true,bool initiallyExpanded=true)
    {
        this.collapsible=collapsible;Background=Ui.Brush("Surface");BorderBrush=Ui.Brush("Line");BorderThickness=new Thickness(1);CornerRadius=new CornerRadius(7);Padding=new Thickness(14,8,14,8);Margin=new Thickness(0,0,0,8);
        var label=Ui.Caption(title,note,true);heading=(TextBlock)label.Children[0];if(label.Children.Count==1)label.Children.Add(Ui.Text("",12,true));remark=(TextBlock)label.Children[1];
        var controls=new StackPanel{Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center};
        if(enabled.HasValue){master=Ui.Switch(enabled.Value,available);master.Click+=(_,_)=>{toggle?.Invoke(master.IsChecked==true);requestedExpanded=master.IsChecked==true;UpdateFold();};controls.Children.Add(master);}
        if(collapsible){arrow=Ui.Light("⌄",()=>SetExpanded(!expanded));arrow.Width=30;arrow.Padding=new Thickness(4);arrow.Margin=new Thickness(0,0,6,0);arrow.Background=System.Windows.Media.Brushes.Transparent;}
        var left=new DockPanel{LastChildFill=true};if(arrow!=null){DockPanel.SetDock(arrow,Dock.Left);left.Children.Add(arrow);}left.Children.Add(label);var header=Ui.Pair(left,controls);label.Cursor=System.Windows.Input.Cursors.Hand;label.MouseLeftButtonUp+=(_,e)=>{if(warning!=null&&(!permitted||forcedOn)){MessageBox.Show(warning,"功能提示");}else if(collapsible&&CanExpand)SetExpanded(!expanded);e.Handled=true;};content.Children.Add(header);content.Children.Add(body);Child=content;requestedExpanded=initiallyExpanded;SetAvailable(available);
    }
    public void Add(FrameworkElement row){var inset=new Border{Margin=new Thickness(16,0,0,0),Child=row};body.Children.Add(new Border{Height=1,Background=Ui.Brush("Line"),Margin=new Thickness(16,4,0,4)});body.Children.Add(inset);}
    public void AddChild(SettingCard card){card.Margin=new Thickness(16,0,0,0);card.Padding=new Thickness(0,2,0,2);card.Background=Brushes.Transparent;card.BorderThickness=new Thickness(0);body.Children.Add(new Border{Height=1,Background=Ui.Brush("Line"),Margin=new Thickness(16,5,0,5)});body.Children.Add(card);}
    public void AddGroup(FrameworkElement label){label.Margin=new Thickness(16,8,0,4);body.Children.Add(label);}
    public bool CanExpand=>permitted&&(master==null||forcedOn||master.IsChecked==true);
    public void SetExpanded(bool value){if(value&&!CanExpand)return;requestedExpanded=value;UpdateFold();}
    private void UpdateFold(){expanded=CanExpand&&requestedExpanded;body.Visibility=expanded?Visibility.Visible:Visibility.Collapsed;body.IsEnabled=CanExpand;if(arrow!=null){arrow.Content=expanded?"⌄":"›";arrow.IsEnabled=CanExpand;}}
    public void ForceOn(string message){forcedOn=true;SetWarning(message);if(master!=null){master.IsChecked=true;master.IsEnabled=false;}UpdateFold();}
    public void SetWarning(string message){warning=message;heading.Foreground=Brushes.IndianRed;remark.Foreground=Brushes.IndianRed;remark.Text=message;}
    public void SetAvailable(bool value){permitted=value;if(master!=null)master.IsEnabled=value&&!forcedOn;Opacity=1;UpdateFold();}
}
