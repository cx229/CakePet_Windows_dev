using System.IO;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using CakePet.Core;
namespace CakePet.Windows;

internal sealed class PackageEditorPage:UserControl
{
    private readonly PackageEditor editor;
    private readonly Action saved,back;
    private readonly ContentControl host=new();
    private readonly TextBlock status=Ui.Text("",12,true);
    private bool dirty;
    private readonly string[] requiredPaths={"standby/pool","standby/static_pool","pat_head/pat_head","snap/snap_head","snap/pool","snap/pat_head/pat_head","snap/feed/lure","snap/feed/lure_fail","snap/feed/eat","feed/lure","feed/lure_fail","feed/food","feed/eat","touch/lift_up/slow","touch/lift_up/left_medium","touch/lift_up/left_fast","touch/lift_up/right_medium","touch/lift_up/right_fast","touch/roll","touch/jump_down","climb/leap/leap","climb/leap/landing","climb/forage/move","menu/pool","menu/preview","menu/summoned"};
    public PackageEditorPage(string root,Action saved,Action back)
    {
        editor=new PackageEditor(root);this.saved=saved;this.back=back;
        var grid=new Grid();grid.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(188)});grid.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});Content=grid;
        var nav=new StackPanel{Margin=new Thickness(14,20,14,12)};grid.Children.Add(nav);
        nav.Children.Add(Ui.Button("‹ 返回",()=>{if(!dirty||MessageBox.Show("放弃尚未保存的配置修改？已导入的图片文件会保留。","返回",MessageBoxButton.YesNo)==MessageBoxResult.Yes)back();}));
        var title=Ui.Caption(editor.Document["name"]?.ToString()??"素材包","素材包编辑",true);title.Margin=new Thickness(4,18,0,18);nav.Children.Add(title);
        var pages=new (string,Action)[]{("基本信息",Info),("功能映射",Functions),("动画",Animations),("图片资源",Images),("碎碎念文本",Thinking)};
        foreach(var (name,action) in pages){var b=Ui.Button(name,action);b.HorizontalContentAlignment=HorizontalAlignment.Left;b.Margin=new Thickness(0,3,0,3);nav.Children.Add(b);}
        var right=new DockPanel{Margin=new Thickness(20)};Grid.SetColumn(right,1);grid.Children.Add(right);
        var footer=new DockPanel{Margin=new Thickness(0,12,0,0)};var save=Ui.Button("保存素材包",Save,true);DockPanel.SetDock(save,Dock.Right);footer.Children.Add(save);footer.Children.Add(status);DockPanel.SetDock(footer,Dock.Bottom);right.Children.Add(footer);right.Children.Add(host);Info();
    }
    private void Changed(){dirty=true;status.Text="有未保存的修改";}
    private void Safe(Action a){try{a();}catch(Exception e){MessageBox.Show(e.Message,"素材包编辑");}}
    private void Save()=>Safe(()=>{var pack=editor.Save(WpfImageCache.IsReadable);dirty=false;status.Text=pack.Warnings.Count==0?"已保存":$"已保存 · {pack.Warnings.Count} 项动画警告";saved();if(pack.Warnings.Count>0)MessageBox.Show(string.Join(Environment.NewLine,pack.Warnings.Take(20)),"素材包警告");});
    private StackPanel Page(string title,string note)
    {var p=new StackPanel{MaxWidth=900};var header=Ui.Caption(title,note,true);((TextBlock)header.Children[0]).FontSize=24;header.Margin=new Thickness(0,0,0,18);p.Children.Add(header);host.Content=new ScrollViewer{Content=p,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};return p;}
    private void Field(StackPanel panel,string label,string value,Action<string> set,bool multiline=false)
    {var box=new TextBox{Text=value,AcceptsReturn=multiline,TextWrapping=TextWrapping.Wrap,MinHeight=multiline?75:32};box.TextChanged+=(_,_)=>{set(box.Text);Changed();};panel.Children.Add(Ui.Card(Ui.Pair(Ui.Caption(label),box,300)));}
    private void Info()
    {
        var p=Page("基本信息","修改素材包自身的信息，不改变已创建角色的名称。");
        foreach(var key in new[]{"name","shortName","id","note","usage"}){var k=key;var label=k switch{"name"=>"素材包名称","shortName"=>"简称","id"=>"标识","note"=>"备注",_=>"使用说明"};Field(p,label,editor.Document[k]?.ToString()??"",v=>editor.Document[k]=v,k is "note" or "usage");}
        p.Children.Add(Ui.Card(Ui.Caption("素材包版本",RolePackage.ContractVersion,true)));
    }
    private void Functions()
    {
        var p=Page("功能映射","选择每个功能对应的候选动画。待机池至少保留一个有效动画。");
        foreach(var path in requiredPaths.Concat(editor.FunctionPaths()).Distinct())
        {
            var pool=editor.Function(path)["modes"]!.AsArray();var selected=pool.Select(x=>x?.ToString()??"").ToHashSet();
            var card=new SettingCard(path,selected.Count==0?"未绑定动画":string.Join(" · ",selected),initiallyExpanded:false);
            foreach(var id in editor.Modes.Select(x=>x.Key).ToArray())
            {var c=new CheckBox{Content=id,IsChecked=selected.Contains(id),Style=(Style)Application.Current.FindResource("Direction"),HorizontalAlignment=HorizontalAlignment.Left};c.Click+=(_,_)=>{if(c.IsChecked==true)selected.Add(id);else selected.Remove(id);editor.SetPool(path,editor.Modes.Select(x=>x.Key).Where(selected.Contains));Changed();};card.Add(c);}p.Children.Add(card);
        }
    }
    private void Animations()
    {
        var p=Page("动画","编辑播放策略、后续动画和逐帧属性。");
        var add=new TextBox{Width=180};var row=new WrapPanel();row.Children.Add(add);row.Children.Add(Ui.Button("新建动画",()=>Safe(()=>{var image=editor.Images().FirstOrDefault()??throw new InvalidDataException("请先导入图片。");editor.AddMode(add.Text.Trim(),image);Changed();Animation(add.Text.Trim());}),true));p.Children.Add(Ui.Card(row));
        foreach(var id in editor.Modes.Select(x=>x.Key).ToArray())
        {var frames=editor.Frames(id);p.Children.Add(Ui.Card(Ui.Pair(Ui.Caption(id,$"{frames.Count} 帧 · {editor.Mode(id)["loopMode"]}",true),Ui.Button("编辑 ›",()=>Animation(id)))));}
    }
    private void Animation(string id)
    {
        var p=Page(id,"动画属性与逐帧编辑");p.Children.Add(Ui.Button("‹ 动画列表",Animations));
        var preview=new Image{Width=160,Height=160,Stretch=Stretch.Uniform};var previewNote=Ui.Text("预览忽略帧位移",12,true);var previewPanel=new StackPanel();previewPanel.Children.Add(preview);previewPanel.Children.Add(previewNote);p.Children.Add(Ui.Card(previewPanel));
        void ShowFrame(JsonObject frame){try{var bitmap=new BitmapImage();bitmap.BeginInit();bitmap.CacheOption=BitmapCacheOption.OnLoad;bitmap.UriSource=new Uri(Path.Combine(editor.Root,frame["res"]?.ToString()??""));bitmap.EndInit();preview.Source=bitmap;previewNote.Text=frame["res"]?.ToString()??"";}catch{preview.Source=null;previewNote.Text="图片不可用";}}
        if(editor.Frames(id).FirstOrDefault() is JsonObject first)ShowFrame(first);
        var m=editor.Mode(id);var name=new TextBox{Text=id,Width=180};var rename=new WrapPanel();rename.Children.Add(name);rename.Children.Add(Ui.Button("重命名",()=>Safe(()=>{editor.RenameMode(id,name.Text.Trim());Changed();Animation(name.Text.Trim());})));rename.Children.Add(Ui.Button("删除动画",()=>{if(MessageBox.Show("删除动画并取消所有功能及后续引用？待机池缺失时将无法保存。","删除",MessageBoxButton.YesNo)==MessageBoxResult.Yes){editor.DeleteMode(id);Changed();Animations();}}));p.Children.Add(Ui.Card(rename));
        var loop=new ComboBox{ItemsSource=new[]{"off","timed","infinite"},SelectedItem=m["loopMode"]?.ToString()??"off",Width=160};loop.SelectionChanged+=(_,_)=>{m["loopMode"]=loop.SelectedItem?.ToString();if(loop.SelectedItem?.ToString()=="timed"&&m["duration"]==null)m["duration"]=new JsonObject{["min"]=1000,["max"]=3000};Changed();};p.Children.Add(Ui.Card(Ui.Pair(Ui.Caption("播放循环","单次 / 定时 / 无限"),loop)));
        var duration=m["duration"] as JsonObject??new JsonObject{["min"]=1000,["max"]=3000};if(m["duration"] is not JsonObject)m["duration"]=duration;
        Field(p,"定时最短 ms",duration["min"]?.ToString()??"1000",v=>{if(int.TryParse(v,out int n)&&n>0)duration["min"]=n;});Field(p,"定时最长 ms",duration["max"]?.ToString()??"3000",v=>{if(int.TryParse(v,out int n)&&n>0)duration["max"]=n;});
        var finish=Ui.Switch(m["finishCycle"]?.GetValue<bool>()??true);finish.Click+=(_,_)=>{m["finishCycle"]=finish.IsChecked==true;Changed();};p.Children.Add(Ui.Card(Ui.Pair(Ui.Caption("完成当前循环","定时结束后是否播完当前轮"),finish)));
        var next=new ComboBox{ItemsSource=new[]{""}.Concat(editor.Modes.Select(x=>x.Key)).ToArray(),SelectedItem=m["nextMode"]?.ToString()??"",Width=180};next.SelectionChanged+=(_,_)=>{var v=next.SelectedItem?.ToString();if(string.IsNullOrEmpty(v))m.Remove("nextMode");else m["nextMode"]=v;Changed();};p.Children.Add(Ui.Card(Ui.Pair(Ui.Caption("后续动画","无限循环不会进入后续动画。"),next)));
        var frames=editor.Frames(id);p.Children.Add(Ui.Button("＋ 新增帧",()=>Safe(()=>{var image=editor.Images().FirstOrDefault()??throw new InvalidDataException("没有图片。");frames.Add(new JsonObject{["res"]=image,["delay"]=100});Changed();Animation(id);}),true));
        for(int i=0;i<frames.Count;i++)
        {
            int index=i;var frame=frames[i]!.AsObject();var card=new SettingCard($"帧 {i+1}",frame["res"]?.ToString()??"",initiallyExpanded:false);
            var image=new ComboBox{ItemsSource=editor.Images(),SelectedItem=frame["res"]?.ToString(),MinWidth=200};image.SelectionChanged+=(_,_)=>{frame["res"]=image.SelectedItem?.ToString();Changed();};card.Add(Ui.Pair(Ui.Caption("图片资源"),image,300));card.Add(Ui.Button("查看本帧",()=>ShowFrame(frame)));
            var delay=frame["delay"];int ReadInt(JsonNode? n,int fallback)=>n is JsonValue j&&j.TryGetValue<int>(out var v)?v:fallback;var min=delay is JsonArray ar&&ar.Count==2?ReadInt(ar[0],100):delay is JsonObject d?ReadInt(d["min"],100):ReadInt(delay,100);var max=delay is JsonArray ar2&&ar2.Count==2?ReadInt(ar2[1],min):delay is JsonObject d2?ReadInt(d2["max"],min):min;
            var range=new RangeSlider(1,Math.Max(10000,max),Math.Max(1,min),Math.Max(Math.Max(1,min),max),(lo,hi)=>{frame["delay"]=lo==hi?JsonValue.Create((int)lo):new JsonArray(JsonValue.Create((int)lo),JsonValue.Create((int)hi));Changed();});card.Add(Ui.Pair(Ui.Caption("帧时长","毫秒，最短至最长"),range,310));
            foreach(var key in new[]{"dx","dy","anchorX","anchorY"}){var k=key;int value=frame[k]?.GetValue<int>()??(k=="anchorX"?-1:0);card.Add(Ui.NumberRow(k,"",value,-500,500,1,"px",v=>{frame[k]=(int)v;Changed();}));}
            foreach(var (key,choices) in new[]{("verticalAlign",new[]{"bottom","center","top"}),("transform",new[]{"none","flipX","flipY","rotate90","rotate180","rotate270"})}){var k=key;var combo=new ComboBox{ItemsSource=choices,SelectedItem=frame[k]?.ToString()??choices[0],Width=160};combo.SelectionChanged+=(_,_)=>{frame[k]=combo.SelectedItem?.ToString();Changed();};card.Add(Ui.Pair(Ui.Caption(k),combo));}
            var actions=new WrapPanel();actions.Children.Add(Ui.Button("上移",()=>{if(index>0){var n=frames[index]!.DeepClone();frames.RemoveAt(index);frames.Insert(index-1,n);Changed();Animation(id);}}));actions.Children.Add(Ui.Button("下移",()=>{if(index+1<frames.Count){var n=frames[index]!.DeepClone();frames.RemoveAt(index);frames.Insert(index+1,n);Changed();Animation(id);}}));actions.Children.Add(Ui.Button("删除帧",()=>{if(frames.Count>1){frames.RemoveAt(index);Changed();Animation(id);}}));card.Add(actions);p.Children.Add(card);
        }
    }
    private void Images()
    {
        var p=Page("图片资源","实际文件直接存放在素材包中；已引用的图片不允许删除。");
        p.Children.Add(Ui.Button("导入图片",()=>Safe(()=>{var d=new OpenFileDialog{Filter="图片|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp",Multiselect=true};if(d.ShowDialog()!=true)return;var folder=Path.Combine(editor.Root,"导入");Directory.CreateDirectory(folder);foreach(var file in d.FileNames){if(!WpfImageCache.IsReadable(file))continue;var target=Path.Combine(folder,Path.GetFileName(file));if(File.Exists(target))target=Path.Combine(folder,Guid.NewGuid().ToString("N").Substring(0,8)+"-"+Path.GetFileName(file));File.Copy(file,target);}Images();}),true));
        foreach(var res in editor.Images())
        {var row=new DockPanel();try{var bitmap=new BitmapImage();bitmap.BeginInit();bitmap.CacheOption=BitmapCacheOption.OnLoad;bitmap.UriSource=new Uri(Path.Combine(editor.Root,res));bitmap.EndInit();var image=new Image{Source=bitmap,Width=64,Height=64,Stretch=Stretch.Uniform,Margin=new Thickness(0,0,14,0)};DockPanel.SetDock(image,Dock.Left);row.Children.Add(image);}catch{}var button=Ui.Button("删除",()=>Safe(()=>{if(editor.ImageReferenced(res))throw new InvalidDataException("图片仍被动画引用。");if(MessageBox.Show("永久删除图片？","删除",MessageBoxButton.YesNo)!=MessageBoxResult.Yes)return;File.Delete(Path.Combine(editor.Root,res));Images();}));button.IsEnabled=!editor.ImageReferenced(res);DockPanel.SetDock(button,Dock.Right);row.Children.Add(button);row.Children.Add(Ui.Caption(res,editor.ImageReferenced(res)?"已引用":"未引用"));p.Children.Add(Ui.Card(row));}
    }
    private void Thinking()
    {
        var p=Page("碎碎念文本","编辑素材包的文本资源；删除会同时取消引用。");
        p.Children.Add(Ui.Button("＋ 新建文本",()=>{var id="t"+Guid.NewGuid().ToString("N").Substring(0,8);editor.Thinkings[id]=new JsonObject{["text"]="新碎碎念"};Changed();Thinking();},true));
        foreach(var id in editor.Thinkings.Select(x=>x.Key).ToArray())
        {if(editor.Thinkings[id] is not JsonObject node)continue;Field(p,id,node["text"]?.ToString()??"",v=>node["text"]=v,true);p.Children.Add(Ui.Button("删除 "+id,()=>{if(MessageBox.Show("删除文本并取消引用？","删除",MessageBoxButton.YesNo)==MessageBoxResult.Yes){editor.DeleteThinking(id);Changed();Thinking();}}));}
    }
}
