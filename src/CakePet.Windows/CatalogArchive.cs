using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using CakePet.Core;
namespace CakePet.Windows;
internal sealed partial class CatalogStorage
{
    private string Stage()=>Path.Combine(root,"staging",Guid.NewGuid().ToString("N"));
    public void ExportPack(PackEntry pack,string path)=>ArchiveIO.Zip(path,zip=>ArchiveIO.Tree(zip,Resolve(pack)));
    public string ImportZip(string path)
    {
        var stage=Stage();try
        {
            ArchiveIO.Extract(path,stage);var manifest=Path.Combine(stage,"manifest.json");
            if(File.Exists(manifest)&&ArchiveIO.Read(manifest)["type"]?.GetValue<string>()=="cakepet-backup")return Restore(stage);
            var candidates=Directory.EnumerateFiles(stage,"modes.jsonc",SearchOption.AllDirectories).ToArray();
            if(candidates.Length!=1)throw new InvalidDataException("ZIP 必须包含一个素材包或软件备份。");
            return "已导入素材包："+Import(Path.GetDirectoryName(candidates[0])!).Name;
        }finally{if(Directory.Exists(stage))Directory.Delete(stage,true);}
    }
    public void ExportBackup(string path,string version)
    {
        Save();ArchiveIO.Zip(path,zip=>{
            ArchiveIO.Text(zip,"manifest.json",new JsonObject{["type"]="cakepet-backup",["platform"]="windows",["appVersion"]=version,["createdAt"]=DateTimeOffset.Now.ToString("O")}.ToJsonString(ArchiveIO.Options));
            ArchiveIO.Text(zip,"catalog.json",JsonSerializer.Serialize(Catalog,ArchiveIO.Options));
            foreach(var p in Catalog.Packs)ArchiveIO.Tree(zip,Resolve(p),"packs/"+p.Directory+"/");
        });
    }
    private string Restore(string stage)
    {
        var source=Path.Combine(stage,"catalog.json");
        if(!File.Exists(source))throw new InvalidDataException("此备份不是支持的 Windows 备份；Android 素材包可单独导入。");
        var data=ArchiveIO.Read(source);var original=Catalog;
        var merged=JsonSerializer.Deserialize<RoleCatalog>(JsonSerializer.Serialize(original,ArchiveIO.Options),ArchiveIO.Options)!;
        var copied=new List<string>();int packs=0,roles=0,skipped=0;
        try
        {
            if(data["Packs"] is JsonArray packList)foreach(var item in packList.OfType<JsonObject>())
            {
                string? dest=null;try
                {
                    var p=item.Deserialize<PackEntry>(ArchiveIO.Options)??throw new InvalidDataException();
                    if(p.Id==Guid.Empty||!Guid.TryParseExact(p.Directory,"N",out _))throw new InvalidDataException();
                    var dir=Path.Combine(stage,"packs",p.Directory);var validated=RolePackage.Load(dir,GdiImageCache.IsReadable);
                    var folder=Guid.NewGuid().ToString("N");dest=Path.Combine(root,"packs",folder);Directory.CreateDirectory(dest);Copy(dir,dest);
                    merged.Packs.RemoveAll(x=>x.Id==p.Id);merged.Packs.Add(new PackEntry(p.Id,validated.Name,folder));copied.Add(dest);packs++;
                }catch(Exception){if(dest!=null&&Directory.Exists(dest))Directory.Delete(dest,true);skipped++;}
            }
            if(data["Roles"] is JsonArray roleList)foreach(var item in roleList.OfType<JsonObject>())
            {
                try
                {
                    var id=item["Id"]?.GetValue<Guid>()??throw new InvalidDataException();var packId=item["PackId"]?.GetValue<Guid>()??throw new InvalidDataException();
                    if(id==Guid.Empty||!merged.Packs.Any(p=>p.Id==packId))throw new InvalidDataException();
                    var old=merged.Roles.FirstOrDefault(x=>x.Id==id);var name=item["Name"]?.GetValue<string>()??old?.Name??"角色";
                    if(string.IsNullOrWhiteSpace(name))throw new InvalidDataException();
                    var settings=item["Settings"] is JsonObject fields?ArchiveIO.Overlay(old?.Settings??merged.Defaults,fields):old?.Settings??merged.Defaults;
                    merged.Roles.RemoveAll(x=>x.Id==id);merged.Roles.Add(new RoleEntry(id,packId,name,settings));roles++;
                }catch(Exception){skipped++;}
            }
            if(data["Defaults"] is JsonObject defaults)merged.Defaults=ArchiveIO.Overlay(merged.Defaults,defaults);
            if(data["Typing"] is JsonObject typing){try{merged.Typing=(typing.Deserialize<TypingSettings>(ArchiveIO.Options)??merged.Typing).Normalize();}catch(JsonException){}}
            if(data["TypingDay"] is JsonValue day&&day.TryGetValue<string>(out var date)&&data["TypingToday"] is JsonValue count&&count.TryGetValue<long>(out var total)&&date==DateTime.Now.ToString("yyyy-MM-dd")){merged.TypingDay=date;merged.TypingToday=Math.Max(merged.TypingDay==original.TypingDay?original.TypingToday:0,total);}
            if(data["ThinkingLocation"] is JsonValue location&&location.TryGetValue<int>(out var loc)&&Enum.IsDefined(typeof(ThinkingLocation),loc))merged.ThinkingLocation=(ThinkingLocation)loc;
            if(data["ThinkingPolicy"] is JsonValue policy&&policy.TryGetValue<int>(out var pol)&&Enum.IsDefined(typeof(ThinkingDisplayPolicy),pol))merged.ThinkingPolicy=(ThinkingDisplayPolicy)pol;
            else if(data["SingleThinking"] is JsonValue single&&single.TryGetValue<bool>(out var st))merged.ThinkingPolicy=st?ThinkingDisplayPolicy.Latest:ThinkingDisplayPolicy.Overlap;
            if(data["AutoSummon"] is JsonValue summon&&summon.TryGetValue<bool>(out var au))merged.AutoSummon=au;
            Commit(merged);
        }catch{foreach(var dir in copied)if(Directory.Exists(dir))Directory.Delete(dir,true);throw;}
        foreach(var old in original.Packs.Where(p=>!merged.Packs.Any(x=>x.Directory==p.Directory)))try{Directory.Delete(Resolve(old),true);}catch(IOException){}catch(UnauthorizedAccessException){}
        return $"已恢复 {packs} 个素材包、{roles} 个角色，跳过 {skipped} 项；未包含的数据保留。";
    }
    public void Reset(string bundled)
    {
        var old=Catalog;var fresh=new RoleCatalog();var folder=Guid.NewGuid().ToString("N");var dest=Path.Combine(root,"packs",folder);
        try{var pack=RolePackage.Load(bundled,GdiImageCache.IsReadable);Directory.CreateDirectory(dest);Copy(bundled,dest);var p=new PackEntry(Guid.NewGuid(),pack.Name,folder);fresh.Packs.Add(p);fresh.AddRole(p.Id,p.Name);Commit(fresh);}
        catch{if(Directory.Exists(dest))Directory.Delete(dest,true);throw;}
        foreach(var p in old.Packs)try{Directory.Delete(Resolve(p),true);}catch(IOException){}catch(UnauthorizedAccessException){}
    }
    public void ExportRole(RoleEntry role,string path)=>ArchiveIO.Write(path,new JsonObject{["type"]="cakepet-role-settings",["platform"]="windows",["version"]=1,["settings"]=JsonSerializer.SerializeToNode(role.Settings,ArchiveIO.Options)}.ToJsonString(ArchiveIO.Options));
    public RoleEntry ImportRole(RoleEntry role,string path)
    {
        var obj=ArchiveIO.Read(path);if(obj["type"]?.GetValue<string>()!="cakepet-role-settings"||obj["settings"] is not JsonObject fields)throw new InvalidDataException("不是支持的角色设置文件。");
        return role with{Settings=ArchiveIO.Overlay(role.Settings,fields)};
    }
}
