using System.Text.Json;
namespace CakePet.Core;

public readonly record struct Interval(int Min, int Max)
{
    public int Sample(Random random) => (int)random.NextInt64(Min, (long)Max + 1);
}
public enum LoopKind { Off, Timed, Infinite }
public sealed record Frame(string Resource, Interval Delay, int Dx, int Dy, int AnchorX, int? AnchorY, string Align, string Transform);
public sealed record Thinking(string Policy, IReadOnlyDictionary<string, Interval> Pool)
{
    public static Thinking Empty { get; } = new("off", new Dictionary<string, Interval>());
}
public sealed record Mode(string Id, IReadOnlyList<Frame> Frames, LoopKind Loop, Interval? Duration, bool FinishCycle, string? Next, Thinking? Thinking);
public sealed record Function(IReadOnlyList<string> Modes, Thinking Thinking);

public sealed class RolePackage
{
    public const string ContractVersion = "1.12";
    public string Root { get; }
    public string Id { get; }
    public string Name { get; }
    public string Version { get; }
    public int AnchorRes { get; }
    public int DecodeTarget { get; }
    public IReadOnlyDictionary<string, Mode> Modes { get; }
    public IReadOnlyDictionary<string, Function> Functions { get; }
    public IReadOnlyDictionary<string, string> Texts { get; }
    public IReadOnlyList<string> Warnings { get; }
    private readonly Dictionary<string, bool> playable = new(StringComparer.Ordinal);

