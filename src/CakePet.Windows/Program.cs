using System.IO;
using System.Windows;
using System.Windows.Controls;
using CakePet.Core;
using Microsoft.Win32;

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
            var cache=new WpfImageCache(pack);
            foreach(var mode in pack.Modes.Values)foreach(var frame in mode.Frames)
                if(cache.Get(frame).PixelWidth<=0)throw new InvalidDataException(frame.Resource);
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
    private readonly TextBlock status=new(){TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,12,0,0)};
    public Launcher(string root)
    {
        defaultRoot=root;Title="CakePet · 引擎测试版";Width=380;Height=260;MinWidth=330;MinHeight=220;
        WindowStartupLocation=WindowStartupLocation.CenterScreen;
        var panel=new StackPanel{Margin=new Thickness(20)};
        panel.Children.Add(new TextBlock{Text="CakePet",FontSize=26,FontWeight=FontWeights.SemiBold});
        panel.Children.Add(new TextBlock{Text="Windows 核心引擎验证",Margin=new Thickness(0,0,0,12)});
        Add("召唤白酥",()=>Summon(defaultRoot));
        Add("从素材包目录召唤",()=>{var dialog=new OpenFolderDialog{Title="选择包含 modes.jsonc 的目录"};if(dialog.ShowDialog(this)==true)Summon(dialog.FolderName);});
        Add("召回全部",()=>{foreach(var pet in pets.ToArray())pet.Close();});
        var single=new CheckBox{Content="不同角色的碎碎念只显示最新一条",Margin=new Thickness(0,8,0,0)};
        single.Checked+=(_,_)=>thoughts.SingleAcrossRoles=true;single.Unchecked+=(_,_)=>thoughts.SingleAcrossRoles=false;panel.Children.Add(single);
        panel.Children.Add(status);Content=panel;
        Loaded+=(_,_)=>{if(File.Exists(Path.Combine(defaultRoot,"modes.jsonc")))Summon(defaultRoot);else Refresh();};
        Closing+=(_,_)=>{foreach(var pet in pets.ToArray())pet.Close();};
        void Add(string text,Action action){var button=new Button{Content=text,Margin=new Thickness(0,3,0,3),Padding=new Thickness(10,5,10,5)};button.Click+=(_,_)=>action();panel.Children.Add(button);}
    }
    private void Refresh()=>status.Text=$"已召唤 {pets.Count} 个角色实例。点击摸头，拖动后松手抛掷；右键查看测试操作。";
    private void Summon(string root)
    {
        try
        {
            if(!packages.TryGetValue(root,out var pair))
            {var pack=RolePackage.Load(root,WpfImageCache.IsReadable);pair=(pack,new WpfImageCache(pack));packages[root]=pair;}
            var role=new RoleRuntime(pair.Pack);
            var pet=new PetWindow(role,pair.Images,pets.Count);
            pets.Add(pet);thoughts.Add(role.Thoughts);
            pet.Closed+=(_,_)=>{thoughts.Remove(role.Thoughts);pets.Remove(pet);Refresh();};
            pet.Show();Refresh();
        }
        catch(Exception e){MessageBox.Show(this,e.Message,"无法召唤角色",MessageBoxButton.OK,MessageBoxImage.Error);}
    }
}
