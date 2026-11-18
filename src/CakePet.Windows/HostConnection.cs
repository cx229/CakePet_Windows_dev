using System.IO;
using System.IO.Pipes;
using System.Text.Json;
namespace CakePet.Windows;
internal sealed record HostRequest(string Command,Guid? RoleId=null,Guid? InstanceId=null,double? Scale=null,string? Baseline=null,string? Catalog=null,CakePet.Core.PackEntry? Pack=null,string? Feature=null,bool? Allowed=null,Guid? RequestId=null);
internal sealed record LiveInstance(Guid RoleId,Guid InstanceId,int InstanceNumber,double CurrentScale);
internal sealed record HostReply(bool Success,string? Error,LiveInstance[] Instances,int FoodCount,string? Catalog=null,int HostPid=0,Guid? RequestId=null);
internal sealed class LivePet
{
    public Guid RoleId {get;private set;}
    public Guid InstanceId {get;private set;}
    public int InstanceNumber {get;private set;}
    public double CurrentScale {get;private set;}
    public LivePet(LiveInstance data)=>Update(data);
    public void Update(LiveInstance data){RoleId=data.RoleId;InstanceId=data.InstanceId;InstanceNumber=data.InstanceNumber;CurrentScale=data.CurrentScale;}
    private bool scaling;
    private double? pendingScale;
    public async void SetInstanceScale(double scale)
    {
        pendingScale=scale;if(scaling)return;scaling=true;
        try
        {
            while(pendingScale is double nextScale)
            {
                pendingScale=null;
                var reply=await HostConnection.CallAsync(new("scale",InstanceId:InstanceId,Scale:nextScale));
                var next=reply.Instances.FirstOrDefault(i=>i.InstanceId==InstanceId);if(next!=null)Update(next);
            }
        }
        catch(Exception e){pendingScale=null;
#if !NATIVE_HOST
            System.Windows.MessageBox.Show(e.Message,"CakePet");
#endif
        }
        finally{scaling=false;}
    }
}
internal sealed class HostRejectedException(string message):InvalidOperationException(message) {}
internal static class HostConnection
{
    internal static string PipeName=>"CakePet.Host."+Environment.UserName+"."+System.Diagnostics.Process.GetCurrentProcess().SessionId;
    private static int lastHostPid;
    internal static int LastHostPid=>Volatile.Read(ref lastHostPid);
    internal static HostReply Call(HostRequest request,string? pipeName=null)=>CallAsync(request,pipeName).GetAwaiter().GetResult();
    internal static async Task<HostReply> CallAsync(HostRequest request,string? pipeName=null,CancellationToken cancellation=default)
    {
        request=request with{RequestId=request.RequestId??Guid.NewGuid()};
        var watch=System.Diagnostics.Stopwatch.StartNew();string stage="connect";
        try
        {
            using var pipe=new NamedPipeClientStream(".",pipeName??PipeName,PipeDirection.InOut,PipeOptions.Asynchronous|PipeOptions.CurrentUserOnly);
            using(var connect=CancellationTokenSource.CreateLinkedTokenSource(cancellation)){connect.CancelAfter(3000);await pipe.ConnectAsync(connect.Token).ConfigureAwait(false);}
            using var deadline=CancellationTokenSource.CreateLinkedTokenSource(cancellation);deadline.CancelAfter(TimeSpan.FromSeconds(10));
            using var reader=new StreamReader(pipe,leaveOpen:true);
            stage="send";
            var payload=System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(request,HostJson.Default.HostRequest)+"\n");
            await pipe.WriteAsync(payload.AsMemory(),deadline.Token).ConfigureAwait(false);
            await pipe.FlushAsync(deadline.Token).ConfigureAwait(false);
            stage="response";
            var line=await reader.ReadLineAsync(deadline.Token).ConfigureAwait(false);
            var reply=JsonSerializer.Deserialize(line??throw new IOException("常驻进程未响应。"),HostJson.Default.HostReply)??throw new IOException("常驻进程返回无效数据。");
            if(reply.RequestId!=request.RequestId)throw new IOException("常驻进程响应编号不匹配。");
            if(reply.HostPid>0)Volatile.Write(ref lastHostPid,reply.HostPid);
            if(!reply.Success)throw new HostRejectedException(reply.Error??"常驻进程拒绝操作。");
            return reply;
        }
        catch(Exception e)
        {
            IpcLog.Write("client",request,stage,watch.ElapsedMilliseconds,e);
            if(e is OperationCanceledException&&!cancellation.IsCancellationRequested)throw new TimeoutException($"常驻进程未及时响应（{request.Command} / {stage}）。",e);
            throw;
        }
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
internal static class IpcLog
{
    private static readonly object gate=new();
    internal static void Write(string side,HostRequest? request,string stage,long ms,Exception? error=null)
    {
        try
        {
            lock(gate)
            {
                var dir=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"CakePet","diagnostics");Directory.CreateDirectory(dir);
                var path=Path.Combine(dir,"ipc-"+side+".log");
                if(File.Exists(path)&&new FileInfo(path).Length>1024*1024)File.Move(path,path+".old",true);
                File.AppendAllText(path,$"{DateTimeOffset.Now:O} {request?.RequestId} {request?.Command} {stage} {ms}ms {error}{Environment.NewLine}");
            }
        }catch(Exception){}
    }
}
#if NATIVE_HOST
internal sealed class HostServer:IDisposable
{
    private readonly CancellationTokenSource cancel=new();
    private readonly Task[] workers;
    private const int Connections=4;
    public HostServer(HostLoop loop,Func<HostRequest,HostReply> handler,string? pipeName=null)
    {
        workers=Enumerable.Range(0,Connections).Select(_=>Task.Run(()=>Serve(loop,handler,pipeName??HostConnection.PipeName,cancel.Token))).ToArray();
    }
    private static async Task Serve(HostLoop loop,Func<HostRequest,HostReply> handler,string name,CancellationToken shutdown)
    {
        while(!shutdown.IsCancellationRequested)
        {
            HostRequest? request=null;var watch=System.Diagnostics.Stopwatch.StartNew();string stage="accept";
            try
            {
                using var pipe=new NamedPipeServerStream(name,PipeDirection.InOut,Connections,PipeTransmissionMode.Byte,PipeOptions.Asynchronous|PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(shutdown).ConfigureAwait(false);watch.Restart();
                using var deadline=CancellationTokenSource.CreateLinkedTokenSource(shutdown);deadline.CancelAfter(TimeSpan.FromSeconds(9));
                using var reader=new StreamReader(pipe,leaveOpen:true);
                stage="read";
                var line=await reader.ReadLineAsync(deadline.Token).ConfigureAwait(false);
                if(line==null)continue;
                request=JsonSerializer.Deserialize(line,HostJson.Default.HostRequest)??throw new InvalidDataException();
                var req=request;var done=new TaskCompletionSource<HostReply>(TaskCreationOptions.RunContinuationsAsynchronously);
                stage="queue";
                // Read-only polling should not dismiss a user's context menu.
                if(req.Command is not ("snapshot" or "diagnostics"))NativeMenu.CancelForRequest();
                else if(NativeMenu.IsOpen)throw new IOException("原生菜单正在交互，请稍后重试。");
                var queuedAt=watch.ElapsedMilliseconds;
                // A timed-out request that has not begun must not mutate state later.
                // Already executing requests complete; clients reconcile uncertain outcomes.
                int state=0;
                using var registration=deadline.Token.Register(()=>{if(Interlocked.CompareExchange(ref state,2,0)==0)done.TrySetCanceled(deadline.Token);});
                loop.Post(()=>{
                    if(Interlocked.CompareExchange(ref state,1,0)!=0)return;
                    var execution=System.Diagnostics.Stopwatch.StartNew();
                    try{done.TrySetResult(handler(req) with{RequestId=req.RequestId});}
                    catch(Exception e){IpcLog.Write("host",req,"handler",execution.ElapsedMilliseconds,e);done.TrySetResult(new(false,e.Message,Array.Empty<LiveInstance>(),0,HostPid:Environment.ProcessId,RequestId:req.RequestId));}
                    finally{if(execution.ElapsedMilliseconds>=250||watch.ElapsedMilliseconds-queuedAt>=250)IpcLog.Write("host",req,"queue+execute",watch.ElapsedMilliseconds);}
                });
                var result=await done.Task.WaitAsync(deadline.Token).ConfigureAwait(false);
                stage="reply";
                var payload=System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(result,HostJson.Default.HostReply)+"\n");
                await pipe.WriteAsync(payload.AsMemory(),deadline.Token).ConfigureAwait(false);
                await pipe.FlushAsync(deadline.Token).ConfigureAwait(false);
            }
            catch(OperationCanceledException) when(shutdown.IsCancellationRequested){break;}
            catch(Exception e){IpcLog.Write("host",request,stage,watch.ElapsedMilliseconds,e);if(!shutdown.IsCancellationRequested)try{await Task.Delay(50,shutdown).ConfigureAwait(false);}catch(OperationCanceledException){break;}}
        }
    }
    public void Dispose(){cancel.Cancel();try{Task.WaitAll(workers,1000);}catch(AggregateException){}cancel.Dispose();}
}
#endif
