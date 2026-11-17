using System.IO;
using CakePet.Core;
namespace CakePet.Windows;

internal sealed class CatalogStorage
{
    private readonly string root=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"CakePet");
    public string FilePath=>Path.Combine(root,"catalog.json");
    public RoleCatalog Catalog {get;}
    public CatalogStorage(string bundled)
    {
        Catalog=RoleCatalog.Load(FilePath);
        if(Catalog.Packs.Count==0&&File.Exists(Path.Combine(bundled,"modes.jsonc")))
        {
            var p=Import(bundled);Catalog.AddRole(p.Id,p.Name);Save();
        }
    }
    public void Save()=>Catalog.Save(FilePath);
    public string Resolve(PackEntry p)=>Path.Combine(root,"packs",p.Directory);
    public PackEntry Import(string source)
    {
        var pack=RolePackage.Load(source,WpfImageCache.IsReadable);
        var id=Guid.NewGuid();var dest=Path.Combine(root,"packs",id.ToString("N"));
        Directory.CreateDirectory(dest);
        try
        {
            Copy(source,dest);
            RolePackage.Load(dest,WpfImageCache.IsReadable);
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
