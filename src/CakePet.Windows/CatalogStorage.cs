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
#if !NATIVE_HOST
    internal bool AsyncSaveInFlight {get;set;}
#endif
    public CatalogStorage(string bundled,bool remote=false,string? dataRoot=null,string? initialCatalog=null)
    {
        root=dataRoot??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"CakePet");
        this.remote=remote;
        Catalog=remote?RoleCatalog.Parse(initialCatalog??throw new ArgumentNullException(nameof(initialCatalog))):RoleCatalog.Load(FilePath);
        baseline=System.Text.Json.JsonSerializer.Serialize(Catalog,CoreJson.Default.RoleCatalog);
        if(remote)return;
        if(!File.Exists(FilePath)&&Catalog.Packs.Count==0&&File.Exists(Path.Combine(bundled,"modes.jsonc")))
        {
            var p=Import(bundled);Catalog.AddRole(p.Id,p.Name);Save();
        }
    }
    public void Save()=>Commit(Catalog);
#if !NATIVE_HOST
    internal HostRequest CaptureSave()=>new("save-catalog",Baseline:baseline,Catalog:System.Text.Json.JsonSerializer.Serialize(Catalog,CoreJson.Default.RoleCatalog));
    internal bool CompleteSave(HostRequest request,HostReply reply)
    {
        string desired=request.Catalog??throw new InvalidDataException();
        string live=reply.Catalog??throw new IOException("缺少配置保存结果。");
        string current=System.Text.Json.JsonSerializer.Serialize(Catalog,CoreJson.Default.RoleCatalog);
        if(current==desired){Adopt(live);return false;}
        // Rebase only the edits made after the request was captured, onto the confirmed host state.
        var merged=CatalogMerge.Merge(desired,current,live);
        Catalog=merged;baseline=System.Text.Json.JsonSerializer.Serialize(RoleCatalog.Parse(live),CoreJson.Default.RoleCatalog);Revision++;
        return System.Text.Json.JsonSerializer.Serialize(merged,CoreJson.Default.RoleCatalog)!=baseline;
    }
    internal void Rebase(string live)
    {
        string desired=System.Text.Json.JsonSerializer.Serialize(Catalog,CoreJson.Default.RoleCatalog);
        Catalog=CatalogMerge.Merge(baseline,desired,live);
        baseline=System.Text.Json.JsonSerializer.Serialize(RoleCatalog.Parse(live),CoreJson.Default.RoleCatalog);Revision++;
    }
#endif
    public void Commit(RoleCatalog value)
    {
#if !NATIVE_HOST
        if(AsyncSaveInFlight)throw new InvalidOperationException("设置正在保存，请稍后重试。");
#endif
        Revision++;
        if(!remote){value.Save(FilePath);Catalog=value;return;}
        var result=HostConnection.Call(new("save-catalog",Baseline:baseline,Catalog:System.Text.Json.JsonSerializer.Serialize(value,CoreJson.Default.RoleCatalog)));
        Adopt(result.Catalog??throw new IOException("Missing catalog"));
    }
    internal bool AdoptIfChanged(string json){if(json==baseline)return false;Adopt(json);return true;}
    public void Adopt(string json){if(json==baseline&&System.Text.Json.JsonSerializer.Serialize(Catalog,CoreJson.Default.RoleCatalog)==baseline)return;Catalog=RoleCatalog.Parse(json);baseline=System.Text.Json.JsonSerializer.Serialize(Catalog,CoreJson.Default.RoleCatalog);Revision++;}
    public void Reload()=>Adopt(remote?HostConnection.Call(new("snapshot")).Catalog??throw new IOException("Missing catalog"):File.ReadAllText(FilePath));
    public string Resolve(PackEntry p){if(!Guid.TryParseExact(p.Directory,"N",out _))throw new InvalidDataException("Invalid package directory.");return Path.Combine(root,"packs",p.Directory);}
    public PackEntry Import(string source)
    {
#if !NATIVE_HOST
        if(AsyncSaveInFlight)throw new InvalidOperationException("设置正在保存，请稍后重试。");
#endif
        var pack=RolePackage.Load(source,GdiImageCache.IsReadable);
        var id=Guid.NewGuid();var dest=Path.Combine(root,"packs",id.ToString("N"));
        Directory.CreateDirectory(dest);
        var entry=new PackEntry(id,pack.Name,id.ToString("N"));
        bool uncertain=false;
        try
        {
            Copy(source,dest);
            RolePackage.Load(dest);
            if(!remote)
            {
                Catalog.Packs.Add(entry);
                try{Save();}catch{Catalog.Packs.Remove(entry);throw;}
                return entry;
            }
            Revision++;
            // A retry uses exactly the same ID. A delayed first request cannot create a duplicate.
            var request=new HostRequest("register-pack",Pack:entry);
            for(int attempt=0;attempt<2;attempt++)
            {
                try
                {
                    var result=HostConnection.Call(request);
                    uncertain=true;
                    Adopt(result.Catalog??throw new IOException("缺少素材包注册结果。"));
                    if(!Catalog.Packs.Contains(entry))throw new IOException("素材包注册结果未确认。");
                    return entry;
                }
                catch(HostRejectedException) when(!uncertain){throw;}
                catch(Exception e)
                {
                    uncertain=true;
                    if(attempt==0)continue;
                    // The host may have committed before its reply was lost.
                    try{Reload();if(Catalog.Packs.Contains(entry))return entry;}catch(Exception){}
                    throw new IOException("素材包已复制，但常驻进程尚未确认导入。资源已保留，请重新打开设置查看素材包列表，避免重复导入。",e);
                }
            }
            throw new InvalidOperationException("素材包注册未完成。");
        }
        catch
        {
            if(!uncertain&&Directory.Exists(dest))Directory.Delete(dest,true);
            throw;
        }
    }
