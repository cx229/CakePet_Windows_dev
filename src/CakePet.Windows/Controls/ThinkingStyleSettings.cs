using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CakePet.Core;
namespace CakePet.Windows;
internal static class ThinkingStyleSettings
{
    public static Color ColorOf(uint v, double alpha=1)=>Color.FromArgb((byte)Math.Round(Math.Clamp(alpha,0,1)*255),(byte)(v>>16),(byte)(v>>8),(byte)v);
    public static void Add(StackPanel page,Func<ThinkingStyle> current,Action<Func<ThinkingStyle,ThinkingStyle>> change,string sample,Func<bool> available,List<Action> dependencies)
    {
        var previewText=new TextBlock{Text=sample,TextWrapping=TextWrapping.Wrap,TextAlignment=TextAlignment.Center};
        var preview=new Border{CornerRadius=new CornerRadius(7),Padding=new Thickness(10,5,10,5),Child=previewText,HorizontalAlignment=HorizontalAlignment.Center};
        var backdrop=new Border{Background=new SolidColorBrush(Color.FromRgb(110,107,117)),Padding=new Thickness(12),CornerRadius=new CornerRadius(8),Child=preview};
        void Refresh(){var s=current().Normalize();previewText.FontSize=s.Size;previewText.Foreground=new SolidColorBrush(ColorOf(s.Color,s.Alpha));preview.Background=s.Background?new SolidColorBrush(ColorOf(s.BackgroundColor,s.BackgroundAlpha)):Brushes.Transparent;}
        void Set(Func<ThinkingStyle,ThinkingStyle> patch){change(patch);Refresh();foreach(var d in dependencies)d();}
        SettingCard Card(string title,string note,bool? on=null,Action<bool>? toggle=null,Func<bool>? extra=null)
        {var c=new SettingCard(title,note,on,toggle);page.Children.Add(c);void Update(){c.Visibility=available()?Visibility.Visible:Visibility.Collapsed;c.SetAvailable(extra?.Invoke()??true);}dependencies.Add(Update);Update();return c;}
        void Number(SettingCard c,string title,string note,double val,double min,double max,double step,string unit,Func<ThinkingStyle,double,ThinkingStyle> patch)=>c.Add(Ui.NumberRow(title,note,val,min,max,step,unit,v=>Set(s=>patch(s,v))));
        void Flag(SettingCard c,string title,string note,bool val,Func<ThinkingStyle,bool,ThinkingStyle> patch){var sw=Ui.Switch(val);sw.Click+=(_,_)=>Set(s=>patch(s,sw.IsChecked==true));c.Add(Ui.Pair(Ui.Caption(title,note),sw));}
        void ColorRow(SettingCard c,string title,string note,uint value,Func<ThinkingStyle,uint,ThinkingStyle> patch)
        {
            var row=new StackPanel{Orientation=Orientation.Horizontal};var dot=new Border{Width=24,Height=24,CornerRadius=new CornerRadius(12),BorderBrush=Ui.Brush("Line"),BorderThickness=new Thickness(1),Background=new SolidColorBrush(ColorOf(value)),Margin=new Thickness(0,0,8,0)};
            row.Children.Add(dot);row.Children.Add(Ui.Button("选择颜色",()=>{using var d=new System.Windows.Forms.ColorDialog{FullOpen=true,Color=System.Drawing.Color.FromArgb(unchecked((int)value))};if(d.ShowDialog()!=System.Windows.Forms.DialogResult.OK)return;value=unchecked((uint)d.Color.ToArgb());dot.Background=new SolidColorBrush(ColorOf(value));Set(s=>patch(s,value));}));
            c.Add(Ui.Pair(Ui.Caption(title,note),row));
        }
        var s=current().Normalize();var display=Card("显示","调整文字样式，预览实时更新。");display.Add(backdrop);
        Number(display,"字号","文字显示大小。",s.Size,8,48,1,"DIP",(x,v)=>x with{Size=v});
        Number(display,"透明度","文字的可见程度。",s.Alpha*100,0,100,1,"%",(x,v)=>x with{Alpha=v/100});
        Number(display,"位置偏移","底部模式向上；任务栏模式向内留白。",s.Offset,-200,300,1,"px",(x,v)=>x with{Offset=v});
        ColorRow(display,"文字颜色","选择文字的颜色。",s.Color,(x,v)=>x with{Color=v});
        var flash=Card("闪回","控制文字出现和消失时的效果。");Flag(flash,"文字闪现","新消息淡入。",s.FlashIn,(x,v)=>x with{FlashIn=v});Flag(flash,"文字闪回","消息结束时淡出。",s.FlashOut,(x,v)=>x with{FlashOut=v});
        var bg=Card("气泡背景","为文字增加独立背景。",s.Background,v=>Set(x=>x with{Background=v}));
        Number(bg,"背景透明度","背景的可见程度。",s.BackgroundAlpha*100,0,100,1,"%",(x,v)=>x with{BackgroundAlpha=v/100});ColorRow(bg,"背景颜色","选择气泡背景颜色。",s.BackgroundColor,(x,v)=>x with{BackgroundColor=v});
        Refresh();
    }
}
