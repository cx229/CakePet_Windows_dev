using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
namespace CakePet.Core;
public static class ArchiveIO
{
    public static readonly JsonSerializerOptions Options=new(){WriteIndented=true,PropertyNameCaseInsensitive=true};
    public static JsonObject Read(string path)=>JsonNode.Parse(File.ReadAllText(path),documentOptions:new JsonDocumentOptions{CommentHandling=JsonCommentHandling.Skip,AllowTrailingCommas=true}) as JsonObject??throw new InvalidDataException("Invalid JSON object.");
    public static void Write(string path,string content)
    {
        var temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        try{File.WriteAllText(temp,content);File.Move(temp,path,true);}finally{if(File.Exists(temp))File.Delete(temp);}
    }
    public static void Extract(string archive,string root)
    {
        Directory.CreateDirectory(root);var prefix=Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
        using var zip=ZipFile.OpenRead(archive);if(zip.Entries.Count>50000)throw new InvalidDataException("ZIP entries exceed limit.");
        long total=0;var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(var entry in zip.Entries)
        {
            var name=entry.FullName.Replace('\\','/');if(name.StartsWith('/')||name.Contains(':')||name.Split('/').Any(x=>x==".."||x=="."))throw new InvalidDataException("Unsafe ZIP path.");
            if(((entry.ExternalAttributes>>16)&0xF000)==0xA000)throw new InvalidDataException("ZIP links are not supported.");
            var dest=Path.GetFullPath(Path.Combine(root,name));if(!dest.StartsWith(prefix,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Unsafe ZIP path.");
            if(name.EndsWith('/')){Directory.CreateDirectory(dest);continue;}
            if(!seen.Add(dest))throw new InvalidDataException("Duplicate ZIP entry.");
            total=checked(total+entry.Length);if(total>2_000_000_000L)throw new InvalidDataException("ZIP too large.");
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);using var input=entry.Open();using var output=new FileStream(dest,FileMode.CreateNew);
            var buffer=new byte[65536];long written=0;int n;while((n=input.Read(buffer,0,buffer.Length))>0){written+=n;if(written>entry.Length)throw new InvalidDataException("Invalid ZIP length.");output.Write(buffer,0,n);}
        }
    }
    public static void Zip(string path,Action<ZipArchive> writer)
    {
        var temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        try{using(var zip=ZipFile.Open(temp,ZipArchiveMode.Create))writer(zip);File.Move(temp,path,true);}finally{if(File.Exists(temp))File.Delete(temp);}
    }
    public static void Text(ZipArchive zip,string name,string content){using var w=new StreamWriter(zip.CreateEntry(name).Open());w.Write(content);}
    public static void Tree(ZipArchive zip,string root,string prefix="")
    {
        if((File.GetAttributes(root)&FileAttributes.ReparsePoint)!=0)throw new IOException("Linked directory.");
        foreach(var file in Directory.EnumerateFiles(root)){if((File.GetAttributes(file)&FileAttributes.ReparsePoint)!=0)throw new IOException("Linked file.");zip.CreateEntryFromFile(file,prefix+Path.GetFileName(file),CompressionLevel.Optimal);}
        foreach(var dir in Directory.EnumerateDirectories(root))Tree(zip,dir,prefix+Path.GetFileName(dir)+"/");
    }
    public static RoleSettings Overlay(RoleSettings original,JsonObject fields)
    {
        var node=JsonSerializer.SerializeToNode(original,Options)!.AsObject();
        foreach(var field in fields)
        {
            var key=node.Select(x=>x.Key).FirstOrDefault(k=>string.Equals(k,field.Key,StringComparison.OrdinalIgnoreCase));if(key==null||field.Value==null)continue;
            var old=node[key]?.DeepClone();node[key]=field.Value.DeepClone();
            try{_ = node.Deserialize<RoleSettings>(Options)??throw new JsonException();}
            catch(JsonException){node[key]=old;}catch(NotSupportedException){node[key]=old;}
        }
        return SettingLimits.Clamp((node.Deserialize<RoleSettings>(Options)??original).Normalize());
    }
}
