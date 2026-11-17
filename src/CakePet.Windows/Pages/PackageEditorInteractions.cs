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
        if(root is Button b&&b.Tag is string tag)b.Background=Ui.Brush(tag==id?"AccentSoft":"Surface");
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
    private sealed record ResourceChoice(PackageEditor Source,string Id);
    private List<string>? PickResources(string type,IEnumerable<string> initial,bool multi,string? imageFolder=null,string? excluded=null)
    {
        var chosen=BrowseResources(type,initial,multi,false,excluded);
        if(chosen==null)return null;
        var result=new List<string>();
        try
        {
            foreach(var choice in chosen)
            {
                var id=ReferenceEquals(choice.Source,editor)?choice.Id:type switch{
                    "图片"=>editor.CopyImageFrom(choice.Source.Root,choice.Id,imageFolder??"导入"),
                    "动画"=>editor.ImportAnimation(choice.Source,choice.Id,"导入"),
                    _=>editor.ImportText(choice.Source,choice.Id)};
                if(!ReferenceEquals(choice.Source,editor))Changed();
                if(!result.Contains(id))result.Add(id);
            }
            return result;
        }
        catch(Exception e){MessageBox.Show("导入失败："+e.Message+"\n已成功复制的资源会保留，可在资源管理页查看。","资源导入");return null;}
    }
    private List<ResourceChoice>? BrowseResources(string type,IEnumerable<string> initial,bool multi,bool externalOnly=false,string? excluded=null)
    {
        var sources=new List<(string Name,PackageEditor Pack)>();
        if(!externalOnly)sources.Add(("当前素材包",editor));
        foreach(var other in otherPacks)try{sources.Add((other.Name,new PackageEditor(other.Root)));}catch{}
        if(sources.Count==0){MessageBox.Show("没有可选择的素材包。");return null;}
        var dialog=EditorDialog("选择"+type,680,640);dialog.MinWidth=480;
        var picked=initial.Where(x=>!string.IsNullOrWhiteSpace(x)&&x!=excluded).Distinct().Select(x=>new ResourceChoice(editor,x)).ToList();
        var state=new HashSet<string>();var root=new DockPanel{Margin=new Thickness(16)};dialog.Content=root;
        var top=new StackPanel();var sourceSelect=new ComboBox{ItemsSource=sources.Select(x=>x.Name).ToArray(),SelectedIndex=0,MinWidth=150,Margin=new Thickness(0,0,8,0)};var search=new TextBox{ToolTip="搜索"+type,MinHeight=32};var searchRow=new DockPanel{Margin=new Thickness(0,0,0,10)};DockPanel.SetDock(sourceSelect,Dock.Left);searchRow.Children.Add(sourceSelect);searchRow.Children.Add(search);top.Children.Add(searchRow);DockPanel.SetDock(top,Dock.Top);root.Children.Add(top);
        var footer=new DockPanel{Margin=new Thickness(0,12,0,0)};var buttons=new StackPanel{Orientation=Orientation.Horizontal};DockPanel.SetDock(buttons,Dock.Right);footer.Children.Add(buttons);
        var count=Ui.Text("",12,true);count.VerticalAlignment=VerticalAlignment.Center;footer.Children.Add(count);
        var clear=Ui.Light("清空",()=>{});buttons.Children.Add(clear);buttons.Children.Add(Ui.Button("取消",()=>dialog.DialogResult=false));buttons.Children.Add(Ui.Button("确认",()=>dialog.DialogResult=true,true));DockPanel.SetDock(footer,Dock.Bottom);root.Children.Add(footer);
        var list=new StackPanel();var scroll=new ScrollViewer{Content=list,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};root.Children.Add(scroll);
        var marks=new List<(ResourceChoice Choice,Button Button,TextBlock Mark)>();var counts=new List<(TextBlock Label,ResourceChoice[] Items)>();
        void UpdateSelection()
        {
            count.Text="已选择 "+picked.Count+" 个"+(picked.Any(x=>!ReferenceEquals(x.Source,editor))?" · 确认时复制到当前素材包":"");
            foreach(var (choice,button,mark) in marks){bool selected=picked.Contains(choice);mark.Text=multi?(selected?"☑":"☐"):(selected?"◉":"○");mark.Foreground=Ui.Brush(selected?"Accent":"Muted");button.Background=Ui.Brush(selected?"AccentSoft":"Surface");}
            foreach(var (label,items) in counts)label.Text=items.Count(picked.Contains)+"/"+items.Length;
        }
        void Toggle(ResourceChoice choice)
        {
            if(picked.Contains(choice)){if(multi)picked.Remove(choice);}
            else{if(!multi)picked.Clear();picked.Add(choice);}UpdateSelection();
        }
        string Label(PackageEditor source,string id)=>type=="碎碎念"?source.Thinkings[id]?["text"]?.ToString()??id:type=="图片"?Path.GetFileName(id):id;
        bool Matches(PackageEditor source,string id)=>id.Contains(search.Text,StringComparison.OrdinalIgnoreCase)||Label(source,id).Contains(search.Text,StringComparison.OrdinalIgnoreCase);
        void AddItem(StackPanel body,PackageEditor source,string id)
        {
            var choice=new ResourceChoice(source,id);var mark=Ui.Text("",20);mark.Width=25;mark.VerticalAlignment=VerticalAlignment.Center;
            var res=type=="图片"?id:type=="动画"?source.Frames(id).FirstOrDefault()?["res"]?.ToString():null;
            var note=type=="动画"?source.Frames(id).Count+" 帧 · "+LoopTitle(source.Mode(id)["loopMode"]?.ToString()):type=="图片"?id:"";
            var button=ResourceRow(Label(source,id),note,res==null?null:Thumbnail(source,res,40),mark,()=>Toggle(choice));marks.Add((choice,button,mark));body.Children.Add(button);
        }
        void AddFolder(StackPanel target,PackageEditor source,string key,string name,string[] ids,Action<StackPanel> populate)
        {
            var body=new StackPanel();var label=Ui.Text("",11,true);counts.Add((label,ids.Select(id=>new ResourceChoice(source,id)).ToArray()));
            var folder=ResourceFolder(source.Root+"|"+key,name,"",body,label,search.Text.Length>0,state);
            LoadOnExpand(folder,()=>{populate(body);UpdateSelection();});target.Children.Add(folder);
        }
        void Refresh()
        {
            list.Children.Clear();marks.Clear();counts.Clear();if(sourceSelect.SelectedIndex<0)return;var source=sources[sourceSelect.SelectedIndex].Pack;
            bool Eligible(string id)=>!(ReferenceEquals(source,editor)&&id==excluded)&&Matches(source,id);
            if(type=="图片")
            {
                var all=source.Images().Where(Eligible).ToArray();
                string Parent(string id)=>id.Contains('/')?id[..id.LastIndexOf('/')]:"";
                void Folders(StackPanel target,string parent)
                {
                    foreach(var name in source.Folders(parent))
                    {
                        var path=parent.Length==0?name:parent+"/"+name;var descendants=all.Where(x=>x.StartsWith(path+"/",StringComparison.OrdinalIgnoreCase)).ToArray();
                        if(search.Text.Length>0&&descendants.Length==0)continue;
                        AddFolder(target,source,"image:"+path,name,descendants,body=>{Folders(body,path);foreach(var id in descendants.Where(x=>Parent(x)==path))AddItem(body,source,id);});
                    }
                }
                Folders(list,"");var ungrouped=all.Where(x=>Parent(x)=="").ToArray();if(ungrouped.Length>0)AddFolder(list,source,"image:root","未分组",ungrouped,body=>{foreach(var id in ungrouped)AddItem(body,source,id);});
            }
            else foreach(var group in source.Grouped(type=="碎碎念"))
            {
                var ids=group.Ids.Where(Eligible).ToArray();if(search.Text.Length>0&&ids.Length==0)continue;
                AddFolder(list,source,"group:"+group.Name,group.Name,ids,body=>{foreach(var id in ids)AddItem(body,source,id);});
            }
            UpdateSelection();scroll.ScrollToTop();
        }
        clear.Click+=(_,_)=>{picked.Clear();UpdateSelection();};sourceSelect.SelectionChanged+=(_,_)=>Refresh();search.TextChanged+=(_,_)=>Refresh();Refresh();
        return dialog.ShowDialog()==true?picked:null;
    }
    private static void LoadOnExpand(Expander folder,Action populate)
    {
        bool loaded=false;void Load(){if(loaded)return;loaded=true;populate();}
        folder.Expanded+=(_,e)=>{if(e.Source==folder)Load();};if(folder.IsExpanded)Load();
    }
    private static Button ResourceRow(string title,string note,Image? thumbnail,FrameworkElement? action,Action select)
    {
        var row=new Grid();row.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});row.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});row.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
        if(thumbnail!=null)row.Children.Add(thumbnail);
        var label=Ui.Caption(title,note);Grid.SetColumn(label,1);row.Children.Add(label);
        if(action!=null){action.Margin=new Thickness(8,0,0,0);Grid.SetColumn(action,2);row.Children.Add(action);}
        var button=Ui.Nav(title,select);button.Content=row;button.Style=(Style)Application.Current.FindResource("ResourceRow");button.HorizontalContentAlignment=HorizontalAlignment.Stretch;button.ToolTip=title;return button;
    }
    private static void AddResourceSplitter(Grid grid)
    {
        grid.ColumnDefinitions[0].MaxWidth=360;grid.ColumnDefinitions[1].MinWidth=220;
        var splitter=new GridSplitter{Width=5,HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Stretch,Background=Ui.Brush("Line"),ResizeDirection=GridResizeDirection.Columns,ResizeBehavior=GridResizeBehavior.CurrentAndNext};Grid.SetColumn(splitter,0);grid.Children.Add(splitter);
    }
    private string? AskName(string title,string label,string value="")
    {
        var dialog=EditorDialog(title,430,210);var panel=new StackPanel();panel.Children.Add(Ui.Text(label));var input=new TextBox{Text=value,Margin=new Thickness(0,8,0,0)};panel.Children.Add(input);
        void Confirm(){if(string.IsNullOrWhiteSpace(input.Text))return;dialog.DialogResult=true;}
        input.KeyDown+=(_,e)=>{if(e.Key==System.Windows.Input.Key.Enter)Confirm();};DialogBody(dialog,panel,Confirm);dialog.Loaded+=(_,_)=>{input.Focus();input.SelectAll();};return dialog.ShowDialog()==true?input.Text.Trim():null;
    }
    private FrameworkElement ResourceToolbar(string title,Action create,Action group,Action import)
    {
        var panel=new DockPanel{Margin=new Thickness(0,0,0,8)};var actions=new StackPanel{Orientation=Orientation.Horizontal};DockPanel.SetDock(actions,Dock.Right);
        Button Icon(string glyph,string hint,Action click){var b=Ui.Light(glyph,click);b.Width=30;b.Padding=new Thickness(4);b.Margin=new Thickness(0);b.ToolTip=hint;return b;}
        actions.Children.Add(Icon("＋","新建"+title,create));var folder=Icon("\uE8F4","新建分组",group);folder.FontFamily=new FontFamily("Segoe MDL2 Assets");actions.Children.Add(folder);
        var more=Icon("⋯","更多操作",()=>{});var menu=new ContextMenu();var item=new MenuItem{Header="从其他素材包导入"};item.Click+=(_,_)=>import();menu.Items.Add(item);more.ContextMenu=menu;more.Click+=(_,_)=>menu.IsOpen=true;actions.Children.Add(more);
        panel.Children.Add(actions);panel.Children.Add(Ui.Text(title,14));return panel;
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
