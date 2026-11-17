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
        var app=new Application{ShutdownMode=ShutdownMode.OnMainWindowClose};
        return app.Run(new Launcher(root));
    }
}

internal sealed class Launcher : Window
{
    private readonly string defaultRoot;
    private readonly CatalogStorage storage;
    private RoleCatalog catalog=>storage.Catalog;
    private readonly StackPanel roleCards=new();
    private readonly StackPanel packCards=new();
    private readonly List<PetWindow> pets=new();
    private readonly Dictionary<string,(RolePackage Pack,WpfImageCache Images)> packages=new(StringComparer.OrdinalIgnoreCase);
    private readonly ThinkingCoordinator thoughts=new();
    private readonly FoodWorld world=new();
    private readonly Dictionary<FoodInstance,FoodWindow> foodWindows=new();
    private readonly DispatcherTimer timer=new(DispatcherPriority.Render){Interval=TimeSpan.FromMilliseconds(16)};
    private readonly Stopwatch clock=Stopwatch.StartNew();
    private long lastTick;
    private bool shuttingDown,exitRequested;
    private readonly Forms.NotifyIcon tray;
    private string Version=>typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion??"dev";
    private readonly TextBlock status=new(){TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,12,0,0)};
    public Launcher(string root)
    {
        defaultRoot=root;storage=new CatalogStorage(root);Title="CakePet · "+Version;Width=780;Height=620;MinWidth=620;MinHeight=420;
        WindowStartupLocation=WindowStartupLocation.CenterScreen;
        var shell=new DockPanel{Margin=new Thickness(24)};
        var heading=new StackPanel{Margin=new Thickness(0,0,0,16)};
        heading.Children.Add(new TextBlock{Text="CakePet",FontSize=30,FontWeight=FontWeights.SemiBold});
        heading.Children.Add(status);DockPanel.SetDock(heading,Dock.Top);shell.Children.Add(heading);
        var tabs=new TabControl();shell.Children.Add(tabs);Content=shell;
        var roles=new DockPanel{Margin=new Thickness(12)};
        var toolbar=new StackPanel{Orientation=Orientation.Horizontal,Margin=new Thickness(0,0,0,10)};
        toolbar.Children.Add(Button("新建角色",NewRole));toolbar.Children.Add(Button("召回全部",RecallAll));toolbar.Children.Add(Button("恢复全部点击",()=>{foreach(var pet in pets)pet.ClickThrough=false;}));
        DockPanel.SetDock(toolbar,Dock.Top);roles.Children.Add(toolbar);roles.Children.Add(new ScrollViewer{Content=roleCards,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});
        tabs.Items.Add(new TabItem{Header="角色",Content=roles});
        var materials=new DockPanel{Margin=new Thickness(12)};var import=Button("导入素材包目录",ImportPack);DockPanel.SetDock(import,Dock.Top);materials.Children.Add(import);materials.Children.Add(new ScrollViewer{Content=packCards,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});tabs.Items.Add(new TabItem{Header="素材包",Content=materials});
        var global=new StackPanel{Margin=new Thickness(20)};
        var single=new CheckBox{Content="不同角色的碎碎念只显示最新一条",IsChecked=catalog.SingleThinking,Margin=new Thickness(0,8,0,8)};
        thoughts.SingleAcrossRoles=catalog.SingleThinking;single.Click+=(_,_)=>{catalog.SingleThinking=single.IsChecked==true;thoughts.SingleAcrossRoles=catalog.SingleThinking;storage.Save();};global.Children.Add(single);
        var launch=new CheckBox{Content="启动时召唤第一个角色",IsChecked=catalog.AutoSummon,Margin=new Thickness(0,8,0,8)};
        launch.Click+=(_,_)=>{catalog.AutoSummon=launch.IsChecked==true;storage.Save();};global.Children.Add(launch);
        global.Children.Add(Button("清除全部食物",()=>world.Clear()));global.Children.Add(Button("清除全部定格",()=>{foreach(var pet in pets)pet.ClearFreezes();}));
        tabs.Items.Add(new TabItem{Header="全局设置",Content=global});RefreshCards();
        var trayMenu=new Forms.ContextMenuStrip();
        void TrayItem(string title,Action action)=>trayMenu.Items.Add(title,null,(_,_)=>Dispatcher.Invoke(action));
        TrayItem("打开 CakePet",ShowLauncher);TrayItem("召唤第一个角色",()=>{if(catalog.Roles.FirstOrDefault() is RoleEntry r)Summon(r);});
        TrayItem("召回全部",()=>{foreach(var pet in pets.ToArray())pet.Close();});
        TrayItem("恢复全部点击",()=>{foreach(var pet in pets)pet.ClickThrough=false;});
        TrayItem("清除全部定格",()=>{foreach(var pet in pets)pet.ClearFreezes();});
        trayMenu.Items.Add(new Forms.ToolStripSeparator());TrayItem("退出",()=>{exitRequested=true;Close();});
        tray=new Forms.NotifyIcon{Icon=System.Drawing.SystemIcons.Application,Text="CakePet "+Version,ContextMenuStrip=trayMenu,Visible=true};
        tray.DoubleClick+=(_,_)=>Dispatcher.Invoke(ShowLauncher);
        StateChanged+=(_,_)=>{if(WindowState==WindowState.Minimized)Hide();};
        System.Windows.Application.Current.SessionEnding+=(_,_)=>{exitRequested=true;Close();};
        world.Spawned+=OnFoodSpawned;world.Removed+=OnFoodRemoved;
        timer.Tick+=(_,_)=>Tick();
        Loaded+=(_,_)=>{if(catalog.AutoSummon&&catalog.Roles.FirstOrDefault() is RoleEntry r)Summon(r);else Refresh();lastTick=clock.ElapsedMilliseconds;timer.Start();};
        Closing+=(_,e)=>{if(!exitRequested){e.Cancel=true;Hide();return;}shuttingDown=true;timer.Stop();world.Clear();foreach(var pet in pets.ToArray())pet.Close();tray.Visible=false;tray.Dispose();trayMenu.Dispose();};
    }
    private void ShowLauncher(){Show();WindowState=WindowState.Normal;Activate();}
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
    }
    private static Button Button(string text,Action action){var b=new Button{Content=text,Padding=new Thickness(12,6,12,6),Margin=new Thickness(0,3,6,3)};b.Click+=(_,_)=>action();return b;}
    private (RolePackage Pack,WpfImageCache Images) GetPack(Guid id)
    {
        var entry=catalog.Packs.Single(p=>p.Id==id);var root=storage.Resolve(entry);
        if(!packages.TryGetValue(root,out var pair)){var pack=RolePackage.Load(root,WpfImageCache.IsReadable);pair=(pack,new WpfImageCache(pack));packages[root]=pair;}return pair;
    }
    private void Safe(Action action){try{action();}catch(Exception e){MessageBox.Show(this,e.Message,"CakePet");}}
    private void RecallAll(){foreach(var pet in pets.ToArray())pet.Close();}
    private void Recall(Guid id){foreach(var pet in pets.Where(p=>p.RoleId==id).ToArray())pet.Close();}
    private void ImportPack()=>Safe(()=>{
        var dialog=new OpenFolderDialog{Title="选择包含 modes.jsonc 的目录"};if(dialog.ShowDialog(this)!=true)return;
        storage.Import(dialog.FolderName);RefreshCards();
    });
    private void NewRole()=>Safe(()=>{
        if(catalog.Packs.Count==0){ImportPack();if(catalog.Packs.Count==0)return;}
        var picker=new ComboBox{ItemsSource=catalog.Packs,DisplayMemberPath="Name",SelectedIndex=0,Margin=new Thickness(12)};
        var win=new Window{Title="选择素材包",Owner=this,Width=360,Height=160,WindowStartupLocation=WindowStartupLocation.CenterOwner};
        var panel=new StackPanel();panel.Children.Add(picker);panel.Children.Add(Button("新建",()=>win.DialogResult=true));win.Content=panel;
        if(win.ShowDialog()!=true||picker.SelectedItem is not PackEntry pack)return;
        var role=catalog.AddRole(pack.Id,pack.Name);storage.Save();RefreshCards();Edit(role);
    });
    private void Edit(RoleEntry entry)=>Safe(()=>{
        var dialog=new RoleSettingsWindow(entry,GetPack(entry.PackId).Pack){Owner=this};
        if(dialog.ShowDialog()!=true)return;catalog.Update(dialog.Result);storage.Save();
        if(pets.Any(p=>p.RoleId==entry.Id))Summon(catalog.Roles.Single(r=>r.Id==entry.Id));RefreshCards();
    });
    private void RefreshCards()
    {
        roleCards.Children.Clear();packCards.Children.Clear();
        foreach(var r in catalog.Roles)
        {
            var panel=new StackPanel{Margin=new Thickness(12)};
            panel.Children.Add(new TextBlock{Text=r.Name,FontSize=20,FontWeight=FontWeights.SemiBold});
            panel.Children.Add(new TextBlock{Text=$"{catalog.Packs.FirstOrDefault(p=>p.Id==r.PackId)?.Name??"缺失素材包"} · 已召唤 {pets.Count(p=>p.RoleId==r.Id)}/{r.Settings.Count}",Margin=new Thickness(0,4,0,4)});
            var row=new WrapPanel();row.Children.Add(Button("召唤",()=>Summon(r)));row.Children.Add(Button("召回",()=>Recall(r.Id)));row.Children.Add(Button("设置",()=>Edit(r)));
            row.Children.Add(Button("删除",()=>Safe(()=>{if(MessageBox.Show(this,"删除角色及其设置？素材包将保留。","删除角色",MessageBoxButton.YesNo)!=MessageBoxResult.Yes)return;Recall(r.Id);catalog.Roles.RemoveAll(x=>x.Id==r.Id);storage.Save();RefreshCards();})));panel.Children.Add(row);
            roleCards.Children.Add(new Border{BorderBrush=System.Windows.Media.Brushes.LightGray,BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(8),Margin=new Thickness(0,0,0,10),Child=panel});
        }
        foreach(var p in catalog.Packs)
        {
            var row=new StackPanel{Margin=new Thickness(10)};
            row.Children.Add(new TextBlock{Text=p.Name,FontSize=18});
            row.Children.Add(new TextBlock{Text=$"{catalog.Roles.Count(r=>r.PackId==p.Id)} 个角色使用"});
            row.Children.Add(Button("删除素材包",()=>Safe(()=>{if(catalog.Roles.Any(r=>r.PackId==p.Id))throw new InvalidOperationException("素材包仍被角色使用");if(MessageBox.Show(this,"删除这个素材包？","删除素材包",MessageBoxButton.YesNo)!=MessageBoxResult.Yes)return;packages.Remove(storage.Resolve(p));storage.RemovePack(p);RefreshCards();})));
            packCards.Children.Add(row);
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
            pets.Add(pet);existing.Add(pet);world.Add(pet.Engine,entry.Id);thoughts.Add(role.Thoughts);
            pet.Closed+=(_,_)=>{world.Remove(pet.Engine);thoughts.Remove(role.Thoughts);pets.Remove(pet);if(!shuttingDown)RefreshCards();};
            pet.Show();
        }
        for(int i=0;i<existing.Count;i++)existing[i].ApplySettings(settings,i==0);
        RefreshCards();
    });
}
