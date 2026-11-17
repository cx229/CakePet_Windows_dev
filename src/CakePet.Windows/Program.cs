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
    private readonly FoodWorld world=new();
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
        defaultRoot=root;storage=new CatalogStorage(root);Title="CakePet · "+Version;Width=960;Height=680;MinWidth=760;MinHeight=460;Background=Ui.Brush("Canvas");FontFamily=new System.Windows.Media.FontFamily("Microsoft YaHei UI, Segoe UI");
        WindowStartupLocation=WindowStartupLocation.CenterScreen;
        var shell=new Grid();shell.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(178)});shell.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});homeShell=shell;Content=navigationHost;navigationHost.Content=shell;
        var nav=new StackPanel{Margin=new Thickness(14,24,14,12)};shell.Children.Add(nav);var logo=Ui.Text("CakePet",26);logo.FontWeight=FontWeights.SemiBold;logo.Margin=new Thickness(6,0,0,22);nav.Children.Add(logo);
        var pages=new ContentControl{Margin=new Thickness(20,18,20,18)};Grid.SetColumn(pages,1);shell.Children.Add(pages);
        var navItems=new List<Button>();void Navigate(FrameworkElement page,int index){pages.Content=page;for(int i=0;i<navItems.Count;i++)navItems[i].Background=Ui.Brush(i==index?"AccentSoft":"Canvas");}
        var roles=new DockPanel{Margin=new Thickness(12)};
        var toolbar=new StackPanel{Orientation=Orientation.Horizontal,Margin=new Thickness(0,0,0,10)};
        toolbar.Children.Add(Ui.Button("＋ 新建角色",NewRole,true));toolbar.Children.Add(Button("召回全部",RecallAll));toolbar.Children.Add(Button("恢复全部点击",RestoreInput));
        var homeHeading=new StackPanel{Margin=new Thickness(0,0,0,14)};homeHeading.Children.Add(Ui.Text("我的角色",26));homeHeading.Children.Add(status);DockPanel.SetDock(homeHeading,Dock.Top);roles.Children.Add(homeHeading);DockPanel.SetDock(toolbar,Dock.Top);roles.Children.Add(toolbar);roles.Children.Add(new ScrollViewer{Content=roleCards,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});
        
        var materials=new DockPanel{Margin=new Thickness(12)};var materialsTitle=Ui.Text("素材包管理",26);DockPanel.SetDock(materialsTitle,Dock.Top);materials.Children.Add(materialsTitle);var importRow=new WrapPanel();importRow.Children.Add(Ui.Button("导入 ZIP",ImportArchive,true));importRow.Children.Add(Ui.Button("导入目录",ImportPack));importRow.Children.Add(Ui.Button("从模板新建",()=>Safe(()=>{var pack=storage.Import(defaultRoot);RefreshCards();EditPack(pack);})));DockPanel.SetDock(importRow,Dock.Top);materials.Children.Add(importRow);materials.Children.Add(new ScrollViewer{Content=packCards,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});
        var global=new StackPanel{Margin=new Thickness(20),MaxWidth=850};global.Children.Add(Ui.Text("全局设置",26));
        var single=new CheckBox{Content="不同角色的碎碎念只显示最新一条",IsChecked=catalog.SingleThinking,Margin=new Thickness(0,8,0,8)};single.IsVisibleChanged+=(_,_)=>single.IsChecked=catalog.SingleThinking;
        thoughts.SingleAcrossRoles=catalog.SingleThinking;single.Click+=(_,_)=>{catalog.SingleThinking=single.IsChecked==true;thoughts.SingleAcrossRoles=catalog.SingleThinking;storage.Save();};global.Children.Add(Ui.Card(single));
        var thinkingCard=new SettingCard("碎碎念显示位置","所有角色共用显示区域；不改变各角色的文本播放策略。",collapsible:false);
        var locations=new ComboBox{ItemsSource=new[]{"主屏幕底部","托盘左侧","任务栏左侧"},SelectedIndex=(int)catalog.ThinkingLocation,Width=175};
        locations.IsVisibleChanged+=(_,_)=>locations.SelectedIndex=(int)catalog.ThinkingLocation;locations.SelectionChanged+=(_,_)=>{if(locations.SelectedIndex<0)return;catalog.ThinkingLocation=(ThinkingLocation)locations.SelectedIndex;QueueSave();};
        thinkingCard.Add(Ui.Pair(Ui.Caption("显示位置","托盘不可用时自动回到主屏幕底部。"),locations));global.Children.Add(thinkingCard);
        var launch=new CheckBox{Content="启动时召唤第一个角色",IsChecked=catalog.AutoSummon,Margin=new Thickness(0,8,0,8)};launch.IsVisibleChanged+=(_,_)=>launch.IsChecked=catalog.AutoSummon;
        launch.Click+=(_,_)=>{catalog.AutoSummon=launch.IsChecked==true;storage.Save();};global.Children.Add(Ui.Card(launch));
        global.Children.Add(Button("用户习惯 · 新角色默认值",EditDefaults));
        var backup=new SettingCard("备份与恢复","导出全部持久化数据；导入时只覆盖支持的内容。");var backupActions=new WrapPanel();backupActions.Children.Add(Ui.Button("导出软件备份",ExportBackup,true));backupActions.Children.Add(Ui.Button("导入 ZIP 备份",ImportArchive));backupActions.Children.Add(Ui.Button("重置软件",ResetSoftware));backup.Add(backupActions);global.Children.Add(backup);
        global.Children.Add(Button("清除全部食物",()=>world.Clear()));global.Children.Add(Button("清除全部定格",()=>{foreach(var pet in pets)pet.ClearFreezes();}));
        foreach(var item in new[]{("首页",(FrameworkElement)roles),("素材包管理",materials),("全局设置",new ScrollViewer{Content=global,VerticalScrollBarVisibility=ScrollBarVisibility.Auto})}){int i=navItems.Count;var page=item.Item2;var b=Ui.Button(item.Item1,()=>Navigate(page,i));b.HorizontalContentAlignment=HorizontalAlignment.Left;b.Margin=new Thickness(0,4,0,4);navItems.Add(b);nav.Children.Add(b);}
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
        
        Closing+=(_,e)=>{if(!exitRequested){e.Cancel=true;Safe(FlushSave);Hide();return;}shuttingDown=true;Safe(FlushSave);timer.Stop();world.Clear();foreach(var pet in pets.ToArray())pet.Close();tray.Visible=false;tray.Dispose();trayIcon.Dispose();trayMenu.Dispose();thinkingPresenter.Dispose();Dispatcher.BeginInvoke(new Action(()=>Application.Current.Shutdown()));};
    }
    internal void StartResident(){if(started)return;started=true;if(catalog.AutoSummon&&catalog.Roles.FirstOrDefault() is RoleEntry r)Summon(r);else Refresh();lastTick=clock.ElapsedMilliseconds;timer.Start();}
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
        world.Advance(dt);
        foreach(var pet in pets.ToArray())pet.Step(dt);
        world.UpdateInteractions();
        foreach(var pet in pets.ToArray())pet.Render();
        foreach(var food in foodWindows.Values.ToArray())food.Render();
        var messages=pets.Where(p=>!string.IsNullOrWhiteSpace(p.VisibleThought)).Select(p=>(Name:catalog.Roles.FirstOrDefault(r=>r.Id==p.RoleId)?.Name??p.Engine.Role.Package.Name,Text:p.VisibleThought!)).Distinct().ToArray();
        thinkingPresenter.Render(messages,catalog.ThinkingLocation,catalog.SingleThinking);
        if(IsVisible&&navigationHost.Content==homeShell&&roleCards.IsVisible)foreach(var preview in previews){preview.Cursor.Advance(dt);var bitmap=preview.Cache.Get(preview.Cursor.Current);if(preview.Image.Source!=bitmap)preview.Image.Source=bitmap;}
    }
    private static Button Button(string text,Action action){var b=new Button{Content=text,Padding=new Thickness(12,6,12,6),Margin=new Thickness(0,3,6,3)};b.Click+=(_,_)=>action();return b;}
    private (RolePackage Pack,WpfImageCache Images) GetPack(Guid id)
    {
        var entry=catalog.Packs.Single(p=>p.Id==id);var root=storage.Resolve(entry);
        if(!packages.TryGetValue(root,out var pair)){var pack=RolePackage.Load(root,WpfImageCache.IsReadable);pair=(pack,new WpfImageCache(pack));packages[root]=pair;}return pair;
    }
    private void Safe(Action action){try{action();}catch(Exception e){MessageBox.Show(this,e.Message,"CakePet");}}
    private void RestoreInput()=>Safe(()=>{foreach(var r in catalog.Roles.ToArray())catalog.Update(r with{Settings=r.Settings with{ClickThrough=false}});storage.Save();foreach(var r in catalog.Roles)ApplyRunning(r);RefreshCards();});
    private void BackHome(){Safe(FlushSave);navigationHost.Content=homeShell;RefreshCards();}
    private void EditDefaults()=>Safe(()=>{
        navigationHost.Content=new RoleSettingsPage(()=>new RoleEntry(Guid.Empty,Guid.Empty,"新角色",catalog.Defaults),null,patch=>{catalog.Defaults=patch(new RoleEntry(Guid.Empty,Guid.Empty,"新角色",catalog.Defaults)).Settings.Normalize();QueueSave();},BackHome,true);
    });
    private void ApplyRunning(RoleEntry entry){var group=pets.Where(p=>p.RoleId==entry.Id).ToArray();for(int i=0;i<group.Length;i++)group[i].ApplySettings(entry.Settings,i==0);}
    private void ChangeRole(Guid id,Func<RoleSettings,RoleSettings> change)=>Safe(()=>{var r=catalog.Roles.Single(x=>x.Id==id);catalog.Update(r with{Settings=change(r.Settings)});storage.Save();ApplyRunning(catalog.Roles.Single(x=>x.Id==id));RefreshCards();});
    private void RecallAll(){foreach(var pet in pets.ToArray())pet.Close();}
    private void Recall(Guid id){foreach(var pet in pets.Where(p=>p.RoleId==id).ToArray())pet.Close();}
    private void ImportPack()=>Safe(()=>{
        var dialog=new OpenFolderDialog{Title="选择包含 modes.jsonc 的目录"};if(dialog.ShowDialog(this)!=true)return;
        storage.Import(dialog.FolderName);RefreshCards();
    });
    private string? ChooseSave(string filename,string filter){var d=new SaveFileDialog{FileName=filename,Filter=filter};return d.ShowDialog(this)==true?d.FileName:null;}
    private void ExportBackup()=>Safe(()=>{FlushSave();var path=ChooseSave("CakePet-backup-"+DateTime.Now.ToString("yyyyMMdd-HHmm")+".zip","ZIP 文件|*.zip");if(path!=null)storage.ExportBackup(path,Version);});
    private void ImportArchive()=>Safe(()=>{
        var d=new OpenFileDialog{Filter="CakePet ZIP|*.zip"};if(d.ShowDialog(this)!=true)return;
        if(MessageBox.Show(this,"导入 ZIP。若为软件备份，将选择性覆盖可兼容的数据，其他数据保留。","导入",MessageBoxButton.OKCancel)!=MessageBoxResult.OK)return;
        FlushSave();var active=pets.Select(x=>x.RoleId).Distinct().ToArray();RecallAll();packages.Clear();
        try{var result=storage.ImportZip(d.FileName);thoughts.SingleAcrossRoles=catalog.SingleThinking;BackHome();MessageBox.Show(this,result,"导入完成");}
        finally{foreach(var id in active)if(catalog.Roles.FirstOrDefault(x=>x.Id==id) is RoleEntry r)Summon(r);}
    });
    private void ResetSoftware()=>Safe(()=>{if(MessageBox.Show(this,"清空角色、素材包和设置，恢复首次安装状态？请先导出备份。","重置软件",MessageBoxButton.YesNo)!=MessageBoxResult.Yes)return;FlushSave();RecallAll();packages.Clear();storage.Reset(defaultRoot);thoughts.SingleAcrossRoles=catalog.SingleThinking;BackHome();});
    private void ExportRole(RoleEntry role)=>Safe(()=>{var path=ChooseSave("CakePet-role-settings.json","JSON 文件|*.json");if(path!=null)storage.ExportRole(catalog.Roles.Single(x=>x.Id==role.Id),path);});
    private void ImportRole(RoleEntry role)=>Safe(()=>{var d=new OpenFileDialog{Filter="角色设置|*.json"};if(d.ShowDialog(this)!=true)return;var next=storage.ImportRole(catalog.Roles.Single(x=>x.Id==role.Id),d.FileName);catalog.Update(next);storage.Save();if(pets.Any(p=>p.RoleId==role.Id))Summon(next);RefreshCards();});
    private void EditPack(PackEntry p)=>Safe(()=>{navigationHost.Content=new PackageEditorPage(storage.Resolve(p),()=>{var ids=catalog.Roles.Where(x=>x.PackId==p.Id&&pets.Any(w=>w.RoleId==x.Id)).Select(x=>x.Id).ToArray();foreach(var id in ids)Recall(id);packages.Remove(storage.Resolve(p));var parsed=GetPack(p.Id).Pack;var index=catalog.Packs.FindIndex(x=>x.Id==p.Id);catalog.Packs[index]=p with{Name=parsed.Name};storage.Save();RefreshCards();foreach(var id in ids)Summon(catalog.Roles.Single(x=>x.Id==id));},BackHome);});
    private void ExportPack(PackEntry p)=>Safe(()=>{var pack=GetPack(p.Id).Pack;var name=string.Concat(pack.Name.Select(c=>Path.GetInvalidFileNameChars().Contains(c)?'_':c));var path=ChooseSave(name+"-v"+pack.Version+".zip","ZIP 文件|*.zip");if(path!=null)storage.ExportPack(p,path);});
    private void NewRole()=>Safe(()=>{
        if(catalog.Packs.Count==0){ImportPack();if(catalog.Packs.Count==0)return;}
        var panel=new StackPanel{Margin=new Thickness(24),MaxWidth=700};panel.Children.Add(Ui.Button("‹ 返回",BackHome));panel.Children.Add(Ui.Text("选择素材包",24));
        foreach(var pack in catalog.Packs){var choice=Ui.Button(pack.Name,()=>{var role=catalog.AddRole(pack.Id,pack.Name);storage.Save();RefreshCards();Edit(role);});panel.Children.Add(Ui.Card(choice));}
        navigationHost.Content=new ScrollViewer{Content=panel};
    });
    private void Edit(RoleEntry entry)=>Safe(()=>{
        ShowLauncher();
        navigationHost.Content=new RoleSettingsPage(()=>catalog.Roles.Single(x=>x.Id==entry.Id),GetPack(entry.PackId).Pack,patch=>Safe(()=>{
            var old=catalog.Roles.Single(x=>x.Id==entry.Id);var next=patch(old);if(next==old)return;catalog.Update(next);QueueSave();var latest=catalog.Roles.Single(x=>x.Id==entry.Id);
            var live=pets.Count(p=>p.RoleId==entry.Id);if(live>0&&live!=latest.Settings.Count)Summon(latest);else if(old.Settings!=latest.Settings)ApplyRunning(latest);
        }),BackHome);
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
            row.Children.Add(Ui.Button(active?"召回":"召唤",()=>{if(active)Recall(r.Id);else Summon(r);},true));row.Children.Add(Ui.Button("设置",()=>Edit(r)));
            var more=Ui.Button("⋯",()=>{});var menu=new ContextMenu();var home=new MenuItem{Header="归位",IsEnabled=active};home.Click+=(_,_)=>{foreach(var pet in pets.Where(p=>p.RoleId==r.Id))pet.Engine.ReturnHome();};menu.Items.Add(home);var exportSettings=new MenuItem{Header="导出角色设置"};exportSettings.Click+=(_,_)=>ExportRole(r);menu.Items.Add(exportSettings);var importSettings=new MenuItem{Header="导入角色设置"};importSettings.Click+=(_,_)=>ImportRole(r);menu.Items.Add(importSettings);
            var delete=new MenuItem{Header="删除角色"};delete.Click+=(_,_)=>Safe(()=>{if(MessageBox.Show(this,"删除角色及其设置？素材包将保留。","删除角色",MessageBoxButton.YesNo)!=MessageBoxResult.Yes)return;Recall(r.Id);catalog.Roles.RemoveAll(x=>x.Id==r.Id);storage.Save();RefreshCards();});menu.Items.Add(delete);more.ContextMenu=menu;more.Click+=(_,_)=>menu.IsOpen=true;more.Margin=new Thickness(0);more.Padding=new Thickness(10,5,10,5);Grid.SetColumn(more,1);header.Children.Add(more);panel.Children.Add(row);
            var card=Ui.Card(panel);card.Margin=new Thickness(0,0,14,14);roleCards.Children.Add(card);
        }
        foreach(var p in catalog.Packs)
        {
            var row=new StackPanel{Margin=new Thickness(10)};
            row.Children.Add(new TextBlock{Text=p.Name,FontSize=18});
            row.Children.Add(new TextBlock{Text=$"{catalog.Roles.Count(r=>r.PackId==p.Id)} 个角色使用"});
            row.Children.Add(Ui.Button("编辑素材包",()=>EditPack(p),true));row.Children.Add(Ui.Button("复制素材包",()=>Safe(()=>{var copy=storage.Import(storage.Resolve(p));RefreshCards();EditPack(copy);})));row.Children.Add(Ui.Button("导出素材包",()=>ExportPack(p)));row.Children.Add(Button("删除素材包",()=>Safe(()=>{if(catalog.Roles.Any(r=>r.PackId==p.Id))throw new InvalidOperationException("素材包仍被角色使用");if(MessageBox.Show(this,"删除这个素材包？","删除素材包",MessageBoxButton.YesNo)!=MessageBoxResult.Yes)return;packages.Remove(storage.Resolve(p));storage.RemovePack(p);RefreshCards();})));
            packCards.Children.Add(Ui.Card(row));
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
            pets.Add(pet);existing.Add(pet);world.Add(pet.Engine,entry.Id);thoughts.Add(role.Thoughts);
            pet.Closed+=(_,_)=>{world.Remove(pet.Engine);thoughts.Remove(role.Thoughts);pets.Remove(pet);if(!shuttingDown)RefreshCards();};
            pet.Show();
        }
        for(int i=0;i<existing.Count;i++)existing[i].ApplySettings(settings,i==0);
        RefreshCards();
    });
}
