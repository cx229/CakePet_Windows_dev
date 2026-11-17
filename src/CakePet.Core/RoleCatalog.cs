using System.Text.Json;
namespace CakePet.Core;

public sealed record PackEntry(Guid Id,string Name,string Directory);
public sealed record RoleSettings
{
    public int Count {get;init;}=1;
    public double Scale {get;init;}=1.4;
    public double Opacity {get;init;}=1;
    public bool Move {get;init;}=true;
    public bool Climb {get;init;}=true;
    public bool Leap {get;init;}
    public bool Forage {get;init;}=true;
    public bool Feed {get;init;}=true;
    public bool AutoFeed {get;init;}=true;
    public bool Grow {get;init;}=true;
    public bool Throw {get;init;}=true;
    public bool Snap {get;init;}=true;
    public bool FullScreen {get;init;}
    public bool ClickThrough {get;init;}
    public bool PixelHit {get;init;}=true;
    public bool Thinking {get;init;}=true;
    public RoleSettings Normalize()=>this with{Count=Math.Clamp(Count,1,30),Scale=double.IsFinite(Scale)?Math.Clamp(Scale,.5,3):1.4,Opacity=double.IsFinite(Opacity)?Math.Clamp(Opacity,.05,1):1};
}
public sealed record RoleEntry(Guid Id,Guid PackId,string Name,RoleSettings Settings);
public sealed class RoleCatalog
{
    public List<PackEntry> Packs {get;init;}=new();
    public List<RoleEntry> Roles {get;init;}=new();
    public bool SingleThinking {get;set;}=true;
    public bool AutoSummon {get;set;}=true;
    public static RoleCatalog Load(string file)
    {
        if(!File.Exists(file))return new();
        var data=JsonSerializer.Deserialize<RoleCatalog>(File.ReadAllText(file))??throw new InvalidDataException("Invalid catalog.");
        if(data.Packs==null||data.Roles==null||data.Packs.Select(x=>x.Id).Distinct().Count()!=data.Packs.Count||data.Roles.Select(x=>x.Id).Distinct().Count()!=data.Roles.Count||data.Roles.Any(x=>x.Settings==null||!data.Packs.Any(p=>p.Id==x.PackId)))throw new InvalidDataException("Invalid role references.");
        return data;
    }
    public void Save(string file)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(file))!);
        var temp=file+"."+Guid.NewGuid().ToString("N")+".tmp";
        try{File.WriteAllText(temp,JsonSerializer.Serialize(this,new JsonSerializerOptions{WriteIndented=true}));File.Move(temp,file,true);}
        finally{if(File.Exists(temp))File.Delete(temp);}
    }
    public RoleEntry AddRole(Guid packId,string name)
    {
        if(!Packs.Any(p=>p.Id==packId))throw new ArgumentException("Unknown pack.");
        if(string.IsNullOrWhiteSpace(name))throw new ArgumentException("Empty name.");
        var role=new RoleEntry(Guid.NewGuid(),packId,name.Trim(),new RoleSettings());Roles.Add(role);return role;
    }
    public void Update(RoleEntry role)
    {
        var index=Roles.FindIndex(x=>x.Id==role.Id);
        if(index<0||!Packs.Any(x=>x.Id==role.PackId)||string.IsNullOrWhiteSpace(role.Name))throw new ArgumentException("Invalid role.");
        Roles[index]=role with{Name=role.Name.Trim(),Settings=role.Settings.Normalize()};
    }
}
