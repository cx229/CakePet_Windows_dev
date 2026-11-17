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
        defaultRoot=root;Title="CakePet · "+Version;Width=420;Height=420;MinWidth=350;MinHeight=280;
        WindowStartupLocation=WindowStartupLocation.CenterScreen;
        var panel=new StackPanel{Margin=new Thickness(20)};
        panel.Children.Add(new TextBlock{Text="CakePet",FontSize=26,FontWeight=FontWeights.SemiBold});
        panel.Children.Add(new TextBlock{Text="Windows 核心引擎验证",Margin=new Thickness(0,0,0,12)});
        Add("召唤白酥",()=>Summon(defaultRoot));
        Add("从素材包目录召唤",()=>{var dialog=new OpenFolderDialog{Title="选择包含 modes.jsonc 的目录"};if(dialog.ShowDialog(this)==true)Summon(dialog.FolderName);});
        Add("召回全部",()=>{foreach(var pet in pets.ToArray())pet.Close();});
        Add("清除全部食物",()=>world.Clear());
        Add("清除全部定格",()=>{foreach(var pet in pets)pet.ClearFreezes();});
        Add("恢复全部点击",()=>{foreach(var pet in pets)pet.ClickThrough=false;});
        var single=new CheckBox{Content="不同角色的碎碎念只显示最新一条",Margin=new Thickness(0,8,0,0)};
        single.Checked+=(_,_)=>thoughts.SingleAcrossRoles=true;single.Unchecked+=(_,_)=>thoughts.SingleAcrossRoles=false;panel.Children.Add(single);
        panel.Children.Add(status);Content=panel;
        var trayMenu=new Forms.ContextMenuStrip();
        void TrayItem(string title,Action action)=>trayMenu.Items.Add(title,null,(_,_)=>Dispatcher.Invoke(action));
        TrayItem("打开 CakePet",ShowLauncher);TrayItem("召唤白酥",()=>Summon(defaultRoot));
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
        Loaded+=(_,_)=>{if(File.Exists(Path.Combine(defaultRoot,"modes.jsonc")))Summon(defaultRoot);else Refresh();lastTick=clock.ElapsedMilliseconds;timer.Start();};
        Closing+=(_,e)=>{if(!exitRequested){e.Cancel=true;Hide();return;}shuttingDown=true;timer.Stop();world.Clear();foreach(var pet in pets.ToArray())pet.Close();tray.Visible=false;tray.Dispose();trayMenu.Dispose();};
        void Add(string text,Action action){var button=new Button{Content=text,Margin=new Thickness(0,3,0,3),Padding=new Thickness(10,5,10,5)};button.Click+=(_,_)=>action();panel.Children.Add(button);}
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
    private void Summon(string root)
    {
        try
        {
            if(!packages.TryGetValue(root,out var pair))
            {var pack=RolePackage.Load(root,WpfImageCache.IsReadable);pair=(pack,new WpfImageCache(pack));packages[root]=pair;}
            var role=new RoleRuntime(pair.Pack);
            var pet=new PetWindow(role,pair.Images,pets.Count,world);
            pets.Add(pet);world.Add(pet.Engine);thoughts.Add(role.Thoughts);
            pet.Closed+=(_,_)=>{world.Remove(pet.Engine);thoughts.Remove(role.Thoughts);pets.Remove(pet);Refresh();};
            pet.Show();Refresh();
        }
        catch(Exception e){MessageBox.Show(this,e.Message,"无法召唤角色",MessageBoxButton.OK,MessageBoxImage.Error);}
    }
}
