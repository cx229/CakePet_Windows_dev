using System.IO;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using CakePet.Core;
using Frame = CakePet.Core.Frame;
namespace CakePet.Windows;

internal sealed partial class PackageEditorPage:UserControl
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
    internal bool HasUnsavedChanges=>dirty;
    internal bool ConfirmDiscard()=>!dirty||MessageBox.Show("放弃尚未保存的配置修改？已导入的图片文件会保留。","关闭设置",MessageBoxButton.YesNo)==MessageBoxResult.Yes;
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
    private static string FunctionTitle(string path)=>string.Join(" / ",path.Split('/').Select((x,i)=>x switch{"standby"=>"待机","static_pool"=>"静止待机池","pool"=>path=="menu/pool"?"菜单动画池":"待机池","pat_head"=>"摸摸头","snap"=>"吸附","feed"=>"投食","touch"=>"抛掷","lift_up"=>"提起","roll"=>"滚动","jump_down"=>"落地","climb"=>"攀岩","leap"=>"飞跃","landing"=>"落地","forage"=>"觅食","move"=>"移动","menu"=>"菜单动画","preview"=>"预览","summoned"=>"已召唤","food"=>"食物","eat"=>"吃","lure"=>"引诱","lure_fail"=>"引诱失败","snap_head"=>"吸附","slow"=>"缓慢","left_medium"=>"左中速","left_fast"=>"左快速","right_medium"=>"右中速","right_fast"=>"右快速",_=>x}));
    private void ThinkingBinding(StackPanel page,JsonObject node,bool inherit)
    {
        var card=new SettingCard("碎碎念","设置播放方式与已绑定文本。",collapsible:false);page.Children.Add(card);card.SetExpanded(true);
        var keys=inherit?new[]{"inherit","cycle","hold","off"}:new[]{"cycle","hold","off"};
        string Label(string key)=>key switch{"inherit"=>"继承功能","cycle"=>"轮播","hold"=>"持续",_=>"不播放"};
        var policy=node["thinking"]?["policy"]?.ToString()??(inherit?"inherit":"cycle");
        var combo=new ComboBox{ItemsSource=keys.Select(Label).ToArray(),SelectedIndex=Math.Max(0,Array.IndexOf(keys,policy)),Width=140};
        card.Add(Ui.Pair(Ui.Caption("播放策略",inherit?"继承时使用所属功能的配置。":""),combo));
        var poolPanel=new StackPanel();card.Add(poolPanel);
        JsonObject Ensure(){if(node["thinking"] is not JsonObject t){t=new JsonObject();node["thinking"]=t;}if(t["pool"] is not JsonObject)t["pool"]=new JsonObject();return t;}
        void Refresh()
        {
            poolPanel.Children.Clear();if(combo.SelectedIndex<0||keys[combo.SelectedIndex] is "inherit" or "off"){poolPanel.Visibility=Visibility.Collapsed;return;}poolPanel.Visibility=Visibility.Visible;
            var chosen=node["thinking"]?["pool"] as JsonObject;
            poolPanel.Children.Add(Ui.Pair(Ui.Caption("已绑定碎碎念",(chosen?.Count??0)+" 条文本"),Ui.Light("编辑 ›",()=>{if(EditThinkingPool(node))Refresh();})));

        }
        combo.SelectionChanged+=(_,_)=>{if(combo.SelectedIndex<0)return;Ensure()["policy"]=keys[combo.SelectedIndex];Changed();Refresh();};Refresh();
    }
    private void Functions()
    {
        animationDetail=null;animationList=null;
        var layout=new DockPanel();var title=Ui.Caption("功能映射","选择一个功能，再为它绑定动画。",true);((TextBlock)title.Children[0]).FontSize=24;title.Margin=new Thickness(0,0,0,12);DockPanel.SetDock(title,Dock.Top);layout.Children.Add(title);
        var grid=new Grid();grid.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(220),MinWidth=160});grid.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});
        var tree=new StackPanel{Margin=new Thickness(0,0,12,0)};grid.Children.Add(new ScrollViewer{Content=tree,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});
        var detail=new ContentControl();Grid.SetColumn(detail,1);grid.Children.Add(detail);AddResourceSplitter(grid);layout.Children.Add(grid);host.Content=layout;
        var paths=requiredPaths.Concat(editor.FunctionPaths()).Distinct().ToArray();
        var ordered=new[]{"standby","pat_head","snap","feed","touch","climb","menu"};
        var groups=ordered.Select(x=>(Name:FunctionTitle(x),Paths:paths.Where(p=>p.Split('/')[0]==x&&requiredPaths.Contains(p)).ToArray())).Concat(new[]{(Name:"未定义",Paths:paths.Where(p=>!requiredPaths.Contains(p)).ToArray())});
        var buttons=new Dictionary<string,Button>();string? selected=null;
        void Show(string path)
        {
            selected=path;foreach(var pair in buttons)pair.Value.Background=Ui.Brush(pair.Key==path?"AccentSoft":"Sidebar");
            var pool=editor.Function(path)["modes"]!.AsArray();var chosen=pool.Select(x=>x?.ToString()??"").ToHashSet();
            var page=new StackPanel();var header=Ui.Caption(FunctionTitle(path),path=="standby/pool"?"至少保留一个有效动画。":"为此功能选择可以播放的动画。",true);header.Margin=new Thickness(0,0,0,10);page.Children.Add(header);
            var summary=new StackPanel();page.Children.Add(Ui.Card(summary));
            void RefreshCount(){summary.Children.Clear();var names=editor.Function(path)["modes"]!.AsArray().Select(x=>x?.ToString()??"").ToArray();summary.Children.Add(Ui.Pair(Ui.Caption("绑定动画",names.Length==0?"当前动画：未绑定":"当前动画："+string.Join(" · ",names)),Ui.Light("编辑 ›",()=>{var result=PickResources("动画",names,true);if(result==null)return;if(!TrySetFunctionPool(path,result))return;RefreshCount();})));}

            RefreshCount();if(requiredPaths.Contains(path))ThinkingBinding(page,editor.Function(path),false);else summary.IsEnabled=false;
            detail.Content=new ScrollViewer{Content=page,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
        }
        foreach(var group in groups.Where(g=>g.Paths.Length>0))
        {
            var children=new StackPanel();var folder=ResourceFolder("function:"+group.Name,group.Name,group.Paths.Length+" 个功能",children);
            foreach(var path in group.Paths){var label=FunctionTitle(path);var b=ResourceRow(string.Join(" / ",label.Split(" / ").Skip(1)), "",null,null,()=>Show(path));b.ToolTip=label;buttons[path]=b;children.Children.Add(b);}tree.Children.Add(folder);
        }
        detail.Content=Ui.Caption("选择功能","展开左侧文件夹，选择要编辑的功能。",true);
    }
    private void CreateGroup(bool thinking,Action refresh)
    {
        var name=AskName("新建分组","分组名称");if(name==null)return;Safe(()=>{editor.AddGroup(thinking,name);Changed();refresh();});
    }
    private void GroupList(StackPanel list,bool thinking,string filter,string? selected,Action<string> select,Action refresh)
    {
        list.Children.Clear();foreach(var group in editor.Grouped(thinking))
        {
            var ids=group.Ids.Where(id=>id.Contains(filter,StringComparison.OrdinalIgnoreCase)||(thinking&&(editor.Thinkings[id]?["text"]?.ToString()??"").Contains(filter,StringComparison.OrdinalIgnoreCase))).ToArray();
            if(filter.Length>0&&ids.Length==0)continue;
            var head=new DockPanel();if(group.Name!="未分组"){var more=Ui.Light("⋯",()=>{});var menu=new ContextMenu();foreach(var (label,action) in new (string,Action)[]{("上移",()=>editor.MoveGroup(thinking,group.Name,-1)),("下移",()=>editor.MoveGroup(thinking,group.Name,1)),("解散",()=>{if(MessageBox.Show("解散分组？资源会保留。","解散",MessageBoxButton.YesNo)==MessageBoxResult.Yes)editor.DissolveGroup(thinking,group.Name);})}){var item=new MenuItem{Header=label};item.Click+=(_,_)=>Safe(()=>{action();Changed();refresh();});menu.Items.Add(item);}more.ContextMenu=menu;more.Click+=(_,_)=>menu.IsOpen=true;DockPanel.SetDock(more,Dock.Right);head.Children.Add(more);}
            
            var body=new StackPanel();var folder=ResourceFolder((thinking?"text:":"animation:")+group.Name,group.Name,group.Ids.Length+" 个",body,head,filter.Length>0);
            LoadOnExpand(folder,()=>{foreach(var id in ids){var label=thinking?(editor.Thinkings[id]?["text"]?.ToString()??id):id;var res=thinking?null:editor.Frames(id).FirstOrDefault()?["res"]?.ToString();var button=ResourceRow(label,thinking?"":editor.Frames(id).Count+" 帧 · "+LoopTitle(editor.Mode(id)["loopMode"]?.ToString()),res==null?null:Thumbnail(editor,res,32),null,()=>{select(id);Highlight(list,id);});button.Tag=id;button.Background=Ui.Brush(id==(thinking?selectedText:selectedAnimation)?"AccentSoft":"Surface");body.Children.Add(button);}});
            list.Children.Add(folder);
        }
    }
    private void MembershipEditor(StackPanel page,bool thinking,string id,Action refresh)
    {
        var summary=Ui.Caption("所属分组",string.Join(" · ",editor.Membership(thinking,id)));page.Children.Add(Ui.Card(Ui.Pair(summary,Ui.Light("编辑 ›",()=>{
            var dialog=EditorDialog("修改所属分组",440,460);var panel=new StackPanel();var chosen=editor.Membership(thinking,id).ToHashSet();
            foreach(var group in editor.Groups(thinking)){var check=ResourceCheck(chosen.Contains(group));check.Click+=(_,_)=>{if(check.IsChecked==true)chosen.Add(group);else chosen.Remove(group);};panel.Children.Add(Ui.Card(Ui.Pair(Ui.Caption(group),check)));}
            DialogBody(dialog,panel,()=>{editor.SetMembership(thinking,id,chosen);Changed();dialog.DialogResult=true;});if(dialog.ShowDialog()==true)refresh();
        }))));
    }
    private void Animations()
    {
        var layout=new DockPanel();var header=Ui.Caption("动画","选择动画，在右侧修改播放循环、后续动画与逐帧属性。",true);((TextBlock)header.Children[0]).FontSize=24;header.Margin=new Thickness(0,0,0,12);DockPanel.SetDock(header,Dock.Top);layout.Children.Add(header);
        var grid=new Grid();grid.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(220),MinWidth=160});grid.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});
        var left=new DockPanel{Margin=new Thickness(0,0,12,0)};var search=new TextBox{Margin=new Thickness(0,0,0,6),ToolTip="搜索动画"};DockPanel.SetDock(search,Dock.Top);left.Children.Add(search);
        var toolbar=ResourceToolbar("动画",()=>Safe(()=>{var name=AskName("新建动画","动画名称");if(name==null)return;if(editor.Modes.ContainsKey(name))throw new InvalidDataException("动画名称重复。");var images=PickResources("图片",Array.Empty<string>(),false);if(images==null||images.Count==0)return;editor.AddMode(name,images[0]);selectedAnimation=name;Changed();Animations();}),()=>CreateGroup(false,Animations),()=>ChooseExternal("动画",(source,id)=>{selectedAnimation=editor.ImportAnimation(source,id,"导入");Changed();Animations();}));DockPanel.SetDock(toolbar,Dock.Top);left.Children.Add(toolbar);
        animationList=new StackPanel();left.Children.Add(new ScrollViewer{Content=animationList,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});grid.Children.Add(left);
        animationDetail=new ContentControl();Grid.SetColumn(animationDetail,1);grid.Children.Add(animationDetail);AddResourceSplitter(grid);layout.Children.Add(grid);host.Content=layout;
        void RefreshList()=>GroupList(animationList,false,search.Text,selectedAnimation,Animation,Animations);
        search.TextChanged+=(_,_)=>RefreshList();
        if(selectedAnimation!=null&&!editor.Modes.ContainsKey(selectedAnimation))selectedAnimation=null;
        RefreshList();if(selectedAnimation!=null)Animation(selectedAnimation);else animationDetail.Content=Ui.Text("展开左侧分组，选择动画。",14,true);
    }
    private void Animation(string id)
    {
        selectedAnimation=id;
        if(animationDetail==null){Animations();return;}
        if(animationList!=null)Highlight(animationList,id);
        var p=new StackPanel();var heading=Ui.Caption("动画 · "+id,string.Join(" · ",editor.Membership(false,id)),true);heading.Margin=new Thickness(0,0,0,10);var moreHeader=Ui.Light("⋯",()=>{});var headerMenu=new ContextMenu();moreHeader.ContextMenu=headerMenu;moreHeader.Click+=(_,_)=>headerMenu.IsOpen=true;p.Children.Add(Ui.Pair(heading,moreHeader));animationDetail.Content=new ScrollViewer{Content=p,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
        void AddHeaderAction(string name,Action action){var item=new MenuItem{Header=name};item.Click+=(_,_)=>Safe(action);headerMenu.Items.Add(item);}
        AddHeaderAction("重命名",()=>{var name=AskName("重命名动画","动画名称",id);if(name==null||name==id)return;editor.RenameMode(id,name);selectedAnimation=name;Changed();Animations();});
        AddHeaderAction("修改所属分组",()=>{var groups=editor.Groups(false);var chosen=editor.Membership(false,id).ToHashSet();var d=EditorDialog("修改所属分组",440,460);var list=new StackPanel();foreach(var group in groups){var c=ResourceCheck(chosen.Contains(group));c.Click+=(_,_)=>{if(c.IsChecked==true)chosen.Add(group);else chosen.Remove(group);};list.Children.Add(Ui.Card(Ui.Pair(Ui.Caption(group),c)));}DialogBody(d,list,()=>d.DialogResult=true);if(d.ShowDialog()==true){editor.SetMembership(false,id,chosen);Changed();Animations();}});
        AddHeaderAction("删除动画",()=>{var impact=editor.DeleteImpact(id);var note=impact.Count==0?"删除动画并取消所有引用？":"删除后以下功能将失去唯一有效动画："+Environment.NewLine+string.Join(Environment.NewLine,impact.Select(FunctionTitle));if(MessageBox.Show(note,"删除",MessageBoxButton.YesNo)==MessageBoxResult.Yes){editor.DeleteMode(id);Changed();Animations();}});
        var properties=new StackPanel();var previewGrid=new Grid();previewGrid.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(190)});previewGrid.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});p.Children.Add(previewGrid);Grid.SetColumn(properties,1);previewGrid.Children.Add(properties);previewGrid.SizeChanged+=(_,_)=>{bool narrow=previewGrid.ActualWidth<560;if(previewGrid.RowDefinitions.Count==0){previewGrid.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});previewGrid.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});}previewGrid.ColumnDefinitions[0].Width=narrow?new GridLength(1,GridUnitType.Star):new GridLength(190);previewGrid.ColumnDefinitions[1].Width=narrow?new GridLength(0):new GridLength(1,GridUnitType.Star);Grid.SetColumn(properties,narrow?0:1);Grid.SetRow(properties,narrow?1:0);};

        var preview=new FramePreview();var previewNote=Ui.Text("预览忽略帧位移",12,true);var previewPanel=new StackPanel();previewPanel.Children.Add(preview);previewPanel.Children.Add(previewNote);previewGrid.Children.Add(Ui.Card(previewPanel));
        var progress=new ProgressBar{Height=5,Minimum=0,Maximum=1,Foreground=Ui.Brush("Accent"),Background=Ui.Brush("Line"),Margin=new Thickness(0,5,0,5)};previewPanel.Children.Add(progress);
        var timer=new System.Windows.Threading.DispatcherTimer{Interval=TimeSpan.FromMilliseconds(33)};
        AnimationCursor? cursor=null;WpfImageCache? cache=null;long last=0;int editorAnchor=128;Frame? displayedFrame=null;
        var replayDelay=new System.Windows.Threading.DispatcherTimer{Interval=TimeSpan.FromMilliseconds(180)};replayDelay.Tick+=(_,_)=>{replayDelay.Stop();Replay();};void ScheduleReplay(){replayDelay.Stop();replayDelay.Start();}
        void Replay(){try{var pack=editor.Preview();cursor=new AnimationCursor(pack,id,new Random());cache=new WpfImageCache(pack);editorAnchor=pack.AnchorRes;displayedFrame=null;preview.Show(cache,cursor.Current,editorAnchor,false);last=Environment.TickCount64;timer.Start();}catch(Exception e){timer.Stop();previewNote.Text=e.Message;}}
        timer.Tick+=(_,_)=>{try{if(cursor==null||cache==null)return;var now=Environment.TickCount64;cursor.Advance(Math.Clamp(now-last,0,100));last=now;if(displayedFrame!=cursor.Current){displayedFrame=cursor.Current;preview.Show(cache,cursor.Current,editorAnchor,false);}previewNote.Text=string.Join(" > ",cursor.Chain)+" · "+cursor.Mode.Id+" · "+(cursor.FrameIndex+1)+"/"+cursor.Mode.Frames.Count+(cursor.Ended?" · 已结束":" · 播放中");progress.Value=cursor.Ended?1:cursor.TargetDuration is long target?Math.Clamp((double)cursor.ModeElapsed/Math.Max(1,target),0,1):(cursor.FrameIndex+(double)cursor.FrameElapsed/Math.Max(1,cursor.FrameDuration))/cursor.Mode.Frames.Count;if(cursor.Ended)timer.Stop();}catch(Exception e){timer.Stop();previewNote.Text=e.Message;}};
        previewPanel.Children.Add(Ui.Light("↻ 重播",Replay));p.Loaded+=(_,_)=>Replay();p.Unloaded+=(_,_)=>{timer.Stop();replayDelay.Stop();};
        var m=editor.Mode(id);var bindings=Ui.Caption("绑定功能",BoundFunctions(id));properties.Children.Add(Ui.Card(Ui.Pair(bindings,Ui.Light("编辑 ›",()=>{if(EditFunctionBindings(id)){UpdateCaption(bindings,BoundFunctions(id));Replay();}}))));
        var loopKeys=new[]{"off","timed","infinite"};var loop=new ComboBox{ItemsSource=loopKeys.Select(LoopTitle).ToArray(),SelectedIndex=Math.Max(0,Array.IndexOf(loopKeys,m["loopMode"]?.ToString()??"off")),Width=160};loop.SelectionChanged+=(_,_)=>{if(loop.SelectedIndex<0)return;m["loopMode"]=loopKeys[loop.SelectedIndex];if(loopKeys[loop.SelectedIndex]=="timed"&&m["duration"]==null)m["duration"]=new JsonObject{["min"]=1000,["max"]=3000};Changed();};properties.Children.Add(Ui.Card(Ui.Pair(Ui.Caption("播放循环"),loop)));
        var durationPanel=new StackPanel();properties.Children.Add(durationPanel);
        void RefreshDuration(){durationPanel.Children.Clear();if(loop.SelectedIndex!=1)return;var duration=m["duration"] as JsonObject;var lo=ReadInt(duration?["min"],1000);var hi=Math.Max(lo,ReadInt(duration?["max"],3000));durationPanel.Children.Add(Ui.Card(Ui.Pair(Ui.Caption("播放时长"),new RangeSlider(1,Math.Max(60000,hi),lo,hi,(a,b)=>{m["duration"]=new JsonObject{["min"]=(int)a,["max"]=(int)b};Changed();ScheduleReplay();},"ms"),340)));var finish=Ui.Switch(m["finishCycle"]?.GetValue<bool>()??true);finish.Click+=(_,_)=>{m["finishCycle"]=finish.IsChecked==true;Changed();Replay();};durationPanel.Children.Add(Ui.Card(Ui.Pair(Ui.Caption("完成当前循环"),finish)));}
        RefreshDuration();loop.SelectionChanged+=(_,_)=>{RefreshDuration();Replay();};
        var nextLabel=Ui.Caption("后续动画",m["nextMode"]?.ToString()??"无");properties.Children.Add(Ui.Card(Ui.Pair(nextLabel,Ui.Light("选择 ›",()=>{var result=PickResources("动画",new[]{m["nextMode"]?.ToString()??""},false);if(result==null)return;if(result.Count==0)m.Remove("nextMode");else m["nextMode"]=result[0];((TextBlock)nextLabel.Children[1]).Text=m["nextMode"]?.ToString()??"无";Changed();Replay();}))));
        ThinkingBinding(properties,m,true);
        var frames=editor.Frames(id);var frameList=new StackPanel();var frameTitle=Ui.Text("",16);p.Children.Add(Ui.Pair(frameTitle,Ui.Button("＋ 新增帧",()=>{var result=PickResources("图片",Array.Empty<string>(),false);if(result==null||result.Count==0)return;frames.Add(new JsonObject{["res"]=result[0],["delay"]=100});Changed();RefreshFrames();Replay();},true)));p.Children.Add(frameList);
        void RefreshFrames(){frameTitle.Text="逐帧编辑 · "+frames.Count+" 帧";frameList.Children.Clear();for(int i=0;i<frames.Count;i++){int index=i;var frame=frames[i]!.AsObject();var res=frame["res"]?.ToString()??"";var (lo,hi)=FrameDelay(frame);var label=Ui.Caption("帧 "+(i+1)+" · "+(lo==hi?lo.ToString():lo+"–"+hi)+"ms"+FrameSummary(frame),res);var left=new StackPanel{Orientation=Orientation.Horizontal};var thumb=Thumbnail(editor,res,48);left.Children.Add(thumb);left.Children.Add(label);var actions=new StackPanel{Orientation=Orientation.Horizontal};actions.Children.Add(Ui.Light("编辑 ›",()=>{timer.Stop();var edited=EditFrame(frames,index);if(edited.Count>0){foreach(var item in edited){var n=frames.IndexOf(item);if(n>=0&&n<frameList.Children.Count){var existing=(Border)frameList.Children[n];if(existing.Tag is Action refresh)refresh();}}Replay();}else timer.Start();}));var more=Ui.Light("⋯",()=>{});var menu=new ContextMenu();void Move(int delta){int target=index+delta;if(target<0||target>=frames.Count)return;var item=frames[index]!;frames.RemoveAt(index);frames.Insert(target,item);Changed();RefreshFrames();Replay();}foreach(var (title,action) in new (string,Action)[]{("上移",()=>Move(-1)),("下移",()=>Move(1)),("删除帧",()=>{if(frames.Count>1&&MessageBox.Show("删除这一帧？","删除",MessageBoxButton.YesNo)==MessageBoxResult.Yes){frames.RemoveAt(index);Changed();RefreshFrames();Replay();}})}){var item=new MenuItem{Header=title};item.Click+=(_,_)=>action();menu.Items.Add(item);}more.ContextMenu=menu;more.Click+=(_,_)=>menu.IsOpen=true;actions.Children.Add(more);var card=Ui.Card(Ui.Pair(left,actions));card.Tag=(Action)(()=>{var (a,b)=FrameDelay(frame);((TextBlock)label.Children[0]).Text="帧 "+(index+1)+" · "+(a==b?a.ToString():a+"–"+b)+"ms"+FrameSummary(frame);UpdateCaption(label,frame["res"]?.ToString()??"");thumb.Source=Thumbnail(editor,frame["res"]?.ToString()??"",48).Source;});card.AllowDrop=true;card.Drop+=(_,e)=>{if(e.Data.GetData("CakePetFrame") is int from&&from>=0&&from<frames.Count&&from!=index){var item=frames[from]!;frames.RemoveAt(from);frames.Insert(index,item);Changed();RefreshFrames();Replay();}};var grip=Ui.Light("⋮⋮",()=>{});grip.PreviewMouseLeftButtonDown+=(_,e)=>{DragDrop.DoDragDrop(grip,new DataObject("CakePetFrame",index),DragDropEffects.Move);e.Handled=true;};left.Children.Insert(0,grip);frameList.Children.Add(card);}}
        RefreshFrames();
    }
    private void ChooseExternal(string type,Action<PackageEditor,string> accept)
    {
        var chosen=BrowseResources(type,Array.Empty<string>(),false,true);if(chosen==null||chosen.Count==0)return;Safe(()=>accept(chosen[0].Source,chosen[0].Id));
    }
    private readonly Dictionary<string,BitmapSource> thumbnailCache=new(StringComparer.OrdinalIgnoreCase);
    private Image Thumbnail(PackageEditor source,string res,int size)
    {
        var image=new Image{Width=size,Height=size,Stretch=Stretch.Uniform,Margin=new Thickness(0,0,8,0)};try{var path=source.ResourcePath(res);var key=path+"|"+File.GetLastWriteTimeUtc(path).Ticks;if(!thumbnailCache.TryGetValue(key,out var bitmap)){var b=new BitmapImage();b.BeginInit();b.CacheOption=BitmapCacheOption.OnLoad;b.DecodePixelWidth=128;b.UriSource=new Uri(path);b.EndInit();b.Freeze();bitmap=b;if(thumbnailCache.Count>=512)thumbnailCache.Clear();thumbnailCache[key]=bitmap;}image.Source=bitmap;}catch{}return image;
    }
    private void Images()
    {
        animationDetail=null;animationList=null;
        var layout=new DockPanel();var title=Ui.Caption("图片资源","按文件夹查看图片；已引用的图片需要先替换引用。",true);((TextBlock)title.Children[0]).FontSize=24;DockPanel.SetDock(title,Dock.Top);layout.Children.Add(title);
        var grid=new Grid{Margin=new Thickness(0,12,0,0)};grid.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(220),MinWidth=160});grid.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});layout.Children.Add(grid);
        var tree=new StackPanel{Margin=new Thickness(0,0,10,0)};grid.Children.Add(new ScrollViewer{Content=tree,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});var detail=new ContentControl();Grid.SetColumn(detail,1);grid.Children.Add(detail);AddResourceSplitter(grid);host.Content=layout;
        void Show(string folder)
        {
            selectedFolder=folder;var page=new StackPanel();var actions=new WrapPanel();
            actions.Children.Add(Ui.Button("导入图片",()=>Safe(()=>{var d=new OpenFileDialog{Filter="图片|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp",Multiselect=true};if(d.ShowDialog()!=true)return;foreach(var file in d.FileNames)if(WpfImageCache.IsReadable(file))editor.CopyImageFile(file,folder);Changed();Images();}),true));
            actions.Children.Add(Ui.Button("从其他素材包导入",()=>ChooseExternal("图片",(source,id)=>{editor.CopyImageFrom(source.Root,id,folder);Changed();Images();})));
            page.Children.Add(Ui.Pair(Ui.Caption(folder.Length==0?"未分组":folder,"图片资源",true),actions));
            page.Children.Add(Ui.Light("＋ 文件夹",()=>Safe(()=>{var name=AskName("新建文件夹","文件夹名称");if(name==null)return;selectedFolder=editor.NewFolder(folder,name);Changed();Images();})));
            foreach(var res in editor.Images().Where(x=>string.Equals(x.Contains('/')?x[..x.LastIndexOf('/') ]:"",folder,StringComparison.OrdinalIgnoreCase)))
            {
                var left=new StackPanel{Orientation=Orientation.Horizontal};left.Children.Add(Thumbnail(editor,res,64));var refs=editor.ImageReferences(res);left.Children.Add(Ui.Caption(Path.GetFileName(res),refs.Count+" 处引用",true));var buttons=new WrapPanel();
                buttons.Children.Add(Ui.Light("引用",()=>MessageBox.Show(refs.Count==0?"没有引用":string.Join(Environment.NewLine,refs),"图片引用")));
                buttons.Children.Add(Ui.Light("替换引用",()=>{var chosen=PickResources("图片",Array.Empty<string>(),false,folder,res);if(chosen==null||chosen.Count==0)return;editor.ReplaceImageReferences(res,chosen[0]);Changed();Images();}));
                var del=Ui.Light("删除",()=>Safe(()=>{if(editor.ImageReferenced(res))throw new InvalidDataException("图片仍被已保存或正在编辑的动画引用，请先替换并保存。");if(MessageBox.Show("永久删除图片？","删除",MessageBoxButton.YesNo)==MessageBoxResult.Yes){File.Delete(editor.ResourcePath(res));Images();}}));del.IsEnabled=refs.Count==0;buttons.Children.Add(del);page.Children.Add(Ui.Card(Ui.Pair(left,buttons)));
            }
            detail.Content=new ScrollViewer{Content=page,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
        }
        void AddFolders(StackPanel target,string parent){foreach(var name in editor.Folders(parent)){var path=parent.Length==0?name:parent+"/"+name;var head=new DockPanel();var select=Ui.Light("查看",()=>Show(path));select.ToolTip=path;head.Children.Add(select);var body=new StackPanel();var exp=ResourceFolder("image:"+path,name,"",body,head);LoadOnExpand(exp,()=>AddFolders(body,path));var menu=new ContextMenu();foreach(var (label,delta) in new[]{("上移",-1),("下移",1)}){var item=new MenuItem{Header=label};item.Click+=(_,_)=>{editor.MoveFolderOrder(parent,name,delta);Changed();Images();};menu.Items.Add(item);}exp.ContextMenu=menu;target.Children.Add(exp);}}
        tree.Children.Add(Ui.Nav("未分组",()=>Show("")));AddFolders(tree,"");if(!Directory.Exists(editor.ResourcePath(selectedFolder)))selectedFolder="";Show(selectedFolder);
    }
    private void Thinking()
    {
        animationDetail=null;animationList=null;
        var layout=new DockPanel();var heading=Ui.Caption("碎碎念文本","按分组选择文本，在右侧修改内容和所属分组。",true);((TextBlock)heading.Children[0]).FontSize=24;DockPanel.SetDock(heading,Dock.Top);layout.Children.Add(heading);
        var grid=new Grid{Margin=new Thickness(0,12,0,0)};grid.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(220),MinWidth=160});grid.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});layout.Children.Add(grid);
        var left=new DockPanel{Margin=new Thickness(0,0,12,0)};var search=new TextBox{ToolTip="搜索文本",Margin=new Thickness(0,0,0,6)};DockPanel.SetDock(search,Dock.Top);left.Children.Add(search);
        var toolbar=ResourceToolbar("碎碎念",()=>{var text=AskName("新建碎碎念","文本内容");if(text==null)return;selectedText="t"+Guid.NewGuid().ToString("N").Substring(0,8);editor.Thinkings[selectedText]=new JsonObject{["text"]=text};Changed();Thinking();},()=>CreateGroup(true,Thinking),()=>ChooseExternal("碎碎念",(source,id)=>{selectedText=editor.ImportText(source,id);Changed();Thinking();}));DockPanel.SetDock(toolbar,Dock.Top);left.Children.Add(toolbar);
        var list=new StackPanel();left.Children.Add(new ScrollViewer{Content=list,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});grid.Children.Add(left);
        var detail=new ContentControl();Grid.SetColumn(detail,1);grid.Children.Add(detail);AddResourceSplitter(grid);host.Content=layout;
        void Show(string id){selectedText=id;var node=editor.Thinkings[id]!.AsObject();var page=new StackPanel();page.Children.Add(Ui.Caption("碎碎念文本",id,true));Field(page,"文本",node["text"]?.ToString()??"",v=>node["text"]=v,true);MembershipEditor(page,true,id,Thinking);var refs=editor.ThinkingReferences(id).ToArray();var references=new SettingCard("引用位置",refs.Length+" 处引用",initiallyExpanded:false);foreach(var reference in refs){var check=new CheckBox{IsChecked=true,Style=(Style)Application.Current.FindResource("ResourceCheck")};var snapshot=reference.Node["thinking"]!["pool"]![id]!.DeepClone();check.Click+=(_,_)=>{var pool=reference.Node["thinking"]!["pool"]!.AsObject();if(check.IsChecked==true)pool[id]=snapshot.DeepClone();else pool.Remove(id);Changed();};references.Add(Ui.Pair(Ui.Caption(reference.Label),check));}page.Children.Add(references);page.Children.Add(Ui.Light("删除文本",()=>{if(MessageBox.Show("删除文本并取消引用？","删除",MessageBoxButton.YesNo)==MessageBoxResult.Yes){editor.DeleteThinking(id);Changed();Thinking();}}));detail.Content=new ScrollViewer{Content=page,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};}
        void Refresh()=>GroupList(list,true,search.Text,selectedText,Show,Thinking);
        if(selectedText!=null&&!editor.Thinkings.ContainsKey(selectedText))selectedText=null;
        search.TextChanged+=(_,_)=>Refresh();Refresh();if(selectedText!=null)Show(selectedText);else detail.Content=Ui.Text("展开左侧分组，选择碎碎念文本。",14,true);
    }
}
