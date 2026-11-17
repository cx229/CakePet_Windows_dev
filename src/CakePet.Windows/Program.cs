using System.IO;
using System.Diagnostics;
using System.Windows.Threading;
using System.Windows;
using System.Windows.Controls;
using CakePet.Core;
using Microsoft.Win32;
using Forms=System.Windows.Forms;
using System.Reflection;

namespace CakePet.Windows;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        var root=Path.Combine(AppContext.BaseDirectory,"assets","default");
        if(args.Length>=2&&args[0] is "--package" or "--validate-assets")root=Path.GetFullPath(args[1]);
        if(args.Length>0&&args[0]=="--validate-assets")
        {
            var pack=RolePackage.Load(root,WpfImageCache.IsReadable);
            if(pack.Warnings.Count>0)throw new InvalidDataException(string.Join(Environment.NewLine,pack.Warnings));
            var cache=new WpfImageCache(pack);
            foreach(var mode in pack.Modes.Values)foreach(var frame in mode.Frames)
                if(cache.Get(frame).PixelWidth<=0)throw new InvalidDataException(frame.Resource);
            foreach(var id in pack.Candidates("feed/food"))
            {var visual=cache.GetFoodVisual(id);if(visual.Frames.Count==0||visual.Width<=0||visual.Height<=0)throw new InvalidDataException(id);}
            return 0;
        }
        using var mutex=new System.Threading.Mutex(true,"Local\\CakePet.Windows",out bool first);
        if(!first){try{using var signal=System.Threading.EventWaitHandle.OpenExisting("Local\\CakePet.ShowLauncher");signal.Set();}catch(System.Threading.WaitHandleCannotBeOpenedException){}return 0;}
        using var activate=new System.Threading.EventWaitHandle(false,System.Threading.EventResetMode.AutoReset,"Local\\CakePet.ShowLauncher");
        var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};
        app.Resources.MergedDictionaries.Add(new ResourceDictionary{Source=new Uri("/CakePet;component/Themes/Modern.xaml",UriKind.Relative)});
        var launcher=new Launcher(root);app.MainWindow=launcher;
        using var registration=new ActivationRegistration(activate,launcher);
        launcher.StartResident();return app.Run();
    }
}

internal sealed class ActivationRegistration:IDisposable
{
    private readonly System.Threading.RegisteredWaitHandle wait;
    public ActivationRegistration(System.Threading.EventWaitHandle signal,Launcher launcher){wait=System.Threading.ThreadPool.RegisterWaitForSingleObject(signal,(_,_)=>launcher.Dispatcher.BeginInvoke(new Action(launcher.ShowLauncher)),null,-1,false);}
    public void Dispose()=>wait.Unregister(null);
}

