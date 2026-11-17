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
    private StackPanel? animationList;private ContentControl? animationDetail;private string? selectedAnimation;
    private readonly TextBlock status=Ui.Text("",12,true);
    private bool dirty;private string selectedFolder="";private readonly IReadOnlyList<(string Name,string Root)> otherPacks;private string? selectedText;
    private readonly string[] requiredPaths={"standby/pool","standby/static_pool","pat_head/pat_head","snap/snap_head","snap/pool","snap/pat_head/pat_head","snap/feed/lure","snap/feed/lure_fail","snap/feed/eat","feed/lure","feed/lure_fail","feed/food","feed/eat","touch/lift_up/slow","touch/lift_up/left_medium","touch/lift_up/left_fast","touch/lift_up/right_medium","touch/lift_up/right_fast","touch/roll","touch/jump_down","climb/leap/leap","climb/leap/landing","climb/forage/move","menu/pool","menu/preview","menu/summoned"};
    public PackageEditorPage(string root,Action saved,Action back,IReadOnlyList<(string Name,string Root)>? otherPacks=null)
    {
        editor=new PackageEditor(root);this.saved=saved;this.back=back;this.otherPacks=otherPacks??Array.Empty<(string,string)>();
        var grid=new Grid();grid.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(145)});grid.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});Content=grid;
        var nav=new StackPanel{Margin=new Thickness(10,16,10,12)};grid.Children.Add(new Border{Background=Ui.Brush("Sidebar"),Child=nav});
        nav.Children.Add(Ui.Light("‹ 返回",()=>{if(!dirty||MessageBox.Show("放弃尚未保存的配置修改？已导入的图片文件会保留。","返回",MessageBoxButton.YesNo)==MessageBoxResult.Yes)back();}));
        var title=Ui.Caption(editor.Document["name"]?.ToString()??"素材包","素材包编辑",true);title.Margin=new Thickness(4,18,0,18);nav.Children.Add(title);
        var pages=new (string,Action)[]{("基本信息",Info),("功能映射",Functions),("动画",Animations),("图片资源",Images),("碎碎念文本",Thinking)};
        var navButtons=new List<Button>();foreach(var (name,action) in pages){int index=navButtons.Count;var b=Ui.Nav(name,()=>{for(int i=0;i<navButtons.Count;i++)navButtons[i].Background=Ui.Brush(i==index?"AccentSoft":"Sidebar");action();});navButtons.Add(b);nav.Children.Add(b);}navButtons[0].Background=Ui.Brush("AccentSoft");
        var right=new DockPanel{Margin=new Thickness(20)};Grid.SetColumn(right,1);grid.Children.Add(right);
        status.Text="无未保存修改";var footer=new DockPanel{Margin=new Thickness(0,0,0,12)};var save=Ui.Button("保存素材包",Save,true);DockPanel.SetDock(save,Dock.Right);footer.Children.Add(save);footer.Children.Add(status);DockPanel.SetDock(footer,Dock.Top);right.Children.Add(footer);right.Children.Add(host);Info();
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
        animationDetail=null;animationList=null;var p=Page("基本信息","修改素材包自身的信息，不改变已创建角色的名称。");
        foreach(var key in new[]{"name","shortName","id","note","usage"}){var k=key;var label=k switch{"name"=>"素材包名称","shortName"=>"简称","id"=>"标识","note"=>"备注",_=>"使用说明"};Field(p,label,editor.Document[k]?.ToString()??"",v=>editor.Document[k]=v,k is "note" or "usage");}
        p.Children.Add(Ui.Card(Ui.Caption("素材包版本",RolePackage.ContractVersion,true)));
    }
    private static string LoopTitle(string? key)=>key switch{"timed"=>"定时循环","infinite"=>"无限循环",_=>"单次播放"};
    private static string FunctionTitle(string path)=>string.Join(" / ",path.Split('/').Select((x,i)=>x switch{"standby"=>"待机","static_pool"=>"静止待机池","pool"=>i==1&&path.StartsWith("standby")?"待机池":"待机池","pat_head"=>"摸摸头","snap"=>"吸附","feed"=>"投食","touch"=>"抛掷","lift_up"=>"提起","roll"=>"滚动","jump_down"=>"落地","climb"=>"攀岩","leap"=>"飞跃","landing"=>"落地","forage"=>"觅食","move"=>"移动","menu"=>"菜单动画","preview"=>"预览","summoned"=>"已召唤","food"=>"食物","eat"=>"吃","lure"=>"引诱","lure_fail"=>"引诱失败","snap_head"=>"吸附","slow"=>"缓慢","left_medium"=>"左中速","left_fast"=>"左快速","right_medium"=>"右中速","right_fast"=>"右快速",_=>x}));
    private void ThinkingBinding(StackPanel page,JsonObject node,bool inherit)
    {
        var card=new SettingCard("碎碎念","设置文字播放方式与候选文本。",initiallyExpanded:false);page.Children.Add(card);
        var keys=inherit?new[]{"inherit","cycle","hold","off"}:new[]{"cycle","hold","off"};
        string Label(string key)=>key switch{"inherit"=>"继承功能","cycle"=>"轮播","hold"=>"持续",_=>"不播放"};
        var config=node["thinking"] as JsonObject;var policy=config?["policy"]?.ToString()??(inherit?"inherit":"cycle");
        var combo=new ComboBox{ItemsSource=keys.Select(Label).ToArray(),SelectedIndex=Math.Max(0,Array.IndexOf(keys,policy)),Width=140};
        card.Add(Ui.Pair(Ui.Caption("播放策略","继承时使用所属功能的碎碎念设置。"),combo));
        var poolPanel=new StackPanel();card.Add(poolPanel);
        JsonObject Ensure(){if(node["thinking"] is not JsonObject t){t=new JsonObject();node["thinking"]=t;}if(t["pool"] is not JsonObject)t["pool"]=new JsonObject();return t;}
        void Refresh()
        {
            poolPanel.Children.Clear();if(combo.SelectedIndex<0||keys[combo.SelectedIndex] is "inherit" or "off")return;
            var chosen=node["thinking"]?["pool"] as JsonObject;
            foreach(var pair in editor.Thinkings)
            {
                var id=pair.Key;var text=pair.Value?["text"]?.ToString()??id;var row=new StackPanel();
                var check=new CheckBox{IsChecked=chosen?.ContainsKey(id)==true,Style=(Style)Application.Current.FindResource("ResourceCheck")};
                row.Children.Add(Ui.Pair(Ui.Caption(text),check));var timing=new StackPanel{Visibility=check.IsChecked==true?Visibility.Visible:Visibility.Collapsed};
                var item=chosen?[id] as JsonObject;int lo=int.TryParse(item?["min"]?.ToString(),out var l)?Math.Max(0,l):500;int hi=int.TryParse(item?["max"]?.ToString(),out var h)?Math.Max(lo,h):4000;
                timing.Children.Add(new RangeSlider(0,Math.Max(20000,hi),lo,hi,(min,max)=>{var t=Ensure();t["policy"]=keys[combo.SelectedIndex];t["pool"]![id]=new JsonObject{["min"]=(int)min,["max"]=(int)max};Changed();},"ms"));row.Children.Add(timing);
                check.Click+=(_,_)=>{var t=Ensure();t["policy"]=keys[combo.SelectedIndex];var p=t["pool"]!.AsObject();if(check.IsChecked==true){p[id]=new JsonObject{["min"]=lo,["max"]=hi};timing.Visibility=Visibility.Visible;}else{p.Remove(id);timing.Visibility=Visibility.Collapsed;}Changed();};poolPanel.Children.Add(Ui.Card(row));
            }
        }
        combo.SelectionChanged+=(_,_)=>{if(combo.SelectedIndex<0)return;var t=Ensure();t["policy"]=keys[combo.SelectedIndex];Changed();Refresh();};Refresh();
    }
    private void Functions()
    {
        animationDetail=null;animationList=null;
        var layout=new DockPanel();var title=Ui.Caption("功能映射","选择一个功能，再为它绑定动画。",true);((TextBlock)title.Children[0]).FontSize=24;title.Margin=new Thickness(0,0,0,12);DockPanel.SetDock(title,Dock.Top);layout.Children.Add(title);
        var grid=new Grid();grid.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(190)});grid.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});
        var tree=new StackPanel{Margin=new Thickness(0,0,12,0)};grid.Children.Add(new ScrollViewer{Content=tree,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});
        var detail=new ContentControl();Grid.SetColumn(detail,1);grid.Children.Add(detail);layout.Children.Add(grid);host.Content=layout;
        var paths=requiredPaths.Concat(editor.FunctionPaths()).Distinct().ToArray();
        var ordered=new[]{"standby","pat_head","snap","feed","touch","climb","menu"};
        var groups=ordered.Select(x=>(Name:FunctionTitle(x),Paths:paths.Where(p=>p.Split('/')[0]==x&&requiredPaths.Contains(p)).ToArray())).Concat(new[]{(Name:"未定义",Paths:paths.Where(p=>!requiredPaths.Contains(p)).ToArray())});
        var buttons=new Dictionary<string,Button>();string? selected=null;
        void Show(string path)
        {
            selected=path;foreach(var pair in buttons)pair.Value.Background=Ui.Brush(pair.Key==path?"AccentSoft":"Sidebar");
            var pool=editor.Function(path)["modes"]!.AsArray();var chosen=pool.Select(x=>x?.ToString()??"").ToHashSet();
            var page=new StackPanel();var header=Ui.Caption(FunctionTitle(path),path=="standby/pool"?"至少保留一个有效动画。":"为此功能选择可以播放的动画。",true);header.Margin=new Thickness(0,0,0,10);page.Children.Add(header);
            var count=Ui.Text("",12,true);page.Children.Add(count);
            var search=new TextBox{Margin=new Thickness(0,8,0,8),ToolTip="搜索动画名称"};page.Children.Add(search);
            var list=new StackPanel();page.Children.Add(list);
            void RefreshCount(){count.Text=chosen.Count==0?"当前动画：未绑定":"当前动画："+string.Join(" · ",editor.Modes.Select(x=>x.Key).Where(chosen.Contains));}
            void RefreshList()
            {
                list.Children.Clear();
                foreach(var id in editor.Modes.Select(x=>x.Key).Where(x=>x.Contains(search.Text,StringComparison.OrdinalIgnoreCase)).ToArray())
                {
                    var left=new StackPanel{Orientation=Orientation.Horizontal};
                    try{var frame=editor.Frames(id).FirstOrDefault() as JsonObject;var image=new Image{Width=44,Height=44,Margin=new Thickness(0,0,10,0),Stretch=Stretch.Uniform};if(frame!=null){var b=new BitmapImage();b.BeginInit();b.CacheOption=BitmapCacheOption.OnLoad;b.UriSource=new Uri(Path.Combine(editor.Root,frame["res"]?.ToString()??""));b.EndInit();image.Source=b;}left.Children.Add(image);}catch{}
                    left.Children.Add(Ui.Caption(id,editor.Frames(id).Count+" 帧 · "+LoopTitle(editor.Mode(id)["loopMode"]?.ToString())));
                    var check=new CheckBox{IsChecked=chosen.Contains(id),Style=(Style)Application.Current.FindResource("ResourceCheck")};
                    check.Click+=(_,_)=>{if(check.IsChecked==true)chosen.Add(id);else chosen.Remove(id);editor.SetPool(path,editor.Modes.Select(x=>x.Key).Where(chosen.Contains));Changed();RefreshCount();};
                    list.Children.Add(Ui.Card(Ui.Pair(left,check)));
                }
            }
            RefreshCount();RefreshList();search.TextChanged+=(_,_)=>RefreshList();ThinkingBinding(page,editor.Function(path),false);
            detail.Content=new ScrollViewer{Content=page,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
        }
        foreach(var group in groups.Where(g=>g.Paths.Length>0))
        {
            var folder=new Expander{Header=group.Name+" · "+group.Paths.Length,IsExpanded=group.Name=="待机",Margin=new Thickness(0,3,0,3),Foreground=Ui.Brush("Ink")};var children=new StackPanel{Margin=new Thickness(8,3,0,0)};folder.Content=children;
            foreach(var path in group.Paths){var label=FunctionTitle(path);var b=Ui.Nav(label,()=>Show(path));b.ToolTip=label;buttons[path]=b;children.Children.Add(b);}tree.Children.Add(folder);
        }
        if(paths.Length>0)Show(paths[0]);
    }
    private FrameworkElement GroupToolbar(bool thinking,Action refresh)
    {
        var panel=new DockPanel{Margin=new Thickness(0,0,0,8)};var name=new TextBox{ToolTip="分组名称",Margin=new Thickness(0,0,5,0)};var add=Ui.Button("＋ 分组",()=>Safe(()=>{editor.AddGroup(thinking,name.Text);Changed();refresh();}));DockPanel.SetDock(add,Dock.Right);panel.Children.Add(add);panel.Children.Add(name);return panel;
    }
    private void GroupList(StackPanel list,bool thinking,string filter,string? selected,Action<string> select,Action refresh)
    {
        list.Children.Clear();foreach(var group in editor.Grouped(thinking))
        {
            var ids=group.Ids.Where(id=>id.Contains(filter,StringComparison.OrdinalIgnoreCase)||(thinking&&(editor.Thinkings[id]?["text"]?.ToString()??"").Contains(filter,StringComparison.OrdinalIgnoreCase))).ToArray();
            if(filter.Length>0&&ids.Length==0)continue;
            var head=new DockPanel();if(group.Name!="未分组"){var more=Ui.Light("⋯",()=>{});var menu=new ContextMenu();foreach(var (label,action) in new (string,Action)[]{("上移",()=>editor.MoveGroup(thinking,group.Name,-1)),("下移",()=>editor.MoveGroup(thinking,group.Name,1)),("解散",()=>{if(MessageBox.Show("解散分组？资源会保留。","解散",MessageBoxButton.YesNo)==MessageBoxResult.Yes)editor.DissolveGroup(thinking,group.Name);})}){var item=new MenuItem{Header=label};item.Click+=(_,_)=>Safe(()=>{action();Changed();refresh();});menu.Items.Add(item);}more.ContextMenu=menu;more.Click+=(_,_)=>menu.IsOpen=true;DockPanel.SetDock(more,Dock.Right);head.Children.Add(more);}
            head.Children.Add(Ui.Text(group.Name+" · "+group.Ids.Length,12,true));
            var body=new StackPanel{Margin=new Thickness(4,2,0,0)};var folder=new Expander{Header=head,Content=body,IsExpanded=filter.Length>0||ids.Contains(selected??"")||group.Name=="未分组",Margin=new Thickness(0,3,0,3),Foreground=Ui.Brush("Ink")};
            foreach(var id in ids){var label=thinking?(editor.Thinkings[id]?["text"]?.ToString()??id):id+" · "+editor.Frames(id).Count+" 帧";var button=Ui.Nav(label,()=>select(id));button.Tag=id;button.ToolTip=label;button.Background=Ui.Brush(id==selected?"AccentSoft":"Sidebar");body.Children.Add(button);}
            list.Children.Add(folder);
        }
    }
    private void MembershipEditor(StackPanel page,bool thinking,string id,Action refresh)
    {
        var card=new SettingCard("所属分组","可以加入多个分组；解散分组不会删除资源。",initiallyExpanded:false);
        foreach(var group in editor.Groups(thinking)){var check=new CheckBox{Content=group,IsChecked=editor.Membership(thinking,id).Contains(group)};check.Click+=(_,_)=>{var chosen=editor.Membership(thinking,id).ToList();if(check.IsChecked==true)chosen.Add(group);else chosen.RemoveAll(x=>x==group);editor.SetMembership(thinking,id,chosen);Changed();refresh();};card.Add(check);}
        page.Children.Add(card);
    }
    private void Animations()
    {
        var layout=new DockPanel();var header=Ui.Caption("动画","选择动画，在右侧修改播放循环、后续动画与逐帧属性。",true);((TextBlock)header.Children[0]).FontSize=24;header.Margin=new Thickness(0,0,0,12);DockPanel.SetDock(header,Dock.Top);layout.Children.Add(header);
        var grid=new Grid();grid.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(185)});grid.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});
        var left=new DockPanel{Margin=new Thickness(0,0,12,0)};var search=new TextBox{Margin=new Thickness(0,0,0,6),ToolTip="搜索动画"};DockPanel.SetDock(search,Dock.Top);left.Children.Add(search);
        var add=new TextBox{Margin=new Thickness(0,0,0,4),ToolTip="新动画名称"};var toolbar=new StackPanel{Margin=new Thickness(0,0,0,8)};toolbar.Children.Add(add);toolbar.Children.Add(Ui.Button("＋ 新建动画",()=>Safe(()=>{var image=editor.Images().FirstOrDefault()??throw new InvalidDataException("请先导入图片。");editor.AddMode(add.Text.Trim(),image);selectedAnimation=add.Text.Trim();Changed();Animations();}),true));toolbar.Children.Add(Ui.Light("从其他素材包导入",()=>ChooseExternal("动画",(source,id)=>{selectedAnimation=editor.ImportAnimation(source,id,"导入");Changed();Animations();})));toolbar.Children.Add(GroupToolbar(false,Animations));DockPanel.SetDock(toolbar,Dock.Top);left.Children.Add(toolbar);
        animationList=new StackPanel();left.Children.Add(new ScrollViewer{Content=animationList,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});grid.Children.Add(left);
        animationDetail=new ContentControl();Grid.SetColumn(animationDetail,1);grid.Children.Add(animationDetail);layout.Children.Add(grid);host.Content=layout;
        void RefreshList()=>GroupList(animationList,false,search.Text,selectedAnimation,id=>{Animation(id);RefreshList();},Animations);
        search.TextChanged+=(_,_)=>RefreshList();
        if(selectedAnimation==null||!editor.Modes.ContainsKey(selectedAnimation))selectedAnimation=editor.Modes.Select(x=>x.Key).FirstOrDefault();
        RefreshList();if(selectedAnimation!=null)Animation(selectedAnimation);else animationDetail.Content=Ui.Text("还没有动画，先新建一个吧。",14,true);
    }
    private void Animation(string id)
    {
        selectedAnimation=id;
        if(animationDetail==null){Animations();return;}
        if(animationList!=null)foreach(var button in animationList.Children.OfType<Button>())button.Background=Ui.Brush(button.Content?.ToString()?.StartsWith(id+" · ",StringComparison.Ordinal)==true?"AccentSoft":"Sidebar");
        var p=new StackPanel();var heading=Ui.Caption(id,"动画属性与逐帧编辑",true);heading.Margin=new Thickness(0,0,0,10);p.Children.Add(heading);animationDetail.Content=new ScrollViewer{Content=p,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
        MembershipEditor(p,false,id,Animations);
        var preview=new Image{Width=160,Height=160,Stretch=Stretch.Uniform};var previewNote=Ui.Text("预览忽略帧位移",12,true);var previewPanel=new StackPanel();previewPanel.Children.Add(preview);previewPanel.Children.Add(previewNote);p.Children.Add(Ui.Card(previewPanel));
        void ShowFrame(JsonObject frame){try{var bitmap=new BitmapImage();bitmap.BeginInit();bitmap.CacheOption=BitmapCacheOption.OnLoad;bitmap.UriSource=new Uri(Path.Combine(editor.Root,frame["res"]?.ToString()??""));bitmap.EndInit();preview.Source=bitmap;previewNote.Text=frame["res"]?.ToString()??"";}catch{preview.Source=null;previewNote.Text="图片不可用";}}
        if(editor.Frames(id).FirstOrDefault() is JsonObject first)ShowFrame(first);
        var progress=new ProgressBar{Height=5,Minimum=0,Maximum=1,Foreground=Ui.Brush("Accent"),Background=Ui.Brush("Line"),Margin=new Thickness(0,5,0,5)};previewPanel.Children.Add(progress);
        var timer=new System.Windows.Threading.DispatcherTimer{Interval=TimeSpan.FromMilliseconds(33)};
        AnimationCursor? cursor=null;WpfImageCache? cache=null;long last=0;
        void Replay(){try{var pack=editor.Preview();cursor=new AnimationCursor(pack,id,new Random());cache=new WpfImageCache(pack);last=Environment.TickCount64;timer.Start();}catch(Exception e){timer.Stop();previewNote.Text=e.Message;}}
        timer.Tick+=(_,_)=>{try{if(cursor==null||cache==null)return;var now=Environment.TickCount64;cursor.Advance(Math.Clamp(now-last,0,100));last=now;preview.Source=cache.Get(cursor.Current);previewNote.Text=string.Join(" > ",cursor.Chain)+" · "+cursor.Mode.Id+" · "+(cursor.FrameIndex+1)+"/"+cursor.Mode.Frames.Count+(cursor.Ended?" · 已结束":" · 播放中");progress.Value=cursor.Ended?1:cursor.TargetDuration is long target?Math.Clamp((double)cursor.ModeElapsed/Math.Max(1,target),0,1):(cursor.FrameIndex+(double)cursor.FrameElapsed/Math.Max(1,cursor.FrameDuration))/cursor.Mode.Frames.Count;if(cursor.Ended)timer.Stop();}catch(Exception e){timer.Stop();previewNote.Text=e.Message;}};
        previewPanel.Children.Add(Ui.Light("↻ 重播",Replay));p.Loaded+=(_,_)=>Replay();p.Unloaded+=(_,_)=>timer.Stop();
        var m=editor.Mode(id);ThinkingBinding(p,m,true);var name=new TextBox{Text=id,Width=180};var rename=new WrapPanel();rename.Children.Add(name);rename.Children.Add(Ui.Button("重命名",()=>Safe(()=>{editor.RenameMode(id,name.Text.Trim());selectedAnimation=name.Text.Trim();Changed();Animations();})));rename.Children.Add(Ui.Button("删除动画",()=>{var impact=editor.DeleteImpact(id);var note=impact.Count==0?"删除动画并取消所有功能及后续引用？":"删除后以下功能将失去唯一有效动画："+Environment.NewLine+string.Join(Environment.NewLine,impact.Select(FunctionTitle))+Environment.NewLine+"待机池缺失时将无法保存，其他功能可能被强制关闭。";if(MessageBox.Show(note,"删除",MessageBoxButton.YesNo)==MessageBoxResult.Yes){editor.DeleteMode(id);Changed();Animations();}}));p.Children.Add(Ui.Card(rename));
        var loopKeys=new[]{"off","timed","infinite"};var loop=new ComboBox{ItemsSource=loopKeys.Select(LoopTitle).ToArray(),SelectedIndex=Math.Max(0,Array.IndexOf(loopKeys,m["loopMode"]?.ToString()??"off")),Width=160};loop.SelectionChanged+=(_,_)=>{if(loop.SelectedIndex<0)return;m["loopMode"]=loopKeys[loop.SelectedIndex];if(loopKeys[loop.SelectedIndex]=="timed"&&m["duration"]==null)m["duration"]=new JsonObject{["min"]=1000,["max"]=3000};Changed();};p.Children.Add(Ui.Card(Ui.Pair(Ui.Caption("播放循环","单次 / 定时 / 无限"),loop)));
        var duration=m["duration"] as JsonObject??new JsonObject{["min"]=1000,["max"]=3000};if(m["duration"] is not JsonObject)m["duration"]=duration;
        Field(p,"定时最短 ms",duration["min"]?.ToString()??"1000",v=>{if(int.TryParse(v,out int n)&&n>0)duration["min"]=n;});Field(p,"定时最长 ms",duration["max"]?.ToString()??"3000",v=>{if(int.TryParse(v,out int n)&&n>0)duration["max"]=n;});
        var finish=Ui.Switch(m["finishCycle"]?.GetValue<bool>()??true);finish.Click+=(_,_)=>{m["finishCycle"]=finish.IsChecked==true;Changed();};p.Children.Add(Ui.Card(Ui.Pair(Ui.Caption("完成当前循环","定时结束后是否播完当前轮"),finish)));
        var next=new ComboBox{ItemsSource=new[]{""}.Concat(editor.Modes.Select(x=>x.Key)).ToArray(),SelectedItem=m["nextMode"]?.ToString()??"",Width=180};next.SelectionChanged+=(_,_)=>{var v=next.SelectedItem?.ToString();if(string.IsNullOrEmpty(v))m.Remove("nextMode");else m["nextMode"]=v;Changed();};p.Children.Add(Ui.Card(Ui.Pair(Ui.Caption("后续动画","无限循环不会进入后续动画。"),next)));
        var frames=editor.Frames(id);p.Children.Add(Ui.Button("＋ 新增帧",()=>Safe(()=>{var image=editor.Images().FirstOrDefault()??throw new InvalidDataException("没有图片。");frames.Add(new JsonObject{["res"]=image,["delay"]=100});Changed();Animation(id);}),true));
        for(int i=0;i<frames.Count;i++)
        {
            int index=i;var frame=frames[i]!.AsObject();var card=new SettingCard($"帧 {i+1}",frame["res"]?.ToString()??"",initiallyExpanded:false);
            var image=new ComboBox{ItemsSource=editor.Images(),SelectedItem=frame["res"]?.ToString(),MinWidth=200};image.SelectionChanged+=(_,_)=>{frame["res"]=image.SelectedItem?.ToString();Changed();};card.Add(Ui.Pair(Ui.Caption("图片资源"),image,300));card.Add(Ui.Button("查看本帧",()=>{timer.Stop();ShowFrame(frame);}));
            var delay=frame["delay"];int ReadInt(JsonNode? n,int fallback)=>n is JsonValue j&&j.TryGetValue<int>(out var v)?v:fallback;var min=delay is JsonArray ar&&ar.Count==2?ReadInt(ar[0],100):delay is JsonObject d?ReadInt(d["min"],100):ReadInt(delay,100);var max=delay is JsonArray ar2&&ar2.Count==2?ReadInt(ar2[1],min):delay is JsonObject d2?ReadInt(d2["max"],min):min;
            var range=new RangeSlider(1,Math.Max(10000,max),Math.Max(1,min),Math.Max(Math.Max(1,min),max),(lo,hi)=>{frame["delay"]=lo==hi?JsonValue.Create((int)lo):new JsonArray(JsonValue.Create((int)lo),JsonValue.Create((int)hi));Changed();},"ms");card.Add(Ui.Pair(Ui.Caption("帧时长","毫秒，最短至最长"),range,310));
            foreach(var key in new[]{"dx","dy","anchorX","anchorY"}){var k=key;int value=frame[k]?.GetValue<int>()??(k=="anchorX"?-1:0);card.Add(Ui.NumberRow(k switch{"dx"=>"水平位移","dy"=>"垂直位移","anchorX"=>"横向锚点",_=>"纵向锚点"},"",value,-500,500,1,"px",v=>{frame[k]=(int)v;Changed();}));}
            foreach(var (key,choices) in new[]{("verticalAlign",new[]{"bottom","center","top"}),("transform",new[]{"none","flipX","flipY","rotate90","rotate180","rotate270"})}){var k=key;string Label(string x)=>x switch{"bottom"=>"底部","center"=>"居中","top"=>"顶部","none"=>"无","flipX"=>"左右镜像","flipY"=>"上下镜像","rotate90"=>"旋转 90°","rotate180"=>"旋转 180°","rotate270"=>"旋转 270°",_=>x};var combo=new ComboBox{ItemsSource=choices.Select(Label).ToArray(),SelectedIndex=Math.Max(0,Array.IndexOf(choices,frame[k]?.ToString()??choices[0])),Width=130};combo.SelectionChanged+=(_,_)=>{if(combo.SelectedIndex<0)return;frame[k]=choices[combo.SelectedIndex];Changed();};card.Add(Ui.Pair(Ui.Caption(k=="verticalAlign"?"垂直对齐":"图片变换"),combo));}
            var actions=new WrapPanel();actions.Children.Add(Ui.Button("上移",()=>{if(index>0){var n=frames[index]!.DeepClone();frames.RemoveAt(index);frames.Insert(index-1,n);Changed();Animation(id);}}));actions.Children.Add(Ui.Button("下移",()=>{if(index+1<frames.Count){var n=frames[index]!.DeepClone();frames.RemoveAt(index);frames.Insert(index+1,n);Changed();Animation(id);}}));actions.Children.Add(Ui.Button("删除帧",()=>{if(frames.Count>1){frames.RemoveAt(index);Changed();Animation(id);}}));card.Add(actions);p.Children.Add(card);
        }
    }
    private void ChooseExternal(string type,Action<PackageEditor,string> accept)
    {
        if(otherPacks.Count==0){MessageBox.Show("没有其他素材包可以选择。");return;}
        var dialog=new Window{Title="从其他素材包选择"+type,Width=620,Height=540,MinWidth=440,MinHeight=360,Background=Ui.Brush("Canvas"),Owner=Window.GetWindow(this),WindowStartupLocation=WindowStartupLocation.CenterOwner};
        var dock=new DockPanel{Margin=new Thickness(16)};dialog.Content=dock;var packs=new ComboBox{ItemsSource=otherPacks.Select(x=>x.Name).ToArray(),SelectedIndex=0,Margin=new Thickness(0,0,0,8)};DockPanel.SetDock(packs,Dock.Top);dock.Children.Add(packs);
        var search=new TextBox{ToolTip="搜索资源",Margin=new Thickness(0,0,0,8)};DockPanel.SetDock(search,Dock.Top);dock.Children.Add(search);var list=new StackPanel();dock.Children.Add(new ScrollViewer{Content=list,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});
        void Refresh(){list.Children.Clear();if(packs.SelectedIndex<0)return;try{var source=new PackageEditor(otherPacks[packs.SelectedIndex].Root);var ids=type=="图片"?source.Images():type=="动画"?source.Modes.Select(x=>x.Key).ToArray():source.Thinkings.Select(x=>x.Key).ToArray();
            foreach(var id in ids){var label=type=="碎碎念"?source.Thinkings[id]?["text"]?.ToString()??id:id;if(!label.Contains(search.Text,StringComparison.OrdinalIgnoreCase))continue;var left=new StackPanel{Orientation=Orientation.Horizontal};if(type!="碎碎念"){var res=type=="图片"?id:source.Frames(id).FirstOrDefault()?["res"]?.ToString();if(res!=null)left.Children.Add(Thumbnail(source,res,40));}left.Children.Add(Ui.Caption(label));list.Children.Add(Ui.Card(Ui.Pair(left,Ui.Button("选择",()=>{try{accept(source,id);dialog.Close();}catch(Exception e){MessageBox.Show(dialog,e.Message,"导入失败");}}))));}
        }catch(Exception e){list.Children.Add(Ui.Text(e.Message));}}
        packs.SelectionChanged+=(_,_)=>Refresh();search.TextChanged+=(_,_)=>Refresh();Refresh();dialog.ShowDialog();
    }
    private static Image Thumbnail(PackageEditor source,string res,int size)
    {
        var image=new Image{Width=size,Height=size,Stretch=Stretch.Uniform,Margin=new Thickness(0,0,8,0)};try{var b=new BitmapImage();b.BeginInit();b.CacheOption=BitmapCacheOption.OnLoad;b.DecodePixelWidth=128;b.UriSource=new Uri(source.ResourcePath(res));b.EndInit();image.Source=b;}catch{}return image;
    }
    private void Images()
    {
        animationDetail=null;animationList=null;
        var layout=new DockPanel();var title=Ui.Caption("图片资源","按文件夹查看图片；已引用的图片需要先替换引用。",true);((TextBlock)title.Children[0]).FontSize=24;DockPanel.SetDock(title,Dock.Top);layout.Children.Add(title);
        var grid=new Grid{Margin=new Thickness(0,12,0,0)};grid.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(180)});grid.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});layout.Children.Add(grid);
        var tree=new StackPanel{Margin=new Thickness(0,0,10,0)};grid.Children.Add(new ScrollViewer{Content=tree,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});var detail=new ContentControl();Grid.SetColumn(detail,1);grid.Children.Add(detail);host.Content=layout;
        void Show(string folder)
        {
            selectedFolder=folder;var page=new StackPanel();var actions=new WrapPanel();
            actions.Children.Add(Ui.Button("导入图片",()=>Safe(()=>{var d=new OpenFileDialog{Filter="图片|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp",Multiselect=true};if(d.ShowDialog()!=true)return;foreach(var file in d.FileNames)if(WpfImageCache.IsReadable(file))editor.CopyImageFile(file,folder);Changed();Images();}),true));
            actions.Children.Add(Ui.Button("从其他素材包导入",()=>ChooseExternal("图片",(source,id)=>{editor.CopyImageFrom(source.Root,id,folder);Changed();Images();})));
            page.Children.Add(Ui.Pair(Ui.Caption(folder.Length==0?"未分组":folder,"图片资源",true),actions));
            var newFolder=new TextBox{ToolTip="新文件夹名称",Width=130};var create=new WrapPanel();create.Children.Add(newFolder);create.Children.Add(Ui.Button("＋ 文件夹",()=>Safe(()=>{selectedFolder=editor.NewFolder(folder,newFolder.Text);Changed();Images();})));page.Children.Add(create);
            foreach(var res in editor.Images().Where(x=>string.Equals(x.Contains('/')?x[..x.LastIndexOf('/') ]:"",folder,StringComparison.OrdinalIgnoreCase)))
            {
                var left=new StackPanel{Orientation=Orientation.Horizontal};left.Children.Add(Thumbnail(editor,res,64));var refs=editor.ImageReferences(res);left.Children.Add(Ui.Caption(Path.GetFileName(res),refs.Count+" 处引用",true));var buttons=new WrapPanel();
                buttons.Children.Add(Ui.Light("引用",()=>MessageBox.Show(refs.Count==0?"没有引用":string.Join(Environment.NewLine,refs),"图片引用")));
                buttons.Children.Add(Ui.Light("替换引用",()=>{var options=editor.Images().Where(x=>x!=res).ToArray();if(options.Length==0)return;var d=new Window{Title="选择替换图片",Width=460,Height=230,Owner=Window.GetWindow(this),WindowStartupLocation=WindowStartupLocation.CenterOwner,Background=Ui.Brush("Canvas")};var panel=new StackPanel{Margin=new Thickness(16)};var choice=new ComboBox{ItemsSource=options,SelectedIndex=0};panel.Children.Add(choice);panel.Children.Add(Ui.Button("确认替换",()=>{editor.ReplaceImageReferences(res,(string)choice.SelectedItem);Changed();d.Close();Images();},true));d.Content=panel;d.ShowDialog();}));
                var del=Ui.Light("删除",()=>Safe(()=>{if(editor.ImageReferenced(res))throw new InvalidDataException("图片仍被已保存或正在编辑的动画引用，请先替换并保存。");if(MessageBox.Show("永久删除图片？","删除",MessageBoxButton.YesNo)==MessageBoxResult.Yes){File.Delete(editor.ResourcePath(res));Images();}}));del.IsEnabled=refs.Count==0;buttons.Children.Add(del);page.Children.Add(Ui.Card(Ui.Pair(left,buttons)));
            }
            detail.Content=new ScrollViewer{Content=page,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
        }
        void AddFolders(StackPanel target,string parent){foreach(var name in editor.Folders(parent)){var path=parent.Length==0?name:parent+"/"+name;var head=new DockPanel();var select=Ui.Nav(name,()=>{Show(path);});select.ToolTip=path;head.Children.Add(select);var body=new StackPanel{Margin=new Thickness(9,0,0,0)};AddFolders(body,path);var exp=new Expander{Header=head,Content=body,IsExpanded=selectedFolder.StartsWith(path,StringComparison.Ordinal),Foreground=Ui.Brush("Ink")};var menu=new ContextMenu();foreach(var (label,delta) in new[]{("上移",-1),("下移",1)}){var item=new MenuItem{Header=label};item.Click+=(_,_)=>{editor.MoveFolderOrder(parent,name,delta);Changed();Images();};menu.Items.Add(item);}exp.ContextMenu=menu;target.Children.Add(exp);}}
        tree.Children.Add(Ui.Nav("未分组",()=>Show("")));AddFolders(tree,"");if(!Directory.Exists(editor.ResourcePath(selectedFolder)))selectedFolder="";Show(selectedFolder);
    }
    private void Thinking()
    {
        animationDetail=null;animationList=null;
        var layout=new DockPanel();var heading=Ui.Caption("碎碎念文本","按分组选择文本，在右侧修改内容和所属分组。",true);((TextBlock)heading.Children[0]).FontSize=24;DockPanel.SetDock(heading,Dock.Top);layout.Children.Add(heading);
        var grid=new Grid{Margin=new Thickness(0,12,0,0)};grid.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(205)});grid.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});layout.Children.Add(grid);
        var left=new DockPanel{Margin=new Thickness(0,0,12,0)};var search=new TextBox{ToolTip="搜索文本",Margin=new Thickness(0,0,0,6)};DockPanel.SetDock(search,Dock.Top);left.Children.Add(search);
        var toolbar=new StackPanel();toolbar.Children.Add(Ui.Button("＋ 新建文本",()=>{selectedText="t"+Guid.NewGuid().ToString("N").Substring(0,8);editor.Thinkings[selectedText]=new JsonObject{["text"]=""};Changed();Thinking();},true));toolbar.Children.Add(Ui.Light("从其他素材包导入",()=>ChooseExternal("碎碎念",(source,id)=>{selectedText=editor.ImportText(source,id);Changed();Thinking();})));toolbar.Children.Add(GroupToolbar(true,Thinking));DockPanel.SetDock(toolbar,Dock.Top);left.Children.Add(toolbar);
        var list=new StackPanel();left.Children.Add(new ScrollViewer{Content=list,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});grid.Children.Add(left);
        var detail=new ContentControl();Grid.SetColumn(detail,1);grid.Children.Add(detail);host.Content=layout;
        void Show(string id){selectedText=id;var node=editor.Thinkings[id]!.AsObject();var page=new StackPanel();page.Children.Add(Ui.Caption("碎碎念文本",id,true));Field(page,"文本",node["text"]?.ToString()??"",v=>node["text"]=v,true);MembershipEditor(page,true,id,Thinking);var refs=editor.ThinkingReferences(id).ToArray();var references=new SettingCard("引用位置",refs.Length+" 处引用",initiallyExpanded:false);foreach(var reference in refs){var check=new CheckBox{IsChecked=true,Style=(Style)Application.Current.FindResource("ResourceCheck")};var snapshot=reference.Node["thinking"]!["pool"]![id]!.DeepClone();check.Click+=(_,_)=>{var pool=reference.Node["thinking"]!["pool"]!.AsObject();if(check.IsChecked==true)pool[id]=snapshot.DeepClone();else pool.Remove(id);Changed();};references.Add(Ui.Pair(Ui.Caption(reference.Label),check));}page.Children.Add(references);page.Children.Add(Ui.Light("删除文本",()=>{if(MessageBox.Show("删除文本并取消引用？","删除",MessageBoxButton.YesNo)==MessageBoxResult.Yes){editor.DeleteThinking(id);Changed();Thinking();}}));detail.Content=new ScrollViewer{Content=page,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};}
        void Refresh()=>GroupList(list,true,search.Text,selectedText,id=>{Show(id);Refresh();},Thinking);
        if(selectedText==null||!editor.Thinkings.ContainsKey(selectedText))selectedText=editor.Thinkings.Select(x=>x.Key).FirstOrDefault();
        search.TextChanged+=(_,_)=>Refresh();Refresh();if(selectedText!=null)Show(selectedText);else detail.Content=Ui.Text("还没有碎碎念文本。",14,true);
    }
}