    private RolePackage(string root, JsonElement doc, Func<string,bool> imageReady)
    {
        Root = root;
        Id = Str(doc, "id", "unknown"); Name = Str(doc, "name", Id);
        Version = Str(doc, "version", ""); AnchorRes = Math.Max(1, Int(doc, "anchorRes", 128));
        DecodeTarget = Math.Max(0, Int(doc, "decodeTarget", 256));
        var warnings = new List<string>();
        var modes = new Dictionary<string, Mode>(StringComparer.Ordinal);
        var functions = new Dictionary<string, Function>(StringComparer.Ordinal);
        var texts = new Dictionary<string, string>(StringComparer.Ordinal);
        Modes = modes; Functions = functions; Texts = texts; Warnings = warnings;
        if (Prop(doc,"modes").ValueKind != JsonValueKind.Object || Prop(doc,"functions").ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Missing modes or functions object.");
        foreach (var entry in Prop(doc,"modes").EnumerateObject())
        {
            try
            {
                var m = entry.Value;
                var loop = Str(m,"loopMode","") switch {
                    "off" => LoopKind.Off, "timed" => LoopKind.Timed, "infinite" => LoopKind.Infinite,
                    _ => Bool(m,"loop") ? (Prop(m,"duration").ValueKind == JsonValueKind.Object ? LoopKind.Timed : LoopKind.Infinite) : LoopKind.Off };
                Interval? duration = loop == LoopKind.Timed ? Range(Prop(m,"duration")) : null;
                var frames = Prop(m,"frames").EnumerateArray().Select(f => {
                    var res = Str(f,"res","");
                    var path = ResolveAsset(res);
                    if (!imageReady(path)) throw new InvalidDataException("Missing or unreadable image: "+res);
                    return new Frame(res, Range(Prop(f,"delay")), Int(f,"dx",0), Int(f,"dy",0), Int(f,"anchorX",-1),
                        Prop(f,"anchorY").TryInt(), Str(f,"verticalAlign","top"), Str(f,"transform","none"));
                }).ToArray();
                if (frames.Length == 0) throw new InvalidDataException("No frames.");
                var next = Str(m,"nextMode","");
                modes.Add(entry.Name,new Mode(entry.Name,frames,loop,duration,Bool(m,"finishCycle"),
                    string.IsNullOrWhiteSpace(next) || next == "null" ? null : next, ReadThinking(Prop(m,"thinking"),warnings)));
            }
            catch (Exception e) when (e is InvalidDataException or InvalidOperationException or ArgumentException or IOException or UnauthorizedAccessException)
            { warnings.Add(entry.Name+": "+e.Message); }
        }
        void Walk(JsonElement node,string path)
        {
            if (node.ValueKind != JsonValueKind.Object) return;
            var pool = Prop(node,"modes");
            if (pool.ValueKind == JsonValueKind.Array)
            {
                functions[path] = new Function(pool.EnumerateArray().Where(x=>x.ValueKind==JsonValueKind.String)
                    .Select(x=>x.GetString()!).ToArray(),ReadThinking(Prop(node,"thinking"),warnings) ?? Thinking.Empty);
                return;
            }
            foreach(var child in node.EnumerateObject()) Walk(child.Value,path.Length==0?child.Name:path+"/"+child.Name);
        }
        Walk(Prop(doc,"functions"),"");
        var t = Prop(doc,"thinkings");
        if(t.ValueKind==JsonValueKind.Object) foreach(var entry in t.EnumerateObject())
        {
            var value=Str(entry.Value,"text","");
            if(!string.IsNullOrWhiteSpace(value)) texts[entry.Name]=value;
        }
        foreach(var key in modes.Keys) playable[key]=CheckChain(key);
        if(Candidates("standby/pool").Count==0) throw new InvalidDataException("待机池至少需要一个可播放的完整动画");
    }
    public static RolePackage Load(string root, Func<string,bool>? imageReady=null)
    {
        root=Path.GetFullPath(root);
        using var doc=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"modes.jsonc")),new JsonDocumentOptions { AllowTrailingCommas=true, CommentHandling=JsonCommentHandling.Skip });
        return new RolePackage(root,doc.RootElement,imageReady ?? (p=>File.Exists(p)&&new FileInfo(p).Length>0));
    }
    public string ResolveAsset(string resource)
    {
        if(string.IsNullOrWhiteSpace(resource)||Path.IsPathRooted(resource)||resource.Contains(':')) throw new InvalidDataException("Invalid resource path.");
        var path=Path.GetFullPath(Path.Combine(Root,resource));
        var rel=Path.GetRelativePath(Root,path);
        if(rel==".."||rel.StartsWith(".."+Path.DirectorySeparatorChar)||Path.IsPathRooted(rel)) throw new InvalidDataException("Resource escapes package.");
        // Reject reparse points/symlinks in package descendants.
        for(var cursor=path; !string.Equals(cursor,Root,StringComparison.OrdinalIgnoreCase); cursor=Path.GetDirectoryName(cursor)!)
            if((File.Exists(cursor)||Directory.Exists(cursor)) && (File.GetAttributes(cursor)&FileAttributes.ReparsePoint)!=0)
                throw new InvalidDataException("Linked resources are not supported.");
        return path;
    }
    private bool CheckChain(string id)
    {
        var seen=new HashSet<string>(StringComparer.Ordinal);
        string? current=id;
        while(current!=null && seen.Add(current))
        {
            if(!Modes.TryGetValue(current,out var mode)) return false;
            if(mode.Loop==LoopKind.Infinite) return true;
            current=mode.Next;
        }
        return true;
    }
    public IReadOnlyList<string> Candidates(string path) => Functions.TryGetValue(path,out var f)
        ? f.Modes.Where(x=>playable.GetValueOrDefault(x)).ToArray() : Array.Empty<string>();
    public Thinking ResolveThinking(string mode,string function) =>
        Modes.TryGetValue(mode,out var m)&&m.Thinking is { Policy: not "inherit" } t ? t :
        Functions.TryGetValue(function,out var f)?f.Thinking:Thinking.Empty;
    private static Thinking? ReadThinking(JsonElement e,List<string> warnings)
    {
        if(e.ValueKind!=JsonValueKind.Object) return null;
        var policy=Str(e,"policy","inherit");
        if(policy is not ("inherit" or "off" or "hold" or "cycle")) return null;
        var pool=new Dictionary<string,Interval>();
        var p=Prop(e,"pool");
        if(p.ValueKind==JsonValueKind.Object) foreach(var v in p.EnumerateObject())
            try { pool[v.Name]=Range(v.Value); } catch(InvalidDataException) { warnings.Add("Invalid thinking interval: "+v.Name); }
        return new Thinking(policy,pool);
    }
    private static Interval Range(JsonElement e)
    {
        int? min=null,max=null;
        if(e.ValueKind==JsonValueKind.Number) min=max=e.TryInt();
        else if(e.ValueKind==JsonValueKind.Array&&e.GetArrayLength()==2){ min=e[0].TryInt();max=e[1].TryInt(); }
        else if(e.ValueKind==JsonValueKind.Object){min=Prop(e,"min").TryInt();max=Prop(e,"max").TryInt();}
        if(min is null||max is null||min<=0||max<min) throw new InvalidDataException("Invalid time interval.");
        return new Interval(min.Value,max.Value);
    }
    private static JsonElement Prop(JsonElement e,string k)=>e.ValueKind==JsonValueKind.Object&&e.TryGetProperty(k,out var v)?v:default;
    private static string Str(JsonElement e,string k,string fallback)=>Prop(e,k) is { ValueKind: JsonValueKind.String } v?v.GetString()!:fallback;
    private static int Int(JsonElement e,string k,int fallback)=>Prop(e,k).TryInt()??fallback;
    private static bool Bool(JsonElement e,string k)=>Prop(e,k).ValueKind==JsonValueKind.True;
}
internal static class JsonExtensions
{
    internal static int? TryInt(this JsonElement e)=>e.ValueKind==JsonValueKind.Number&&e.TryGetInt32(out var v)?v:null;
}
