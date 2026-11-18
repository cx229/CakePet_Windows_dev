using System.IO;
using CakePet.Core;
namespace CakePet.Windows;

internal sealed partial class CatalogStorage
{
    private readonly string root;
    public string FilePath=>Path.Combine(root,"catalog.json");
    public RoleCatalog Catalog {get;private set;}
    private readonly bool remote;
    private string baseline="";
    internal long Revision {get;private set;}
    public CatalogStorage(string bundled,bool remote=false,string? dataRoot=null)
    {
        root=dataRoot??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"CakePet");
        this.remote=remote;
        Catalog=remote?RoleCatalog.Parse(HostConnection.Call(new("snapshot")).Catalog??throw new IOException("Missing catalog")):RoleCatalog.Load(FilePath);
        baseline=System.Text.Json.JsonSerializer.Serialize(Catalog,CoreJson.Default.RoleCatalog);
        if(remote)return;
        if(!File.Exists(FilePath)&&Catalog.Packs.Count==0&&File.Exists(Path.Combine(bundled,"modes.jsonc")))
        {
            var p=Import(bundled);Catalog.AddRole(p.Id,p.Name);Save();
        }
    }
    public void Save()=>Commit(Catalog);
    public void Commit(RoleCatalog value)
    {
        Revision++;
        if(!remote){value.Save(FilePath);Catalog=value;return;}
        var result=HostConnection.Call(new("save-catalog",Baseline:baseline,Catalog:System.Text.Json.JsonSerializer.Serialize(value,CoreJson.Default.RoleCatalog)));
        Adopt(result.Catalog??throw new IOException("Missing catalog"));
    }
    public void Adopt(string json){if(json==baseline)return;Catalog=RoleCatalog.Parse(json);baseline=System.Text.Json.JsonSerializer.Serialize(Catalog,CoreJson.Default.RoleCatalog);Revision++;}
    public void Reload()=>Adopt(remote?HostConnection.Call(new("snapshot")).Catalog??throw new IOException("Missing catalog"):File.ReadAllText(FilePath));
    public string Resolve(PackEntry p){if(!Guid.TryParseExact(p.Directory,"N",out _))throw new InvalidDataException("Invalid package directory.");return Path.Combine(root,"packs",p.Directory);}
    public PackEntry Import(string source)
    {
        var pack=RolePackage.Load(source,GdiImageCache.IsReadable);
        var id=Guid.NewGuid();var dest=Path.Combine(root,"packs",id.ToString("N"));
        Directory.CreateDirectory(dest);
        try
        {
            Copy(source,dest);
            RolePackage.Load(dest);
            var entry=new PackEntry(id,pack.Name,id.ToString("N"));Catalog.Packs.Add(entry);
            try{Save();}catch{Catalog.Packs.Remove(entry);throw;}return entry;
        }
        catch{Directory.Delete(dest,true);throw;}
    }
    private static void Copy(string source,string dest)
    {
        if((File.GetAttributes(source)&FileAttributes.ReparsePoint)!=0)throw new IOException("不支持链接目录");
        foreach(var file in Directory.EnumerateFiles(source))
        {
            if((File.GetAttributes(file)&FileAttributes.ReparsePoint)!=0)throw new IOException("不支持链接文件");
            File.Copy(file,Path.Combine(dest,Path.GetFileName(file)));
        }
        foreach(var folder in Directory.EnumerateDirectories(source))
        {
            if((File.GetAttributes(folder)&FileAttributes.ReparsePoint)!=0)throw new IOException("不支持链接目录");
            var child=Path.Combine(dest,Path.GetFileName(folder));Directory.CreateDirectory(child);Copy(folder,child);
        }
    }
    public void RemovePack(PackEntry entry)
    {
        if(Catalog.Roles.Any(r=>r.PackId==entry.Id))throw new InvalidOperationException("素材包仍被角色使用");
        Catalog.Packs.Remove(entry);try{Save();}catch{Catalog.Packs.Add(entry);throw;}
        var path=Resolve(entry);if(Directory.Exists(path))Directory.Delete(path,true);
    }
}