internal sealed class Launcher : Window
{
    private readonly string defaultRoot;
    private readonly CatalogStorage storage;
    private RoleCatalog catalog=>storage.Catalog;
    private readonly WrapPanel roleCards=new();
    private readonly ContentControl navigationHost=new();
    private FrameworkElement? homeShell;
    private readonly List<(Image Image,AnimationCursor Cursor,WpfImageCache Cache)> previews=new();
    private readonly DispatcherTimer saveTimer=new(){Interval=TimeSpan.FromMilliseconds(400)};
    private bool dirty;
    private void QueueSave(){dirty=true;saveTimer.Stop();saveTimer.Start();}
    private void FlushSave(){saveTimer.Stop();if(dirty){storage.Save();dirty=false;}}
    private readonly StackPanel packCards=new();
    private readonly List<PetWindow> pets=new();
    private readonly Dictionary<string,(RolePackage Pack,WpfImageCache Images)> packages=new(StringComparer.OrdinalIgnoreCase);
    private readonly ThinkingCoordinator thoughts=new();
    private readonly ThinkingPresenter thinkingPresenter=new();
    private readonly TypingThinking typing=new();
    private readonly TypingInput typingInput;
    private long lastTypingSave;
    private bool typingDirty;
    private string Today()=>DateTime.Now.ToString("yyyy-MM-dd");
    private void SaveTyping(){if(!typingDirty)return;catalog.TypingDay=typing.Day;catalog.TypingToday=typing.Today;storage.Save();typingDirty=false;lastTypingSave=clock.ElapsedMilliseconds;}
    private void OnTyping(){if(!catalog.Typing.Enabled||catalog.ThinkingPolicy==ThinkingDisplayPolicy.None)return;typing.Press(clock.ElapsedMilliseconds,Today(),catalog.Typing);typingDirty=true;}
    private void ReloadTyping(){typing.Restore(catalog.TypingDay,catalog.TypingToday,Today());typingDirty=false;try{typingInput.SetEnabled(catalog.Typing.Enabled&&catalog.ThinkingPolicy!=ThinkingDisplayPolicy.None);}catch(Exception e){catalog.Typing=catalog.Typing with{Enabled=false};storage.Save();MessageBox.Show(e.Message,"无法启用打字统计");}}
    private void ChangeTyping(Func<TypingSettings,TypingSettings> patch){var next=patch(catalog.Typing).Normalize();if(next.Enabled!=catalog.Typing.Enabled){typingInput.SetEnabled(next.Enabled&&catalog.ThinkingPolicy!=ThinkingDisplayPolicy.None);if(!next.Enabled){typing.Stop();SaveTyping();}}catalog.Typing=next;QueueSave();}
    private readonly FoodWorld world=new();
    private readonly RandomSchedule randomSchedule=new();
    private long lastRandomCheck;
    private readonly Dictionary<FoodInstance,FoodWindow> foodWindows=new();
    private readonly DispatcherTimer timer=new(DispatcherPriority.Render){Interval=TimeSpan.FromMilliseconds(16)};
    private readonly Stopwatch clock=Stopwatch.StartNew();
    private long lastTick;
    private bool shuttingDown,exitRequested;
    private readonly Forms.NotifyIcon tray;
    private readonly System.Drawing.Icon trayIcon;
    private bool started;
    private string Version=>typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion??"dev";
    private readonly TextBlock status=new(){TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,12,0,0)};
    public Launcher(string root)
    {
        defaultRoot=root;storage=new CatalogStorage(root);typingInput=new TypingInput(OnTyping);thoughts.Add(typing);Title="CakePet · "+Version;Width=1080;Height=740;MinWidth=800;MinHeight=500;Background=Ui.Brush("Canvas");FontFamily=new System.Windows.Media.FontFamily("Microsoft YaHei UI, Segoe UI");
        WindowStartupLocation=WindowStartupLocation.CenterScreen;
        var shell=new Grid();shell.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(190)});shell.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});homeShell=shell;Content=navigationHost;navigationHost.Content=shell;
        var nav=new StackPanel{Margin=new Thickness(12,20,12,12)};shell.Children.Add(new Border{Background=Ui.Brush("Sidebar"),Child=nav});var logo=Ui.Text("CakePet",26);logo.FontWeight=FontWeights.SemiBold;logo.Margin=new Thickness(6,0,0,22);nav.Children.Add(logo);
        var pages=new ContentControl{Margin=new Thickness(20,18,20,18)};Grid.SetColumn(pages,1);shell.Children.Add(pages);
        var navItems=new List<Button>();void Navigate(FrameworkElement page,int index){pages.Content=page;for(int i=0;i<navItems.Count;i++)navItems[i].Background=Ui.Brush(i==index?"AccentSoft":"Sidebar");}
        var roles=new DockPanel{Margin=new Thickness(12)};
        var homeActions=new StackPanel{Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center};
        homeActions.Children.Add(Ui.Button("＋ 新建角色",NewRole,true));var all=Ui.Light("⋯",()=>{});all.Width=32;all.ToolTip="全部角色操作";var allMenu=new ContextMenu();
        void AllAction(string name,Action action){var item=new MenuItem{Header=name};item.Click+=(_,_)=>Safe(action);allMenu.Items.Add(item);}
        AllAction("召唤全部",()=>{foreach(var role in catalog.Roles.ToArray())Summon(role);});AllAction("召回全部",RecallAll);AllAction("恢复全部点击",RestoreInput);all.ContextMenu=allMenu;all.Click+=(_,_)=>allMenu.IsOpen=true;homeActions.Children.Add(all);
        var homeTitle=new StackPanel();homeTitle.Children.Add(Ui.Text("我的角色",26));status.Margin=new Thickness(0,5,0,0);homeTitle.Children.Add(status);
        var homeHeading=Ui.Pair(homeTitle,homeActions);homeHeading.Margin=new Thickness(0,0,0,16);DockPanel.SetDock(homeHeading,Dock.Top);roles.Children.Add(homeHeading);roles.Children.Add(new ScrollViewer{Content=roleCards,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});
        var materials=new DockPanel{Margin=new Thickness(12)};var importRow=new StackPanel{Orientation=Orientation.Horizontal};
        var import=Ui.Button("导入 ▾",()=>{});var importMenu=new ContextMenu();var zipItem=new MenuItem{Header="导入 ZIP"};zipItem.Click+=(_,_)=>ImportArchive();importMenu.Items.Add(zipItem);var dirItem=new MenuItem{Header="导入目录"};dirItem.Click+=(_,_)=>ImportPack();importMenu.Items.Add(dirItem);import.ContextMenu=importMenu;import.Click+=(_,_)=>importMenu.IsOpen=true;importRow.Children.Add(import);
        importRow.Children.Add(Ui.Button("＋ 新建",()=>Safe(()=>{var pack=storage.Import(defaultRoot);RefreshCards();EditPack(pack);}),true));
        var materialsTitle=Ui.Pair(Ui.Caption("素材包管理","管理角色使用的素材包",true),importRow);((TextBlock)((StackPanel)materialsTitle.Children[0]).Children[0]).FontSize=26;materialsTitle.Margin=new Thickness(0,0,0,16);DockPanel.SetDock(materialsTitle,Dock.Top);materials.Children.Add(materialsTitle);materials.Children.Add(new ScrollViewer{Content=packCards,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});
        var global=new StackPanel{Margin=new Thickness(12),MaxWidth=900};global.Children.Add(Ui.Text("全局设置",26));
        void GlobalGroup(string name){var t=Ui.Text(name,12,true);t.Margin=new Thickness(2,14,0,7);global.Children.Add(t);}
        GlobalGroup("个性化");
        global.Children.Add(Ui.Card(Ui.Pair(Ui.Caption("用户习惯","新角色的设置与默认值"),Ui.Light("进入 ›",EditDefaults))));
        GlobalGroup("碎碎念");
        var thinkingCard=new SettingCard("碎碎念","控制所有角色和打字统计的显示方式。");
        var policies=new ComboBox{ItemsSource=new[]{"无","仅显示最新","重叠"},SelectedIndex=(int)catalog.ThinkingPolicy,Width=175};
        var policyNote=Ui.Caption("显示策略","新的碎碎念会隐藏此前显示的消息。");
        var locations=new ComboBox{ItemsSource=new[]{"主屏幕底部","托盘左侧","任务栏左侧"},SelectedIndex=(int)catalog.ThinkingLocation,Width=175};
        var typingLink=Ui.Light("设置 ›",EditTyping);
        var typingCaption=Ui.Caption("打字统计","自定义按键计数消息及其显示样式。");
        void UpdateThinkingUi()
        {
            bool enabled=catalog.ThinkingPolicy!=ThinkingDisplayPolicy.None;
            ((TextBlock)policyNote.Children[1]).Text=catalog.ThinkingPolicy switch{ThinkingDisplayPolicy.None=>"关闭所有碎碎念，包括打字统计。",ThinkingDisplayPolicy.Overlap=>"同一位置重叠，新的消息显示在上方。",_=>"新的碎碎念会直接隐藏此前显示的消息。"};
            locations.IsEnabled=enabled;
            ((TextBlock)typingCaption.Children[0]).Foreground=enabled?Ui.Brush("Ink"):System.Windows.Media.Brushes.IndianRed;
            ((TextBlock)typingCaption.Children[1]).Text=enabled?"自定义按键计数消息及其显示样式。":"全局设置 > 碎碎念已关闭";
            ((TextBlock)typingCaption.Children[1]).Foreground=enabled?Ui.Brush("Muted"):System.Windows.Media.Brushes.IndianRed;
        }
        policies.IsVisibleChanged+=(_,_)=>{policies.SelectedIndex=(int)catalog.ThinkingPolicy;UpdateThinkingUi();};
        policies.SelectionChanged+=(_,_)=>{if(policies.SelectedIndex<0)return;Safe(()=>{ChangeThinkingPolicy((ThinkingDisplayPolicy)policies.SelectedIndex);UpdateThinkingUi();});};
        locations.IsVisibleChanged+=(_,_)=>locations.SelectedIndex=(int)catalog.ThinkingLocation;
        locations.SelectionChanged+=(_,_)=>{if(locations.SelectedIndex<0)return;catalog.ThinkingLocation=(ThinkingLocation)locations.SelectedIndex;QueueSave();};
        thinkingCard.Add(Ui.Pair(policyNote,policies));thinkingCard.Add(Ui.Pair(Ui.Caption("显示位置","所有碎碎念共用的显示区域。"),locations));thinkingCard.Add(Ui.Pair(typingCaption,typingLink));global.Children.Add(thinkingCard);
        thoughts.Policy=catalog.ThinkingPolicy;UpdateThinkingUi();
        GlobalGroup("后台运行");
        var launch=Ui.Switch(catalog.AutoSummon);launch.IsVisibleChanged+=(_,_)=>launch.IsChecked=catalog.AutoSummon;
        launch.Click+=(_,_)=>{catalog.AutoSummon=launch.IsChecked==true;storage.Save();};global.Children.Add(Ui.Card(Ui.Pair(Ui.Caption("启动时召唤角色","软件启动后自动召唤第一个角色"),launch)));
        GlobalGroup("数据与备份");
        var backup=new SettingCard("备份与恢复","导出全部持久化数据；导入时只覆盖支持的内容。");backup.Add(Ui.Pair(Ui.Caption("导出软件备份","将软件设置与数据保存为备份文件"),Ui.Button("导出",ExportBackup)));backup.Add(Ui.Pair(Ui.Caption("导入软件备份","从备份文件恢复软件设置与数据"),Ui.Button("导入",ImportArchive)));backup.Add(Ui.Pair(Ui.Caption("重置软件","恢复首次安装状态"),Ui.Button("重置",ResetSoftware)));global.Children.Add(backup);
        GlobalGroup("系统与调试");var cleanup=new SettingCard("清理桌面","不修改角色设置。");cleanup.Add(Ui.Pair(Ui.Caption("清除全部食物"),Ui.Button("清除",()=>world.Clear())));cleanup.Add(Ui.Pair(Ui.Caption("清除全部定格"),Ui.Button("清除",()=>{foreach(var pet in pets)pet.ClearFreezes();})));global.Children.Add(cleanup);
        foreach(var item in new[]{("首页",(FrameworkElement)roles),("素材包管理",materials),("全局设置",new ScrollViewer{Content=global,VerticalScrollBarVisibility=ScrollBarVisibility.Auto})}){int i=navItems.Count;var page=item.Item2;var b=Ui.Nav(item.Item1,()=>Navigate(page,i));navItems.Add(b);nav.Children.Add(b);}
        nav.Children.Add(Ui.Text(Version,11,true));Navigate(roles,0);RefreshCards();
        saveTimer.Tick+=(_,_)=>Safe(FlushSave);
        var trayMenu=new Forms.ContextMenuStrip();
        void TrayItem(string title,Action action)=>trayMenu.Items.Add(title,null,(_,_)=>Dispatcher.Invoke(action));
        TrayItem("打开 CakePet",ShowLauncher);TrayItem("召唤全部",()=>{foreach(var r in catalog.Roles.ToArray())Summon(r);});
        TrayItem("全部归位",()=>{foreach(var pet in pets)pet.Engine.ReturnHome();});
        TrayItem("召回全部",()=>{foreach(var pet in pets.ToArray())pet.Close();});
        TrayItem("恢复全部点击",RestoreInput);
        TrayItem("清除全部定格",()=>{foreach(var pet in pets)pet.ClearFreezes();});
        trayMenu.Items.Add(new Forms.ToolStripSeparator());TrayItem("退出",()=>{exitRequested=true;Close();});
        try{trayIcon=TrayIcon.Load();}catch(System.IO.IOException){trayIcon=(System.Drawing.Icon)System.Drawing.SystemIcons.Application.Clone();}catch(ArgumentException){trayIcon=(System.Drawing.Icon)System.Drawing.SystemIcons.Application.Clone();}
        var iconPath=Path.Combine(AppContext.BaseDirectory,"assets","CakePet.ico");if(File.Exists(iconPath)){using var stream=File.OpenRead(iconPath);Icon=System.Windows.Media.Imaging.BitmapFrame.Create(stream,System.Windows.Media.Imaging.BitmapCreateOptions.None,System.Windows.Media.Imaging.BitmapCacheOption.OnLoad);}
        tray=new Forms.NotifyIcon{Icon=trayIcon,Text="CakePet "+Version,ContextMenuStrip=trayMenu,Visible=true};
        tray.MouseClick+=(_,e)=>{if(e.Button==Forms.MouseButtons.Left)Dispatcher.Invoke(ShowLauncher);};
        StateChanged+=(_,_)=>{if(WindowState==WindowState.Minimized)Hide();};
        System.Windows.Application.Current.SessionEnding+=(_,_)=>{exitRequested=true;Close();};
        world.Spawned+=OnFoodSpawned;world.Removed+=OnFoodRemoved;
        timer.Tick+=(_,_)=>Tick();
        
        Closing+=(_,e)=>{if(!exitRequested){e.Cancel=true;Safe(FlushSave);Hide();return;}shuttingDown=true;Safe(FlushSave);Safe(SaveTyping);typingInput.Dispose();timer.Stop();world.Clear();foreach(var pet in pets.ToArray())pet.Close();tray.Visible=false;tray.Dispose();trayIcon.Dispose();trayMenu.Dispose();thinkingPresenter.Dispose();Dispatcher.BeginInvoke(new Action(()=>Application.Current.Shutdown()));};
    }
    internal void StartResident(){if(started)return;started=true;ReloadTyping();if(catalog.AutoSummon&&catalog.Roles.FirstOrDefault() is RoleEntry r)Summon(r);else Refresh();lastTick=clock.ElapsedMilliseconds;timer.Start();}
    internal void ShowLauncher(){Show();WindowState=WindowState.Normal;Activate();}
    private void Refresh()=>status.Text=$"已召唤 {pets.Count} 个角色 · {world.Foods.Count} 条食物。右键角色可投食；拖动食物至角色松手。";
    private void OnFoodSpawned(FoodInstance food)
    {
        var owner=pets.FirstOrDefault(p=>p.Engine==food.Owner);if(owner==null){world.Remove(food);return;}
        try
        {
            var window=new FoodWindow(world,food,owner.Images.GetFoodVisual(food.StartMode));
            foodWindows.Add(food,window);window.Show();Refresh();
        }
        catch(Exception e){world.Remove(food);if(!shuttingDown)MessageBox.Show(this,e.Message,"无法显示食物");}
    }
    private void OnFoodRemoved(FoodInstance food)
    {if(foodWindows.Remove(food,out var window))window.CloseFromWorld();Refresh();}
    private void Tick()
    {
        var now=clock.ElapsedMilliseconds;var dt=Math.Clamp(now-lastTick,0,100);lastTick=now;
        if(now-lastRandomCheck>=1000){lastRandomCheck=now;foreach(var entry in catalog.Roles.Where(r=>pets.Any(p=>p.RoleId==r.Id)).ToArray())if(randomSchedule.Due(entry.Id,entry.Settings.RandomMode,now)){catalog.Update(entry with{Settings=entry.Settings.RandomMode.Apply(entry.Settings,Random.Shared)});ApplyRunning(catalog.Roles.Single(r=>r.Id==entry.Id));QueueSave();}}
        world.Advance(dt);
        foreach(var pet in pets.ToArray())pet.Step(dt);
        world.UpdateInteractions();
        foreach(var pet in pets.ToArray())pet.Render();
        foreach(var food in foodWindows.Values.ToArray())food.Render();
        if(catalog.Typing.Enabled&&catalog.ThinkingPolicy!=ThinkingDisplayPolicy.None){if(typing.RollDay(Today()))typingDirty=true;typing.Advance(now);if(typingDirty&&now-lastTypingSave>=5000)Safe(SaveTyping);}
        var messages=pets.Where(p=>!string.IsNullOrWhiteSpace(p.VisibleThought)).Select(p=>new ThoughtMessage(p.RoleId+":"+p.VisibleThought,catalog.Roles.FirstOrDefault(r=>r.Id==p.RoleId)?.Name??p.Engine.Role.Package.Name,p.VisibleThought!,p.ThoughtStyle,thoughts.Order(p.Engine.Role.Thoughts))).GroupBy(m=>m.Key).Select(g=>g.OrderByDescending(m=>m.Order).First()).ToList();
        if(!typing.Suppressed&&typing.CurrentText is string typed)messages.Add(new ThoughtMessage("typing","打字统计",typed,catalog.Typing.Style,thoughts.Order(typing)));
        thinkingPresenter.Render(messages,catalog.ThinkingLocation,catalog.ThinkingPolicy);
        if(IsVisible&&navigationHost.Content==homeShell&&roleCards.IsVisible)foreach(var preview in previews){preview.Cursor.Advance(dt);var bitmap=preview.Cache.Get(preview.Cursor.Current);if(preview.Image.Source!=bitmap)preview.Image.Source=bitmap;}
    }
    private static Button Button(string text,Action action)=>Ui.Button(text,action);
    private (RolePackage Pack,WpfImageCache Images) GetPack(Guid id)
    {
        var entry=catalog.Packs.Single(p=>p.Id==id);var root=storage.Resolve(entry);
        if(!packages.TryGetValue(root,out var pair)){var pack=RolePackage.Load(root,WpfImageCache.IsReadable);pair=(pack,new WpfImageCache(pack));packages[root]=pair;}return pair;
    }
    private void Safe(Action action){try{action();}catch(Exception e){MessageBox.Show(this,e.Message,"CakePet");}}
    private void RestoreInput()=>Safe(()=>{foreach(var r in catalog.Roles.ToArray())catalog.Update(r with{Settings=r.Settings with{ClickThrough=false}});storage.Save();foreach(var r in catalog.Roles)ApplyRunning(r);RefreshCards();});
    private void BackHome(){Safe(FlushSave);navigationHost.Content=homeShell;RefreshCards();}
    private void ChangeThinkingPolicy(ThinkingDisplayPolicy value)
    {
        if(value==catalog.ThinkingPolicy)return;
        if(value==ThinkingDisplayPolicy.None){typingInput.SetEnabled(false);SaveTyping();typing.Stop();}
        catalog.ThinkingPolicy=value;thoughts.Policy=value;
        if(value==ThinkingDisplayPolicy.None)thinkingPresenter.Clear();
        foreach(var role in catalog.Roles)ApplyRunning(role);
        if(value!=ThinkingDisplayPolicy.None)typingInput.SetEnabled(catalog.Typing.Enabled);
        QueueSave();
    }
    private void EditTyping()=>Safe(()=>{
        var page=new StackPanel{MaxWidth=850,Margin=new Thickness(22)};page.Children.Add(Ui.Light("‹ 返回",BackHome));var heading=Ui.Caption("打字统计","只统计按键次数，不记录按键内容；关闭后停止监听。",true);heading.Margin=new Thickness(0,16,0,16);page.Children.Add(heading);
        var deps=new List<Action>();void Set(Func<TypingSettings,TypingSettings> patch)=>Safe(()=>{ChangeTyping(patch);foreach(var d in deps)d();});var s=catalog.Typing;bool globalEnabled=catalog.ThinkingPolicy!=ThinkingDisplayPolicy.None;
        var main=new SettingCard("打字统计总开关","每次按键触发消息并刷新显示时长。",globalEnabled&&s.Enabled,v=>Set(x=>x with{Enabled=v}),available:globalEnabled,collapsible:false);if(!globalEnabled)main.SetWarning("全局设置 > 碎碎念已关闭");page.Children.Add(main);
        var options=new SettingCard("统计与播放","最近一分钟的按键次数与当天累计值。");page.Children.Add(options);void Refresh()=>options.SetAvailable(globalEnabled&&catalog.Typing.Enabled);deps.Add(Refresh);Refresh();
        options.Add(Ui.NumberRow("显示时长","最后一次按键后保留的时间。",s.Seconds,1,30,1,"秒",v=>Set(x=>x with{Seconds=(int)v})));
        void Flag(string title,string note,bool value,Func<TypingSettings,bool,TypingSettings> patch){var sw=Ui.Switch(value);sw.Click+=(_,_)=>Set(x=>patch(x,sw.IsChecked==true));options.Add(Ui.Pair(Ui.Caption(title,note),sw));}
        Flag("按键速度","最近一分钟内的按键次数。",s.ShowSpeed,(x,v)=>x with{ShowSpeed=v});Flag("今日累计","当天累计的按键次数。",s.ShowToday,(x,v)=>x with{ShowToday=v});
        ThinkingStyleSettings.Add(page,()=>catalog.Typing.Style,patch=>Set(x=>x with{Style=patch(x.Style)}),"打字：126 次/分钟　今日：8,642 次",()=>globalEnabled&&catalog.Typing.Enabled,deps);
        navigationHost.Content=new ScrollViewer{Content=page,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
    });
    private void EditDefaults()=>EditDefaultsAt(0);
    private void EditDefaultsAt(int section)=>Safe(()=>{
        navigationHost.Content=new RoleSettingsPage(()=>new RoleEntry(Guid.Empty,Guid.Empty,"新角色",catalog.Defaults),null,patch=>{catalog.Defaults=SettingLimits.Clamp(patch(new RoleEntry(Guid.Empty,Guid.Empty,"新角色",catalog.Defaults)).Settings.Normalize());QueueSave();},BackHome,true,true,ApplyHabits,ExportHabits,ImportHabits,ResetHabits,section,EditDefaultsAt);
    });
    private void ExportHabits()=>Safe(()=>{FlushSave();var path=ChooseSave("CakePet-user-habits.json","JSON 文件|*.json");if(path!=null)ArchiveIO.Write(path,new System.Text.Json.Nodes.JsonObject{["type"]="cakepet-user-habits",["platform"]="windows",["settings"]=System.Text.Json.JsonSerializer.SerializeToNode(catalog.Defaults,ArchiveIO.Options)}.ToJsonString(ArchiveIO.Options));});
    private void ImportHabits()=>Safe(()=>{var d=new OpenFileDialog{Filter="用户习惯|*.json"};if(d.ShowDialog(this)!=true)return;var doc=ArchiveIO.Read(d.FileName);if(doc["type"]?.ToString()!="cakepet-user-habits"||doc["platform"]?.ToString()!="windows"||doc["settings"] is not System.Text.Json.Nodes.JsonObject fields)throw new InvalidDataException("不支持的用户习惯文件。");if(MessageBox.Show(this,"导入后只影响以后新建的角色，已有角色不受影响。","导入用户习惯",MessageBoxButton.YesNo)!=MessageBoxResult.Yes)return;catalog.Defaults=SettingLimits.Clamp(ArchiveIO.Overlay(catalog.Defaults,fields));storage.Save();EditDefaults();});
    private void ResetHabits()=>Safe(()=>{if(MessageBox.Show(this,"恢复软件自带的用户习惯？已有角色不受影响。","恢复默认",MessageBoxButton.YesNo)!=MessageBoxResult.Yes)return;catalog.Defaults=new RoleSettings();storage.Save();EditDefaults();});
    private void ApplyHabits(string group)=>Safe(()=>{
        if(MessageBox.Show(this,group=="全部"?"将全部用户习惯应用到已有角色？":"将「"+(group.StartsWith("item:")?SettingLimits.Specs.First(x=>x.Key==group[5..]).Label:group)+"」的默认设置和阈值应用到所有已有角色？","应用用户习惯",MessageBoxButton.YesNo)!=MessageBoxResult.Yes)return;
        foreach(var role in catalog.Roles.ToArray()){var next=group=="全部"?catalog.Defaults:group.StartsWith("item:")?SettingLimits.ApplyItem(role.Settings,catalog.Defaults,group[5..]):SettingLimits.ApplyGroup(role.Settings,catalog.Defaults,group);catalog.Update(role with{Settings=next});var updated=catalog.Roles.Single(r=>r.Id==role.Id);randomSchedule.Forget(role.Id);if(pets.Any(p=>p.RoleId==role.Id)){if(pets.Count(p=>p.RoleId==role.Id)!=updated.Settings.Count)Summon(updated);else ApplyRunning(updated);}}
        FlushSave();storage.Save();RefreshCards();
    });
    private void ApplyRunning(RoleEntry entry){var group=pets.Where(p=>p.RoleId==entry.Id).ToArray();for(int i=0;i<group.Length;i++)group[i].ApplySettings(entry.Settings,i==0,catalog.ThinkingPolicy!=ThinkingDisplayPolicy.None);}
    private void ChangeRole(Guid id,Func<RoleSettings,RoleSettings> change)=>Safe(()=>{var r=catalog.Roles.Single(x=>x.Id==id);catalog.Update(r with{Settings=change(r.Settings)});storage.Save();ApplyRunning(catalog.Roles.Single(x=>x.Id==id));RefreshCards();});
    private void RecallAll(){foreach(var pet in pets.ToArray())pet.Close();}
    private void Recall(Guid id){randomSchedule.Forget(id);foreach(var pet in pets.Where(p=>p.RoleId==id).ToArray())pet.Close();}
    private void ImportPack()=>Safe(()=>{
        var dialog=new OpenFolderDialog{Title="选择包含 modes.jsonc 的目录"};if(dialog.ShowDialog(this)!=true)return;
        storage.Import(dialog.FolderName);RefreshCards();
    });
    private string? ChooseSave(string filename,string filter){var d=new SaveFileDialog{FileName=filename,Filter=filter};return d.ShowDialog(this)==true?d.FileName:null;}
    private void ExportBackup()=>Safe(()=>{FlushSave();SaveTyping();var path=ChooseSave("CakePet-backup-"+DateTime.Now.ToString("yyyyMMdd-HHmm")+".zip","ZIP 文件|*.zip");if(path!=null)storage.ExportBackup(path,Version);});
    private void ImportArchive()=>Safe(()=>{
        var d=new OpenFileDialog{Filter="CakePet ZIP|*.zip"};if(d.ShowDialog(this)!=true)return;
        if(MessageBox.Show(this,"导入 ZIP。若为软件备份，将选择性覆盖可兼容的数据，其他数据保留。","导入",MessageBoxButton.OKCancel)!=MessageBoxResult.OK)return;
        FlushSave();SaveTyping();var active=pets.Select(x=>x.RoleId).Distinct().ToArray();RecallAll();packages.Clear();
        try{var result=storage.ImportZip(d.FileName);ReloadTyping();thoughts.Policy=catalog.ThinkingPolicy;BackHome();MessageBox.Show(this,result,"导入完成");}
        finally{foreach(var id in active)if(catalog.Roles.FirstOrDefault(x=>x.Id==id) is RoleEntry r)Summon(r);}
    });
    private void ResetSoftware()=>Safe(()=>{if(MessageBox.Show(this,"清空角色、素材包和设置，恢复首次安装状态？请先导出备份。","重置软件",MessageBoxButton.YesNo)!=MessageBoxResult.Yes)return;FlushSave();RecallAll();packages.Clear();storage.Reset(defaultRoot);ReloadTyping();thoughts.Policy=catalog.ThinkingPolicy;BackHome();});
    private void ExportRole(RoleEntry role)=>Safe(()=>{var path=ChooseSave("CakePet-role-settings.json","JSON 文件|*.json");if(path!=null)storage.ExportRole(catalog.Roles.Single(x=>x.Id==role.Id),path);});
    private void ImportRole(RoleEntry role)=>Safe(()=>{var d=new OpenFileDialog{Filter="角色设置|*.json"};if(d.ShowDialog(this)!=true)return;var next=storage.ImportRole(catalog.Roles.Single(x=>x.Id==role.Id),d.FileName);catalog.Update(next);storage.Save();if(pets.Any(p=>p.RoleId==role.Id))Summon(next);RefreshCards();});
    private void EditPack(PackEntry p)=>Safe(()=>{navigationHost.Content=new PackageEditorPage(storage.Resolve(p),()=>{var ids=catalog.Roles.Where(x=>x.PackId==p.Id&&pets.Any(w=>w.RoleId==x.Id)).Select(x=>x.Id).ToArray();foreach(var id in ids)Recall(id);packages.Remove(storage.Resolve(p));var parsed=GetPack(p.Id).Pack;var index=catalog.Packs.FindIndex(x=>x.Id==p.Id);catalog.Packs[index]=p with{Name=parsed.Name};storage.Save();RefreshCards();foreach(var id in ids)Summon(catalog.Roles.Single(x=>x.Id==id));},BackHome,catalog.Packs.Where(x=>x.Id!=p.Id).Select(x=>(x.Name,storage.Resolve(x))).ToArray());});
    private void ExportPack(PackEntry p)=>Safe(()=>{var pack=GetPack(p.Id).Pack;var name=string.Concat(pack.Name.Select(c=>Path.GetInvalidFileNameChars().Contains(c)?'_':c));var path=ChooseSave(name+"-v"+pack.Version+".zip","ZIP 文件|*.zip");if(path!=null)storage.ExportPack(p,path);});
    private void NewRole()=>Safe(()=>{
        if(catalog.Packs.Count==0){ImportPack();if(catalog.Packs.Count==0)return;}
        var panel=new StackPanel{Margin=new Thickness(24),MaxWidth=700};panel.Children.Add(Ui.Button("‹ 返回",BackHome));panel.Children.Add(Ui.Text("选择素材包",24));
        foreach(var pack in catalog.Packs){var choice=Ui.Button(pack.Name,()=>{var role=catalog.AddRole(pack.Id,pack.Name);storage.Save();RefreshCards();Edit(role);});panel.Children.Add(Ui.Card(choice));}
        navigationHost.Content=new ScrollViewer{Content=panel};
    });
    private void Edit(RoleEntry entry)=>EditAt(entry,0);
    private void EditAt(RoleEntry entry,int section)=>Safe(()=>{
        ShowLauncher();
        navigationHost.Content=new RoleSettingsPage(()=>catalog.Roles.Single(x=>x.Id==entry.Id),GetPack(entry.PackId).Pack,patch=>Safe(()=>{
            var old=catalog.Roles.Single(x=>x.Id==entry.Id);var next=patch(old);if(next==old)return;if(!old.Settings.RandomMode.Enabled&&next.Settings.RandomMode.Enabled)next=next with{Settings=next.Settings.RandomMode.Apply(next.Settings,Random.Shared)};if(old.Settings.RandomMode!=next.Settings.RandomMode)randomSchedule.Forget(entry.Id);catalog.Update(next);QueueSave();var latest=catalog.Roles.Single(x=>x.Id==entry.Id);
            var live=pets.Count(p=>p.RoleId==entry.Id);if(live>0&&live!=latest.Settings.Count)Summon(latest);else if(old.Settings!=latest.Settings)ApplyRunning(latest);
        }),BackHome,false,catalog.ThinkingPolicy!=ThinkingDisplayPolicy.None,initialSection:section,reopen:i=>EditAt(catalog.Roles.Single(r=>r.Id==entry.Id),i));
    });
    private void RefreshCards()
    {
        previews.Clear();roleCards.Children.Clear();packCards.Children.Clear();
        foreach(var r in catalog.Roles)
        {
            var panel=new StackPanel{Width=230};
            var header=new Grid();header.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});header.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});var identity=Ui.Caption(r.Name,catalog.Packs.FirstOrDefault(p=>p.Id==r.PackId)?.Name??"缺失素材包",true);header.Children.Add(identity);panel.Children.Add(header);
            var image=new Image{Width=200,Height=148,Stretch=System.Windows.Media.Stretch.Uniform};
            var previewArea=new Border{Background=Ui.Brush("Canvas"),CornerRadius=new CornerRadius(9),Padding=new Thickness(10),Margin=new Thickness(0,12,0,10),Child=image};panel.Children.Add(previewArea);
            var active=pets.Any(p=>p.RoleId==r.Id);
            try{var pair=GetPack(r.PackId);var modes=pair.Pack.Candidates(active?"menu/summoned":"standby/static_pool");if(modes.Count==0)modes=pair.Pack.Candidates("standby/pool");var cursor=new AnimationCursor(pair.Pack,modes[0],new Random());image.Source=pair.Images.Get(cursor.Current);previews.Add((image,cursor,pair.Images));}catch{}
            
            panel.Children.Add(Ui.Text(active?$"已召唤 · {pets.Count(p=>p.RoleId==r.Id)} 个实例":"未召唤",12,true));
            var row=new WrapPanel{Margin=new Thickness(0,12,0,0)};
            row.Children.Add(Ui.Button(active?"召回":"召唤",()=>{if(active)Recall(r.Id);else Summon(r);},!active));row.Children.Add(Ui.Light("设置 ›",()=>Edit(r)));
            var more=Ui.Light("⋯",()=>{});var menu=new ContextMenu();var home=new MenuItem{Header="归位",IsEnabled=active};home.Click+=(_,_)=>{foreach(var pet in pets.Where(p=>p.RoleId==r.Id))pet.Engine.ReturnHome();};menu.Items.Add(home);var exportSettings=new MenuItem{Header="导出角色设置"};exportSettings.Click+=(_,_)=>ExportRole(r);menu.Items.Add(exportSettings);var importSettings=new MenuItem{Header="导入角色设置"};importSettings.Click+=(_,_)=>ImportRole(r);menu.Items.Add(importSettings);
            var delete=new MenuItem{Header="删除角色"};delete.Click+=(_,_)=>Safe(()=>{if(MessageBox.Show(this,"删除角色及其设置？素材包将保留。","删除角色",MessageBoxButton.YesNo)!=MessageBoxResult.Yes)return;Recall(r.Id);catalog.Roles.RemoveAll(x=>x.Id==r.Id);storage.Save();RefreshCards();});menu.Items.Add(delete);more.ContextMenu=menu;more.Click+=(_,_)=>menu.IsOpen=true;more.Margin=new Thickness(0);more.Padding=new Thickness(10,5,10,5);Grid.SetColumn(more,1);header.Children.Add(more);panel.Children.Add(row);
            var card=Ui.Card(panel);card.Margin=new Thickness(0,0,14,14);roleCards.Children.Add(card);
        }
        foreach(var p in catalog.Packs)
        {
            var left=new StackPanel{Orientation=Orientation.Horizontal};
            var preview=new Image{Width=62,Height=62,Margin=new Thickness(0,0,12,0),Stretch=System.Windows.Media.Stretch.Uniform};left.Children.Add(preview);
            string detail="素材包 · "+catalog.Roles.Count(r=>r.PackId==p.Id)+" 个角色使用";
            try{var pair=GetPack(p.Id);detail="v"+pair.Pack.Version+" · "+catalog.Roles.Count(r=>r.PackId==p.Id)+" 个角色使用";var id=pair.Pack.Candidates("standby/static_pool").Concat(pair.Pack.Candidates("standby/pool")).FirstOrDefault();if(id!=null)preview.Source=pair.Images.Get(pair.Pack.Modes[id].Frames[0]);}catch{}
            left.Children.Add(Ui.Caption(p.Name,detail,true));
            var actions=new StackPanel{Orientation=Orientation.Horizontal};actions.Children.Add(Ui.Button("编辑",()=>EditPack(p)));var more=Ui.Light("⋯",()=>{});more.Width=32;more.ToolTip="素材包操作";var menu=new ContextMenu();
            void Menu(string title,Action action){var item=new MenuItem{Header=title};item.Click+=(_,_)=>Safe(action);menu.Items.Add(item);}
            Menu("复制素材包",()=>{var copy=storage.Import(storage.Resolve(p));RefreshCards();EditPack(copy);});Menu("导出素材包",()=>ExportPack(p));Menu("删除素材包",()=>{if(catalog.Roles.Any(r=>r.PackId==p.Id))throw new InvalidOperationException("素材包仍被角色使用");if(MessageBox.Show(this,"删除这个素材包？","删除素材包",MessageBoxButton.YesNo)!=MessageBoxResult.Yes)return;packages.Remove(storage.Resolve(p));storage.RemovePack(p);RefreshCards();});
            more.ContextMenu=menu;more.Click+=(_,_)=>menu.IsOpen=true;actions.Children.Add(more);packCards.Children.Add(Ui.Card(Ui.Pair(left,actions)));
        }
        Refresh();
    }
    private void Summon(RoleEntry entry)=>Safe(()=>{
        var pair=GetPack(entry.PackId);var settings=entry.Settings.Normalize();
        var existing=pets.Where(p=>p.RoleId==entry.Id).ToList();
        while(existing.Count>settings.Count){var last=existing[^1];existing.RemoveAt(existing.Count-1);last.Close();}
        while(existing.Count<settings.Count)
        {
            var role=new RoleRuntime(pair.Pack);var pet=new PetWindow(role,pair.Images,pets.Count,world,entry.Id);
            pet.OpenSettings+=id=>Edit(catalog.Roles.Single(x=>x.Id==id));pet.ChangeSettings+=ChangeRole;pet.RecallRole+=Recall;pet.ClearRoleFood+=id=>{foreach(var p in pets.Where(p=>p.RoleId==id).ToArray())world.Clear(p.Engine);};pet.ClearRoleFreezes+=id=>{foreach(var p in pets.Where(p=>p.RoleId==id))p.ClearFreezes();};pet.ResetRoleSize+=id=>{foreach(var p in pets.Where(p=>p.RoleId==id))p.ResetSize();};
            pets.Add(pet);existing.Add(pet);world.Add(pet.Engine,entry.Id);role.Thoughts.Enabled=settings.Thinking&&catalog.ThinkingPolicy!=ThinkingDisplayPolicy.None;thoughts.Add(role.Thoughts,entry.Id);
            pet.Closed+=(_,_)=>{world.Remove(pet.Engine);thoughts.Remove(role.Thoughts);pets.Remove(pet);if(!pets.Any(p=>p.RoleId==entry.Id))randomSchedule.Forget(entry.Id);if(!shuttingDown)RefreshCards();};
            pet.Show();
        }
        for(int i=0;i<existing.Count;i++)existing[i].ApplySettings(settings,i==0,catalog.ThinkingPolicy!=ThinkingDisplayPolicy.None);
        RefreshCards();
    });
}
