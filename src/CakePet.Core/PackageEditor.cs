using System.Text.Json.Nodes;
namespace CakePet.Core;

// The editable document preserves unknown fields and ordering. Only modes.jsonc is rewritten.
public sealed class PackageEditor
{
    public string Root {get;}
    public JsonObject Document {get;}
    public JsonObject Modes=>Object("modes");
    public JsonObject Functions=>Object("functions");
    public JsonObject Thinkings=>Object("thinkings");
    public PackageEditor(string root){Root=Path.GetFullPath(root);Document=ArchiveIO.Read(Path.Combine(Root,"modes.jsonc"));}
    private JsonObject Object(string key){if(Document[key] is not JsonObject o){o=new JsonObject();Document[key]=o;}return o;}
    public JsonObject Mode(string id)=>Modes[id] as JsonObject??throw new InvalidDataException("Unknown animation.");
    public JsonArray Frames(string id)=>Mode(id)["frames"] as JsonArray??throw new InvalidDataException("Missing frames.");
    public JsonObject Function(string path)
    {
        var node=Functions;foreach(var part in path.Split('/',StringSplitOptions.RemoveEmptyEntries)){if(node[part] is not JsonObject child){child=new JsonObject();node[part]=child;}node=child;}
        if(node["modes"] is not JsonArray)node["modes"]=new JsonArray();return node;
    }
    public IEnumerable<string> FunctionPaths()
    {
        IEnumerable<string> Walk(JsonObject o,string prefix)
        {foreach(var p in o){if(p.Value is not JsonObject child)continue;var path=prefix.Length==0?p.Key:prefix+"/"+p.Key;if(child["modes"] is JsonArray)yield return path;else foreach(var sub in Walk(child,path))yield return sub;}}
        return Walk(Functions,"");
    }
    public void SetPool(string path,IEnumerable<string> modes)=>Function(path)["modes"]=new JsonArray(modes.Distinct().Select(x=>(JsonNode?)JsonValue.Create(x)).ToArray());
    public void RenameMode(string old,string name)
    {
        if(string.IsNullOrWhiteSpace(name)||Modes.ContainsKey(name))throw new InvalidDataException("动画名称为空或重复。");
        var node=Mode(old).DeepClone();Modes.Remove(old);Modes[name]=node;
        foreach(var mode in Modes.Select(x=>x.Value).OfType<JsonObject>())if(mode["nextMode"]?.ToString()==old)mode["nextMode"]=name;
        foreach(var path in FunctionPaths().ToArray()){var pool=(JsonArray)Function(path)["modes"]!;for(int i=0;i<pool.Count;i++)if(pool[i]?.ToString()==old)pool[i]=JsonValue.Create(name);}
    }
    public void DeleteMode(string id)
    {
        Modes.Remove(id);foreach(var mode in Modes.Select(x=>x.Value).OfType<JsonObject>())if(mode["nextMode"]?.ToString()==id)mode.Remove("nextMode");
        foreach(var path in FunctionPaths().ToArray()){var pool=(JsonArray)Function(path)["modes"]!;for(int i=pool.Count-1;i>=0;i--)if(pool[i]?.ToString()==id)pool.RemoveAt(i);}
    }
    public void AddMode(string name,string image)
    {
        if(string.IsNullOrWhiteSpace(name)||Modes.ContainsKey(name))throw new InvalidDataException("动画名称为空或重复。");
        Modes[name]=new JsonObject{["loopMode"]="off",["frames"]=new JsonArray(new JsonObject{["res"]=image,["delay"]=100})};
    }
    public void DeleteThinking(string id)
    {
        Thinkings.Remove(id);
        void Clean(JsonObject node){if(node["thinking"] is JsonObject t&&t["pool"] is JsonObject p)p.Remove(id);}
        foreach(var mode in Modes.Select(x=>x.Value).OfType<JsonObject>())Clean(mode);
        foreach(var path in FunctionPaths().ToArray())Clean(Function(path));
    }
    public bool ImageReferenced(string path)=>References(Modes,path)||References(ArchiveIO.Read(Path.Combine(Root,"modes.jsonc"))["modes"] as JsonObject??new JsonObject(),path);
    private static bool References(JsonObject modes,string path)=>modes.Select(x=>x.Value).OfType<JsonObject>().SelectMany(x=>x["frames"] as JsonArray??new JsonArray()).OfType<JsonObject>().Any(x=>string.Equals(x["res"]?.ToString().Replace('\\','/'),path.Replace('\\','/'),StringComparison.OrdinalIgnoreCase));
    public IReadOnlyList<string> Images()=>Directory.EnumerateFiles(Root,"*",SearchOption.AllDirectories).Where(x=>new[]{".png",".jpg",".jpeg",".webp",".bmp",".gif"}.Contains(Path.GetExtension(x),StringComparer.OrdinalIgnoreCase)).Select(x=>Path.GetRelativePath(Root,x).Replace('\\','/')).OrderBy(x=>x).ToArray();
    public IEnumerable<(string Label,JsonObject Node)> ThinkingReferences(string id)
    {
        foreach(var pair in Modes)if(pair.Value is JsonObject m&&m["thinking"]?["pool"] is JsonObject p&&p.ContainsKey(id))yield return ("动画 / "+pair.Key,m);
        foreach(var path in FunctionPaths()) {var f=Function(path);if(f["thinking"]?["pool"] is JsonObject p&&p.ContainsKey(id))yield return ("功能 / "+path,f);}
    }
    public RolePackage Preview()=>RolePackage.FromDraft(Root,Document.ToJsonString(),File.Exists);
    public IReadOnlyList<string> Groups(bool thinking)=> (Document[thinking?"thinkingGroups":"modeGroups"] as JsonArray??new JsonArray()).Select(x=>x?.ToString()??"").Where(x=>x.Length>0).Distinct().ToArray();
    public IReadOnlyList<string> Membership(bool thinking,string id)=>((thinking?Thinkings:Modes)[id]?["groups"] as JsonArray??new JsonArray()).Select(x=>x?.ToString()??"").ToArray();
    public void SetMembership(bool thinking,string id,IEnumerable<string> groups){var item=(thinking?Thinkings:Modes)[id]!.AsObject();item["groups"]=new JsonArray(groups.Distinct().Select(x=>(JsonNode?)JsonValue.Create(x)).ToArray());}
    private void WriteGroups(bool thinking,IEnumerable<string> groups)=>Document[thinking?"thinkingGroups":"modeGroups"]=new JsonArray(groups.Select(x=>(JsonNode?)JsonValue.Create(x)).ToArray());
    public void AddGroup(bool thinking,string name){name=name.Trim();var groups=Groups(thinking);if(name.Length==0||name=="未分组"||groups.Contains(name))throw new InvalidDataException("分组名称为空或重复。");WriteGroups(thinking,groups.Append(name));}
    public void DissolveGroup(bool thinking,string name){WriteGroups(thinking,Groups(thinking).Where(x=>x!=name));foreach(var id in (thinking?Thinkings:Modes).Select(x=>x.Key).ToArray())if(Membership(thinking,id).Contains(name))SetMembership(thinking,id,Membership(thinking,id).Where(x=>x!=name));}
    public void MoveGroup(bool thinking,string name,int direction){var groups=Groups(thinking).ToList();var i=groups.IndexOf(name);var j=i+direction;if(i<0||j<0||j>=groups.Count)return;(groups[i],groups[j])=(groups[j],groups[i]);WriteGroups(thinking,groups);}
    public IEnumerable<(string Name,string[] Ids)> Grouped(bool thinking)
    {
        var groups=Groups(thinking);var ids=(thinking?Thinkings:Modes).Select(x=>x.Key).ToArray();
        foreach(var group in groups)yield return (group,ids.Where(id=>Membership(thinking,id).Contains(group)).ToArray());
        yield return ("未分组",ids.Where(id=>!Membership(thinking,id).Any(groups.Contains)).ToArray());
    }
    public RolePackage Save(Func<string,bool>? imageReady=null)
    {
        var path=Path.Combine(Root,"modes.jsonc");var old=File.ReadAllText(path);Document["version"]=RolePackage.ContractVersion;
        ArchiveIO.Write(path,Document.ToJsonString(ArchiveIO.Options));
        try{return RolePackage.Load(Root,imageReady);}catch{ArchiveIO.Write(path,old);throw;}
    }
}
