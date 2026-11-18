using System.IO;
using System.IO.Pipes;
using System.Text.Json;
namespace CakePet.Windows;
internal sealed record HostRequest(string Command,Guid? RoleId=null,Guid? InstanceId=null,double? Scale=null,string? Baseline=null,string? Catalog=null);
internal sealed record LiveInstance(Guid RoleId,Guid InstanceId,int InstanceNumber,double CurrentScale);
internal sealed record HostReply(bool Success,string? Error,LiveInstance[] Instances,int FoodCount,string? Catalog=null,int HostPid=0);
internal sealed class LivePet
{
    public Guid RoleId {get;private set;}
    public Guid InstanceId {get;private set;}
    public int InstanceNumber {get;private set;}
    public double CurrentScale {get;private set;}
    public LivePet(LiveInstance data)=>Update(data);
    public void Update(LiveInstance data){RoleId=data.RoleId;InstanceId=data.InstanceId;InstanceNumber=data.InstanceNumber;CurrentScale=data.CurrentScale;}
    public void SetInstanceScale(double scale){var reply=HostConnection.Call(new("scale",InstanceId:InstanceId,Scale:scale));var next=reply.Instances.FirstOrDefault(i=>i.InstanceId==InstanceId);if(next!=null)Update(next);}
}
internal static class HostConnection
{
    internal static string PipeName=>"CakePet.Host."+Environment.UserName+"."+System.Diagnostics.Process.GetCurrentProcess().SessionId;
    private static readonly object requestGate=new();
    private static int lastHostPid;
    internal static int LastHostPid=>Volatile.Read(ref lastHostPid);
    internal static HostReply Call(HostRequest request,string? pipeName=null)
    {
        lock(requestGate)return Exchange(request,pipeName);
    }
    private static HostReply Exchange(HostRequest request,string? pipeName)
    {
        using var pipe=new NamedPipeClientStream(".",pipeName??PipeName,PipeDirection.InOut,PipeOptions.CurrentUserOnly);
        pipe.Connect(3000);
        using var writer=new StreamWriter(pipe,leaveOpen:true){AutoFlush=true};
        using var reader=new StreamReader(pipe,leaveOpen:true);
        writer.WriteLine(JsonSerializer.Serialize(request,HostJson.Default.HostRequest));
        var line=reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
        var reply=JsonSerializer.Deserialize(line??throw new IOException("常驻进程未响应。"),HostJson.Default.HostReply)??throw new IOException("常驻进程返回无效数据。");
        if(reply.HostPid>0)Volatile.Write(ref lastHostPid,reply.HostPid);
        if(!reply.Success)throw new InvalidOperationException(reply.Error);
        return reply;
    }
    internal static bool? IsKnownHostRunning()
    {
        int pid=LastHostPid;if(pid<=0)return null;
        try{using var p=System.Diagnostics.Process.GetProcessById(pid);return !p.HasExited;}
        catch(ArgumentException){return false;}
        catch(InvalidOperationException){return false;}
        catch(Exception){return null;}
    }
    internal static string SettingsSignalPath=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"CakePet","settings-activation.txt");
    internal static void SignalSettings(Guid? role){CakePet.Core.ArchiveIO.Write(SettingsSignalPath,role?.ToString()??"home");}
}
#if NATIVE_HOST
internal sealed class HostServer:IDisposable
{
    private readonly CancellationTokenSource cancel=new();
    private readonly Task worker;
    public HostServer(HostLoop loop,Func<HostRequest,HostReply> handler,string? pipeName=null)
    {
        worker=Task.Run(async()=>{
            while(!cancel.IsCancellationRequested)
            {
                try
                {
                    using var pipe=new NamedPipeServerStream(pipeName??HostConnection.PipeName,PipeDirection.InOut,1,PipeTransmissionMode.Byte,PipeOptions.Asynchronous|PipeOptions.CurrentUserOnly);
                    await pipe.WaitForConnectionAsync(cancel.Token);
                    using var reader=new StreamReader(pipe,leaveOpen:true);using var writer=new StreamWriter(pipe,leaveOpen:true){AutoFlush=true};
                    var line=await reader.ReadLineAsync(cancel.Token);
                    if(line==null)continue;
                    var request=JsonSerializer.Deserialize(line,HostJson.Default.HostRequest)??throw new InvalidDataException();
                    var done=new TaskCompletionSource<HostReply>(TaskCreationOptions.RunContinuationsAsynchronously);
                    loop.Post(()=>{try{done.TrySetResult(handler(request));}catch(Exception e){done.TrySetResult(new(false,e.Message,Array.Empty<LiveInstance>(),0,HostPid:Environment.ProcessId));}});
                    var result=await done.Task.WaitAsync(cancel.Token);
                    await writer.WriteLineAsync(JsonSerializer.Serialize(result,HostJson.Default.HostReply));
                }
                catch(OperationCanceledException){break;}
                catch(Exception){if(!cancel.IsCancellationRequested)await Task.Delay(50,cancel.Token);}
            }
        });
    }
    public void Dispose(){cancel.Cancel();try{worker.Wait(1000);}catch(AggregateException){}cancel.Dispose();}
}
#endif
