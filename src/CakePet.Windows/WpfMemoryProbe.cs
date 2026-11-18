using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using CakePet.Core;

namespace CakePet.Windows;

// A separate process/mode: no real catalog, no changes to player settings.
internal static class WpfMemoryProbe
{
    public static int Run(string[] args)
    {
        if(args.Length<4||args[1] is not ("tray" or "sprite"))return 2;
        var mode=args[1];var root=Path.GetFullPath(args[2]);var output=Path.GetFullPath(args[3]);
        var duration=args.Length>4?Math.Clamp(int.Parse(args[4]),1000,60000):5000;
        var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};
        using var tray=new NativeTray(Path.Combine(AppContext.BaseDirectory,"assets","CakePet.ico"),()=>{},a=>Dispatcher.CurrentDispatcher.BeginInvoke(a),Array.Empty<(string?,Action?)>(),new Guid("fa938750-a2ed-4b60-84b9-e4a398e76ed6"),"CakePet WPF probe");
        PetWindow? pet=null;WpfImageCache? cache=null;var world=new FoodWorld();
        if(mode=="sprite")
        {
            var pack=RolePackage.Load(root,WpfImageCache.IsReadable);cache=new WpfImageCache(pack);
            var role=new RoleRuntime(pack,new Random(1234));role.Thoughts.Enabled=false;
            pet=new PetWindow(role,cache,0,world);world.Add(pet.Engine,Guid.Empty);
            pet.ApplySettings(new RoleSettings{Feed=false,AutoFeed=false,Forage=false,Thinking=false,ScreenConnect=false},true,false);pet.Show();
        }
        var watch=Stopwatch.StartNew();long last=0;int result=0;
        var timer=new DispatcherTimer(DispatcherPriority.Render){Interval=TimeSpan.FromMilliseconds(16)};
        timer.Tick+=(_,_)=>{
            try
            {
                var now=watch.ElapsedMilliseconds;var dt=Math.Clamp(now-last,0,100);last=now;
                if(pet!=null){pet.Step(dt);pet.Render();}
                if(now<duration)return;
                var report=MemoryDiagnostics.Sample("wpf-"+mode,new(){
                    ["durationMs"]=now,["trayRegistered"]=tray.Registered,["sprites"]=pet==null?0:1,
                    ["imageCacheBytes"]=cache?.CachedImageBytes??0,["foodCacheBytes"]=0});
                File.WriteAllText(output,JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));
                timer.Stop();if(pet!=null){world.Remove(pet.Engine);pet.Close();}app.Shutdown(0);
            }
            catch(Exception e){Console.Error.WriteLine(e);result=1;timer.Stop();app.Shutdown(1);}
        };
        timer.Start();app.Run();return result;
    }
}