#if !NATIVE_HOST
    internal async Task<PackEntry> ImportAsync(string source)
    {
        var entry=await Task.Run(()=>PrepareImport(source));
        bool uncertain=false;
        try
        {
            var request=new HostRequest("register-pack",Pack:entry);
            for(int attempt=0;attempt<2;attempt++)
            {
                try
                {
                    var result=await HostConnection.CallAsync(request);
                    uncertain=true;
                    var live=result.Catalog??throw new IOException("缺少素材包注册结果。");
                    if(!RoleCatalog.Parse(live).Packs.Contains(entry))throw new IOException("素材包注册结果未确认。");
                    Rebase(live);return entry;
                }
                catch(HostRejectedException) when(!uncertain){throw;}
                catch(Exception e)
                {
                    uncertain=true;if(attempt==0)continue;
                    try
                    {
                        var snapshot=await HostConnection.CallAsync(new("snapshot"));
                        var live=snapshot.Catalog??throw new IOException();
                        if(RoleCatalog.Parse(live).Packs.Contains(entry)){Rebase(live);return entry;}
                    }catch(Exception){}
                    throw new IOException("素材包已复制，但导入尚未确认，资源已保留。",e);
                }
            }
            throw new InvalidOperationException("素材包注册未完成。");
        }
        catch{if(!uncertain&&Directory.Exists(Resolve(entry)))Directory.Delete(Resolve(entry),true);throw;}
    }
    internal bool IsSoftwareBackup(string archive)
    {
        using var zip=System.IO.Compression.ZipFile.OpenRead(archive);
        var manifest=zip.Entries.FirstOrDefault(e=>e.FullName.Replace('\\','/')=="manifest.json");
        if(manifest==null)return false;
        if(manifest.Length>1024*1024)throw new InvalidDataException("备份清单过大。");
        using var stream=manifest.Open();using var reader=new StreamReader(stream);
        return System.Text.Json.Nodes.JsonNode.Parse(reader.ReadToEnd())?["type"]?.GetValue<string>()=="cakepet-backup";
    }
    internal async Task<PackEntry> ImportPackZipAsync(string archive)
    {
        var stage=Stage();
        try
        {
            var source=await Task.Run(()=>{
                ArchiveIO.Extract(archive,stage);
                var candidates=Directory.EnumerateFiles(stage,"modes.jsonc",SearchOption.AllDirectories).ToArray();
                if(candidates.Length!=1)throw new InvalidDataException("ZIP 必须包含一个素材包。");
                return Path.GetDirectoryName(candidates[0])!;
            });
            return await ImportAsync(source);
        }
        finally{if(Directory.Exists(stage))Directory.Delete(stage,true);}
    }
    private PackEntry PrepareImport(string source)
    {
        var pack=RolePackage.Load(source,GdiImageCache.IsReadable);
        var id=Guid.NewGuid();var entry=new PackEntry(id,pack.Name,id.ToString("N"));var dest=Resolve(entry);
        Directory.CreateDirectory(dest);
        try{Copy(source,dest);RolePackage.Load(dest);return entry;}
        catch{if(Directory.Exists(dest))Directory.Delete(dest,true);throw;}
    }
#endif
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
