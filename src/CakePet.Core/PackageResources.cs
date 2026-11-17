using System.Text.Json.Nodes;
namespace CakePet.Core;
public sealed partial class PackageEditor
{
    private static string SafePath(string root,string relative)
    {
        if(Path.IsPathRooted(relative)||relative.Contains(':')||relative.Replace('\\','/').Split('/').Any(x=>x==".."||x=="."))throw new InvalidDataException("无效资源路径");
        var path=Path.GetFullPath(Path.Combine(root,relative));var prefix=Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
        if(path!=Path.GetFullPath(root)&&!path.StartsWith(prefix,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("资源越界");
        for(var p=path; p!=null; p=Path.GetDirectoryName(p)){if((File.Exists(p)||Directory.Exists(p))&&(File.GetAttributes(p)&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("不支持链接资源");if(string.Equals(p,root,StringComparison.OrdinalIgnoreCase))break;}
        return path;
    }
    private static string Segment(string name){name=name.Trim();if(name.Length==0||name is "." or ".."||name.IndexOfAny(Path.GetInvalidFileNameChars())>=0||name.Contains('/')||name.Contains('\\')||name.Contains(':')||name.EndsWith('.'))throw new InvalidDataException("无效名称");return name;}
    public string ResourcePath(string relative)=>SafePath(Root,relative);
    private static string Join(string folder,string name)=>folder.Length==0?name:folder+"/"+name;
    public IReadOnlyList<string> Folders(string parent)
    {
        var dir=ResourcePath(parent);if(!Directory.Exists(dir))return Array.Empty<string>();
        var actual=Directory.EnumerateDirectories(dir).Where(x=>(File.GetAttributes(x)&FileAttributes.ReparsePoint)==0&&!Path.GetFileName(x).StartsWith(".")).Select(Path.GetFileName).OfType<string>().OrderBy(x=>x,StringComparer.OrdinalIgnoreCase).ToArray();
        JsonObject? node=Document["resourceOrder"] as JsonObject;foreach(var part in parent.Split('/',StringSplitOptions.RemoveEmptyEntries))node=node?[part] as JsonObject;
        return (node?.Select(x=>x.Key).Where(actual.Contains)??Enumerable.Empty<string>()).Concat(actual).Distinct().ToArray();
    }
    public void SyncFolders()
    {
        JsonObject Build(string parent){var result=new JsonObject();foreach(var name in Folders(parent))result[name]=Build(Join(parent,name));return result;}
        Document["resourceOrder"]=Build("");
    }
    public string NewFolder(string parent,string name){var path=Join(parent,Segment(name));var dir=ResourcePath(path);if(Directory.Exists(dir)||File.Exists(dir))throw new IOException("文件夹已存在");Directory.CreateDirectory(dir);SyncFolders();return path;}
    public void MoveFolderOrder(string parent,string name,int delta)
    {
        SyncFolders();var node=Document["resourceOrder"]!.AsObject();foreach(var part in parent.Split('/',StringSplitOptions.RemoveEmptyEntries))node=node[part]!.AsObject();var keys=node.Select(x=>x.Key).ToList();var i=keys.IndexOf(name);var j=i+delta;if(i<0||j<0||j>=keys.Count)return;(keys[i],keys[j])=(keys[j],keys[i]);var children=keys.Select(k=>(k,node[k]!.DeepClone())).ToArray();node.Clear();foreach(var (key,value) in children)node[key]=value;
    }
    public string CopyImageFrom(string sourceRoot,string relative,string folder)
    {
        var source=SafePath(sourceRoot,relative);return CopyImageFile(source,folder);
    }
    public string CopyImageFile(string source,string folder)
    {
        if(!File.Exists(source)||(File.GetAttributes(source)&FileAttributes.ReparsePoint)!=0)throw new IOException("图片不存在或不可用");
        var dir=ResourcePath(folder);Directory.CreateDirectory(dir);var name=Segment(Path.GetFileName(source));var stem=Path.GetFileNameWithoutExtension(name);var ext=Path.GetExtension(name);
        if(!new[]{".png",".jpg",".jpeg",".bmp",".gif",".webp"}.Contains(ext,StringComparer.OrdinalIgnoreCase))throw new InvalidDataException("不支持的图片格式");
        for(int i=0;;i++){var rel=Join(folder,i==0?name:stem+"-"+(i+1)+ext);var dest=ResourcePath(rel);if(File.Exists(dest)){if(File.ReadAllBytes(source).AsSpan().SequenceEqual(File.ReadAllBytes(dest)))return rel;continue;}if(Directory.Exists(dest))continue;File.Copy(source,dest);SyncFolders();return rel;}
    }
    public IReadOnlyList<string> ImageReferences(string relative)
    {
        var result=new HashSet<string>();void Scan(JsonObject modes){foreach(var pair in modes)if(pair.Value?["frames"] is JsonArray frames)for(int i=0;i<frames.Count;i++)if(string.Equals(frames[i]?["res"]?.ToString().Replace('\\','/'),relative.Replace('\\','/'),StringComparison.OrdinalIgnoreCase))result.Add(pair.Key+" / 帧 "+(i+1));}
        Scan(Modes);Scan(ArchiveIO.Read(Path.Combine(Root,"modes.jsonc"))["modes"] as JsonObject??new JsonObject());return result.ToArray();
    }
    public void ReplaceImageReferences(string old,string replacement)
    {
        if(!File.Exists(ResourcePath(replacement)))throw new IOException("替换图片不存在");
        foreach(var pair in Modes)if(pair.Value?["frames"] is JsonArray frames)foreach(var frame in frames.OfType<JsonObject>())if(string.Equals(frame["res"]?.ToString().Replace('\\','/'),old.Replace('\\','/'),StringComparison.OrdinalIgnoreCase))frame["res"]=replacement;
    }
    public IReadOnlyList<string> DeleteImpact(string id)
    {
        var preview=Preview();var affected=new List<string>();foreach(var path in FunctionPaths()){var pool=preview.Candidates(path);if(pool.Contains(id)&&pool.Count==1)affected.Add(path);}
        return affected;
    }
    public string ImportText(PackageEditor source,string id)
    {
        var original=source.Thinkings[id] as JsonObject??throw new InvalidDataException("文本不存在");
        var text=original["text"]?.ToString()??"";var same=Thinkings.FirstOrDefault(x=>x.Value?["text"]?.ToString()==text);if(same.Key!=null)return same.Key;
        var name=id;for(int i=2;Thinkings.ContainsKey(name);i++)name=id+"-"+i;
        var copy=original.DeepClone().AsObject();copy.Remove("groups");Thinkings[name]=copy;return name;
    }
    public string ImportAnimation(PackageEditor source,string id,string folder)
    {
        var map=new Dictionary<string,string>();var copied=new Dictionary<string,string>();var textMap=new Dictionary<string,string>();var pending=new Dictionary<string,JsonObject>();var newModes=new List<string>();var newTexts=Thinkings.Select(x=>x.Key).ToHashSet();
        string Visit(string key)
        {
            if(map.TryGetValue(key,out var existing))return existing;
            var original=source.Mode(key);var name=key;for(int i=2;Modes.ContainsKey(name)||pending.ContainsKey(name);i++)name=key+"-"+i;
            map[key]=name;var node=original.DeepClone().AsObject();pending[name]=node;node.Remove("groups");
            foreach(var frame in (node["frames"] as JsonArray??throw new InvalidDataException("动画缺少帧")).OfType<JsonObject>()){var res=frame["res"]?.ToString()??throw new InvalidDataException("帧缺少图片");if(!copied.TryGetValue(res,out var path))copied[res]=path=CopyImageFrom(source.Root,res,folder);frame["res"]=path;}
            if(node["thinking"]?["pool"] is JsonObject pool){var entries=pool.ToArray();pool.Clear();foreach(var pair in entries){if(!source.Thinkings.ContainsKey(pair.Key))continue;if(!textMap.TryGetValue(pair.Key,out var target))textMap[pair.Key]=target=ImportText(source,pair.Key);pool[target]=pair.Value?.DeepClone();}}
            var next=node["nextMode"]?.ToString();if(!string.IsNullOrWhiteSpace(next)&&next!="null"){if(source.Modes.ContainsKey(next))node["nextMode"]=Visit(next);else node.Remove("nextMode");}return name;
        }
        try{var result=Visit(id);foreach(var pair in pending){Modes[pair.Key]=pair.Value;newModes.Add(pair.Key);}return result;}
        catch{foreach(var key in newModes)Modes.Remove(key);foreach(var key in Thinkings.Select(x=>x.Key).Where(x=>!newTexts.Contains(x)).ToArray())Thinkings.Remove(key);throw;}
    }
}
