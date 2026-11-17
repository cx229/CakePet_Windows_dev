using System.IO;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CakePet.Core;
namespace CakePet.Windows;
internal sealed partial class PackageEditorPage
{
    private readonly HashSet<string> expandedFolders=new();
    private static int ReadInt(JsonNode? n,int fallback)=>n is JsonValue j&&j.TryGetValue<int>(out var v)?v:fallback;
    private static (int Min,int Max) FrameDelay(JsonObject frame)
    {
        var d=frame["delay"];var lo=Math.Max(1,d is JsonArray a&&a.Count==2?ReadInt(a[0],100):d is JsonObject o?ReadInt(o["min"],100):ReadInt(d,100));
        var hi=d is JsonArray b&&b.Count==2?ReadInt(b[1],lo):d is JsonObject p?ReadInt(p["max"],lo):lo;return(lo,Math.Max(lo,hi));
    }
    private static string FrameSummary(JsonObject frame)
    {
        string Pair(string x,string y,string label)=>frame[x]!=null||frame[y]!=null?" · "+label+"("+(frame[x]?.ToString()??"-")+","+(frame[y]?.ToString()??"-")+")":"";
        return Pair("anchorX","anchorY","XY")+Pair("dx","dy","dXY")+(frame["transform"]?.ToString()=="flipX"?" · 左右镜像":"");
    }
    private static CheckBox ResourceCheck(bool value)=>new(){IsChecked=value,Style=(Style)Application.Current.FindResource("ResourceCheck")};
    private static void Highlight(DependencyObject root,string id)
    {
        if(root is Button b&&b.Tag is string tag)b.Background=Ui.Brush(tag==id?"AccentSoft":"Sidebar");
        foreach(var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())Highlight(child,id);
    }
    private Expander ResourceFolder(string key,string title,string count,StackPanel body,FrameworkElement? actions=null,bool search=false,HashSet<string>? state=null)
    {
        var states=state??expandedFolders;var heading=new DockPanel{LastChildFill=true,Margin=new Thickness(0,2,0,2)};
        if(actions!=null){DockPanel.SetDock(actions,Dock.Right);heading.Children.Add(actions);}
        var badge=Ui.Text(count,11,true);badge.Margin=new Thickness(6,0,4,0);DockPanel.SetDock(badge,Dock.Right);heading.Children.Add(badge);
        var label=new StackPanel{Orientation=Orientation.Horizontal};var icon=new TextBlock{FontFamily=new FontFamily("Segoe MDL2 Assets"),Text="\uE8B7",Foreground=Ui.Brush("Muted"),FontSize=16,Margin=new Thickness(0,0,7,0),VerticalAlignment=VerticalAlignment.Center};label.Children.Add(icon);label.Children.Add(Ui.Text(title,13));heading.Children.Add(label);
        body.Margin=new Thickness(18,4,0,2);var exp=new Expander{Header=heading,Content=body,IsExpanded=search||states.Contains(key),Foreground=Ui.Brush("Ink"),Background=Ui.Brush("Surface"),BorderBrush=Ui.Brush("Line"),BorderThickness=new Thickness(0,0,0,1),Margin=new Thickness(0,2,0,2)};
        exp.Expanded+=(_,e)=>{if(e.Source!=exp)return;icon.Text="\uE838";if(!search)states.Add(key);};exp.Collapsed+=(_,e)=>{if(e.Source!=exp)return;icon.Text="\uE8B7";if(!search)states.Remove(key);};if(exp.IsExpanded)icon.Text="\uE838";return exp;
    }
    private Window EditorDialog(string title,double width,double height)=>new(){Title=title,Width=width,Height=height,MinWidth=380,MinHeight=260,Background=Ui.Brush("Canvas"),Foreground=Ui.Brush("Ink"),Owner=Window.GetWindow(this),WindowStartupLocation=WindowStartupLocation.CenterOwner};
    private static void DialogBody(Window dialog,FrameworkElement body,Action confirm)
    {
        var dock=new DockPanel{Margin=new Thickness(16)};var footer=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,10,0,0)};
        footer.Children.Add(Ui.Button("取消",()=>dialog.DialogResult=false));footer.Children.Add(Ui.Button("确认",confirm,true));DockPanel.SetDock(footer,Dock.Bottom);dock.Children.Add(footer);dock.Children.Add(new ScrollViewer{Content=body,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});dialog.Content=dock;
    }
    private List<string>? PickResources(string type,IEnumerable<string> initial,bool multi)
    {
        var dialog=EditorDialog("选择"+type,660,620);var picked=initial.Where(x=>!string.IsNullOrWhiteSpace(x)).Distinct().ToList();var state=new HashSet<string>();var page=new StackPanel();var search=new TextBox{ToolTip="搜索"+type,Margin=new Thickness(0,0,0,8)};page.Children.Add(search);
        var count=Ui.Text("",12,true);page.Children.Add(count);var list=new StackPanel();page.Children.Add(list);
        var checks=new Dictionary<string,List<CheckBox>>();var counts=new List<(TextBlock Label,string[] Ids)>();bool updating=false;
        void UpdateCounts(){count.Text="已选择 "+picked.Count+" 个";foreach(var (label,ids) in counts)label.Text=ids.Count(picked.Contains)+"/"+ids.Length;}
        void Refresh()
        {
            list.Children.Clear();checks.Clear();counts.Clear();UpdateCounts();
            IEnumerable<(string Name,string[] Ids)> groups=type=="图片"?editor.Images().GroupBy(x=>x.Contains('/')?x[..x.LastIndexOf('/') ]:"未分组").Select(g=>(g.Key,g.ToArray())):editor.Grouped(type=="碎碎念");
            foreach(var group in groups){var ids=group.Ids.Where(id=>id.Contains(search.Text,StringComparison.OrdinalIgnoreCase)||(type=="碎碎念"&&(editor.Thinkings[id]?["text"]?.ToString()??"").Contains(search.Text,StringComparison.OrdinalIgnoreCase))).ToArray();if(search.Text.Length>0&&ids.Length==0)continue;var body=new StackPanel();foreach(var id in ids){var label=type=="碎碎念"?editor.Thinkings[id]?["text"]?.ToString()??id:id;var left=new StackPanel{Orientation=Orientation.Horizontal};if(type!="碎碎念"){var res=type=="图片"?id:editor.Frames(id).FirstOrDefault()?["res"]?.ToString();if(res!=null)left.Children.Add(Thumbnail(editor,res,40));}left.Children.Add(Ui.Caption(label));var check=ResourceCheck(picked.Contains(id));if(!checks.TryGetValue(id,out var copies))checks[id]=copies=new();copies.Add(check);
                check.Click+=(_,_)=>{if(updating)return;if(check.IsChecked==true){if(!multi)picked.Clear();if(!picked.Contains(id))picked.Add(id);}else picked.Remove(id);updating=true;foreach(var kv in checks)foreach(var c in kv.Value)c.IsChecked=picked.Contains(kv.Key);updating=false;UpdateCounts();};body.Children.Add(Ui.Card(Ui.Pair(left,check)));}
                var counter=Ui.Text(group.Ids.Count(picked.Contains)+"/"+group.Ids.Length,11,true);counts.Add((counter,group.Ids));list.Children.Add(ResourceFolder(group.Name,group.Name,"",body,counter,search:search.Text.Length>0,state:state));}
        }
        search.TextChanged+=(_,_)=>Refresh();page.Children.Add(Ui.Light("清空选择",()=>{picked.Clear();Refresh();}));Refresh();DialogBody(dialog,page,()=>dialog.DialogResult=true);return dialog.ShowDialog()==true?picked:null;
    }
    private (int Min,int Max)? EditInterval(string title,int lo,int hi,int minimum)
    {
        var dialog=EditorDialog(title,500,260);var p=new StackPanel();p.Children.Add(new RangeSlider(minimum,Math.Max(60000,hi),lo,hi,(a,b)=>{lo=(int)a;hi=(int)b;},"ms"));DialogBody(dialog,p,()=>dialog.DialogResult=true);return dialog.ShowDialog()==true?(lo,hi):null;
    }
    private void EditFrame(JsonArray frames,int index)
    {
        var dialog=EditorDialog("逐帧编辑",740,540);var root=new DockPanel{Margin=new Thickness(16)};dialog.Content=root;var footer=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};DockPanel.SetDock(footer,Dock.Bottom);root.Children.Add(footer);var host=new ContentControl();root.Children.Add(host);
        void Show()
        {
            dialog.Title="编辑帧 "+(index+1)+" / "+frames.Count;footer.Children.Clear();var prev=Ui.Button("‹ 上一帧",()=>{index--;Show();});prev.IsEnabled=index>0;footer.Children.Add(prev);var next=Ui.Button("下一帧 ›",()=>{index++;Show();});next.IsEnabled=index+1<frames.Count;footer.Children.Add(next);footer.Children.Add(Ui.Button("完成",()=>dialog.Close(),true));
            var frame=frames[index]!.AsObject();var grid=new Grid();grid.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(190)});grid.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});var image=Thumbnail(editor,frame["res"]?.ToString()??"",160);var left=new StackPanel{Margin=new Thickness(0,0,16,0)};left.Children.Add(Ui.Card(image));left.Children.Add(Ui.Text("图片预览",12,true));grid.Children.Add(left);var p=new StackPanel();var scroll=new ScrollViewer{Content=p,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};Grid.SetColumn(scroll,1);grid.Children.Add(scroll);host.Content=grid;
            p.Children.Add(Ui.Card(Ui.Pair(Ui.Caption("图片资源",frame["res"]?.ToString()??""),Ui.Light("选择 ›",()=>{var result=PickResources("图片",new[]{frame["res"]?.ToString()??""},false);if(result==null||result.Count==0)return;frame["res"]=result[0];Changed();Show();}))));
            var (lo,hi)=FrameDelay(frame);p.Children.Add(Ui.Card(Ui.Pair(Ui.Caption("帧时长"),new RangeSlider(1,Math.Max(10000,hi),lo,hi,(a,b)=>{frame["delay"]=a==b?JsonValue.Create((int)a):new JsonArray(JsonValue.Create((int)a),JsonValue.Create((int)b));Changed();},"ms"),300)));
            void XY(string title,string x,string y){var row=new StackPanel{Orientation=Orientation.Horizontal};foreach(var (key,label) in new[]{(x,"X"),(y,"Y")}){row.Children.Add(Ui.Text(label,12,true));var box=new TextBox{Width=65,Text=frame[key]?.ToString()??"",ToolTip="留空使用默认值",Margin=new Thickness(4,0,8,0)};void Commit(){if(string.IsNullOrWhiteSpace(box.Text))frame.Remove(key);else if(int.TryParse(box.Text,out var value))frame[key]=Math.Clamp(value,-500,500);else{box.Text=frame[key]?.ToString()??"";return;}Changed();}box.LostKeyboardFocus+=(_,_)=>Commit();box.KeyDown+=(_,e)=>{if(e.Key==System.Windows.Input.Key.Enter){Commit();e.Handled=true;}};row.Children.Add(box);}p.Children.Add(Ui.Card(Ui.Pair(Ui.Caption(title,"留空使用默认值"),row)));}
            XY("锚点","anchorX","anchorY");XY("逐帧位移","dx","dy");
            foreach(var (key,choices,labels,title) in new[]{("verticalAlign",new[]{"bottom","center","top"},new[]{"底部","居中","顶部"},"垂直对齐"),("transform",new[]{"none","flipX","flipY","rotate90","rotate180","rotate270"},new[]{"无","左右镜像","上下镜像","旋转90°","旋转180°","旋转270°"},"图片变换")}){var combo=new ComboBox{ItemsSource=labels,SelectedIndex=Math.Max(0,Array.IndexOf(choices,frame[key]?.ToString()??choices[0])),Width=140};combo.SelectionChanged+=(_,_)=>{if(combo.SelectedIndex>=0){frame[key]=choices[combo.SelectedIndex];Changed();}};p.Children.Add(Ui.Card(Ui.Pair(Ui.Caption(title),combo)));}
        }
        Show();dialog.ShowDialog();
    }
}
