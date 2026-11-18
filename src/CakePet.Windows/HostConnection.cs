using System.IO;
using System.IO.Pipes;
using System.Text.Json;
namespace CakePet.Windows;
internal sealed record HostRequest(string Command,Guid? RoleId=null,Guid? InstanceId=null,double? Scale=null);
internal sealed record LiveInstance(Guid RoleId,Guid InstanceId,int InstanceNumber,double CurrentScale);
internal sealed record HostReply(bool Success,string? Error,LiveInstance[] Instances,int FoodCount);
internal static class HostConnection
{
    internal static string PipeName=>"CakePet.Host."+Environment.UserName+"."+System.Diagnostics.Process.GetCurrentProcess().SessionId;
    internal static HostReply Call(HostRequest request)
    {
        using var pipe=new NamedPipeClientStream(".",PipeName,PipeDirection.InOut,PipeOptions.CurrentUserOnly);
        pipe.Connect(3000);
        using var writer=new StreamWriter(pipe,leaveOpen:true){AutoFlush=true};
        using var reader=new StreamReader(pipe,leaveOpen:true);
        writer.WriteLine(JsonSerializer.Serialize(request));
        var line=reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
        var reply=JsonSerializer.Deserialize<HostReply>(line??throw new IOException("常驻进程未响应。"))??throw new IOException("常驻进程返回无效数据。");
        if(!reply.Success)throw new InvalidOperationException(reply.Error);
        return reply;
    }
    internal static string SettingsSignalPath=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"CakePet","settings-activation.txt");
    internal static void SignalSettings(Guid? role){File.WriteAllText(SettingsSignalPath,role?.ToString()??"home");}
}
#if NATIVE_HOST
internal sealed class HostServer:IDisposable
{
    private readonly CancellationTokenSource cancel=new();
    private readonly Task worker;
    public HostServer(HostLoop loop,Func<HostRequest,HostReply> handler)
    {
        worker=Task.Run(async()=>{
            while(!cancel.IsCancellationRequested)
            {
                try
                {
                    using var pipe=new NamedPipeServerStream(HostConnection.PipeName,PipeDirection.InOut,1,PipeTransmissionMode.Byte,PipeOptions.Asynchronous|PipeOptions.CurrentUserOnly);
                    await pipe.WaitForConnectionAsync(cancel.Token);
                    using var reader=new StreamReader(pipe,leaveOpen:true);using var writer=new StreamWriter(pipe,leaveOpen:true){AutoFlush=true};
                    var line=await reader.ReadLineAsync(cancel.Token);
                    if(line==null)continue;
                    var request=JsonSerializer.Deserialize<HostRequest>(line)??throw new InvalidDataException();
                    var done=new TaskCompletionSource<HostReply>(TaskCreationOptions.RunContinuationsAsynchronously);
                    loop.Post(()=>{try{done.TrySetResult(handler(request));}catch(Exception e){done.TrySetResult(new(false,e.Message,Array.Empty<LiveInstance>(),0));}});
                    var result=await done.Task.WaitAsync(cancel.Token);
                    await writer.WriteLineAsync(JsonSerializer.Serialize(result));
                }
                catch(OperationCanceledException){break;}
                catch(Exception){if(!cancel.IsCancellationRequested)await Task.Delay(50,cancel.Token);}
            }
        });
    }
    public void Dispose(){cancel.Cancel();try{worker.Wait(1000);}catch(AggregateException){}cancel.Dispose();}
}
#endif
