namespace CakePet.Core;

public readonly record struct FoodExtent(double Width,double Height);

public sealed class FoodInstance
{
    internal FoodInstance(PetEngine owner,AnimationCursor animation,Point center,FoodExtent extent,long? lifetime)
    {Owner=owner;Animation=animation;Center=center;Extent=extent;RemainingMs=lifetime;}
    public Guid Id { get; }=Guid.NewGuid();
    public PetEngine Owner { get; }
    public AnimationCursor Animation { get; }
    public Point Center { get; internal set; }
    public FoodExtent Extent { get; }
    public double Width=>Extent.Width;
    public double Height=>Extent.Height;
    public double Size=>Math.Max(Width,Height);
    public string StartMode { get; internal set; }="";
    public double Opacity {get;set;}=.7;
    public bool Held { get; internal set; }
    public long? RemainingMs { get; internal set; }
}

// One world owns food lifetime and cross-role interactions. Each food has its own cursor.
public sealed class FoodWorld
{
    private readonly Random random;
    private readonly List<PetEngine> pets=new();
    private readonly Dictionary<PetEngine,Guid> owners=new();
    private Guid Group(PetEngine p)=>owners.GetValueOrDefault(p);
    private bool SameOwner(PetEngine a,PetEngine b)=>a==b||(Group(a)!=Guid.Empty&&Group(a)==Group(b));
    private readonly List<FoodInstance> foods=new();
    private readonly Dictionary<PetEngine,FoodInstance> lures=new();
    private readonly Dictionary<PetEngine,FoodInstance> forage=new();
    private readonly Dictionary<FoodInstance,HashSet<PetEngine>> contacts=new();
    public IReadOnlyList<FoodInstance> Foods=>foods.AsReadOnly();
    private readonly Dictionary<PetEngine,SpawnSchedule> schedules=new();
    private sealed record SpawnSchedule(Interval Period,Func<FoodInstance?> Spawn,long Remaining);
    public void SetAutomatic(PetEngine pet,bool enabled,Interval period,Func<FoodInstance?> spawn)
    {
        if(!enabled){schedules.Remove(pet);Clear(pet);return;}
        if(!pets.Contains(pet)||period.Min<=0||period.Max<period.Min)return;
        schedules[pet]=new(period,spawn,period.Sample(random));
    }
    public bool IsAutomatic(PetEngine pet)=>schedules.ContainsKey(pet);
    public void ResetSpawnTimer(PetEngine pet)
    {if(schedules.TryGetValue(pet,out var s))schedules[pet]=s with{Remaining=s.Period.Sample(random)};}
    public event Action<FoodInstance>? Spawned;
    public event Action<FoodInstance>? Removed;
    public FoodWorld(Random? random=null)=>this.random=random??new Random();
    public void Add(PetEngine pet,Guid ownerId=default)
    {
        if(pets.Contains(pet))return;
        pets.Add(pet);owners[pet]=ownerId;pet.ChooseForage=()=>ChooseForage(pet);
    }
    public void Remove(PetEngine pet)
    {
        if(!pets.Remove(pet))return;
        schedules.Remove(pet);owners.Remove(pet);foreach(var set in contacts.Values)set.Remove(pet);pet.ChooseForage=null;lures.Remove(pet);forage.Remove(pet);pet.EndLure(false);pet.StopForage();
        foreach(var food in foods.Where(f=>f.Owner==pet).ToArray())Remove(food);
    }
    public FoodInstance? Spawn(PetEngine owner,Bounds area,double size=48,long? lifetimeMs=60000,bool edge=true,int maximum=10,Func<string,FoodExtent>? dimensions=null,double opacity=.7)
    {
        if(!pets.Contains(owner)||!owner.FeedingEnabled||foods.Count(f=>SameOwner(f.Owner,owner))>=maximum)return null;
        var candidates=owner.Role.Package.Candidates("feed/food");
        if(candidates.Count==0||!double.IsFinite(size)||size<=0||lifetimeMs<=0)return null;
        var selected=candidates[random.Next(candidates.Count)];
        var extent=dimensions?.Invoke(selected)??new FoodExtent(size,size);
        if(!double.IsFinite(extent.Width)||!double.IsFinite(extent.Height)||extent.Width<=0||extent.Height<=0||area.Right<=area.Left||area.Bottom<=area.Top)return null;
        var scale=Math.Min(1,Math.Min((area.Right-area.Left)/extent.Width,(area.Bottom-area.Top)/extent.Height));
        extent=new FoodExtent(extent.Width*scale,extent.Height*scale);
        var hw=extent.Width/2;var hh=extent.Height/2;
        var b=new Bounds(area.Left+hw,area.Top+hh,Math.Max(area.Left+hw,area.Right-hw),Math.Max(area.Top+hh,area.Bottom-hh));
        var p=new Point(b.Left+random.NextDouble()*(b.Right-b.Left),b.Top+random.NextDouble()*(b.Bottom-b.Top));
        if(edge){if(owner.Space!=null){var position=owner.FindEdgeFoodPosition(extent.Width,extent.Height,random);if(position==null)return null;p=position.Value;}else p=SurfaceMotion.OnEdge(p,b,SurfaceMotion.Sides[random.Next(4)]);}
        var cursor=new AnimationCursor(owner.Role.Package,selected,new Random(random.Next()));
        var food=new FoodInstance(owner,cursor,p,extent,lifetimeMs){StartMode=cursor.Mode.Id,Opacity=opacity};foods.Add(food);Spawned?.Invoke(food);return food;
    }
    public bool Hold(FoodInstance food)
    {if(!foods.Contains(food))return false;if(food.Held)return true;food.Held=true;contacts[food]=new();UpdateInteractions();return true;}
    public void Move(FoodInstance food,Point center,Point? pointer=null)
    {
        if(!foods.Contains(food)||!food.Held)return;
        food.Center=center;UpdateInteractions();
        // Contact gives automatic forage a chance to select immediately, without forcing an animation by ID.
        var now=pets.Where(p=>p.Contains(pointer??center)).ToHashSet();
        var previous=contacts.GetValueOrDefault(food)??new HashSet<PetEngine>();
        foreach(var pet in now.Where(p=>!previous.Contains(p)&&p.CanForage&&p.Role.IsNaturalStandby&&!p.Foraging))ChooseForage(pet);
        contacts[food]=now;
    }
    public bool Release(FoodInstance food,Point pointer,bool cancelled=false,bool allowConsume=true)
    {
        if(!foods.Contains(food)||!food.Held)return false;
        food.Held=false;contacts.Remove(food);
        var eater=cancelled||!allowConsume?null:FindPet(pointer);
        if(eater!=null)Consume(food,eater);
        if(cancelled&&!foods.Any(f=>f.Held))foreach(var p in lures.Where(x=>x.Value==food).Select(x=>x.Key).ToArray())
        {lures.Remove(p);p.EndLure(false);}
        UpdateInteractions();return eater!=null;
    }
    public void Remove(FoodInstance food)
    {
        if(!foods.Remove(food))return;
        food.Held=false;contacts.Remove(food);
        foreach(var p in forage.Where(x=>x.Value==food).Select(x=>x.Key).ToArray())
        {forage.Remove(p);p.StopForage();}
        Removed?.Invoke(food);
    }
    public void Clear(PetEngine? owner=null)
    {
        foreach(var food in foods.Where(f=>owner==null||f.Owner==owner).ToArray())Remove(food);
        UpdateInteractions();
    }
    private PetEngine? FindPet(Point p)=>pets.Where(x=>x.Contains(p)).MinBy(x=>(x.Center-p).Length);
    private void Consume(FoodInstance food,PetEngine pet)
    {
        // Success clears lure/forage before the next state-pool selection, without a failure event.
        lures.Remove(pet);forage.Remove(pet);
        pet.Eat(food.Center);pet.StopForage(false);pet.EndLure(false);
        if(food.Owner.GrowOnFeed)pet.Grow(food.Owner.FeedGrowth);
        Remove(food);
    }
    private static bool InRange(PetEngine pet,FoodInstance f)
    {
        if(pet.Space!=null)return pet.InActivitySpace(f.Center);
        var b=pet.ActivityArea??new Bounds(pet.Bounds.Left,pet.Bounds.Top,pet.Bounds.Right+pet.DisplaySize,pet.Bounds.Bottom+pet.DisplaySize);
        return f.Center.X>=b.Left&&f.Center.X<=b.Right&&f.Center.Y>=b.Top&&f.Center.Y<=b.Bottom;
    }
    private FoodInstance? BestForage(PetEngine pet)=>foods.Where(f=>InRange(pet,f)&&double.IsFinite(pet.ForageDistance(f.Center))).MinBy(f=>pet.ForageDistance(f.Center));
    private bool ChooseForage(PetEngine pet)
    {
        if(!pet.CanForage)return false;
        var target=BestForage(pet);
        if(target==null||!pet.StartForage(target.Center))return false;
        forage[pet]=target;return true;
    }
    public void UpdateInteractions()
    {
        foreach(var pet in pets.ToArray())
        {
            if(forage.ContainsKey(pet))
            {
                var target=pet.CanForage&&pet.Role.StateFunction==PetEngine.ForagePath?BestForage(pet):null;
                if(target==null){forage.Remove(pet);pet.StopForage();}
                else
                {
                    forage[pet]=target;pet.SetForageTarget(target.Center);
                    if(pet.Foraging&&!target.Held&&FindPet(target.Center)==pet)Consume(target,pet);
                }
            }
            var held=pet.CanInteract&&(!pet.CanForage||pet.Role.Snapped)?foods.Where(f=>f.Held).MinBy(f=>(f.Center-pet.Center).Length):null;
            if(held==null)
            {
                if(lures.Remove(pet))pet.EndLure(pet.CanInteract);
                continue;
            }
            if(lures.TryGetValue(pet,out var previous)&&previous!=held&&!pet.IsLuring)lures.Remove(pet);
            if(pet.StartLure(held.Center))lures[pet]=held;
            else if(lures.Remove(pet))pet.EndLure(false);
        }
    }
    public void Advance(long milliseconds)
    {
        if(milliseconds<0)throw new ArgumentOutOfRangeException(nameof(milliseconds));
        foreach(var pet in schedules.Keys.ToArray())
        {
            if(!schedules.TryGetValue(pet,out var s))continue;
            var remaining=s.Remaining-milliseconds;
            if(remaining<=0){if(pet.FeedingEnabled)s.Spawn();remaining=s.Period.Sample(random);}
            if(schedules.ContainsKey(pet))schedules[pet]=s with{Remaining=remaining};
        }
        foreach(var food in foods.ToArray())
        {
            if(food.RemainingMs is long remaining)
            {
                food.RemainingMs=remaining-milliseconds;
                if(food.RemainingMs<=0){Remove(food);continue;}
            }
            food.Animation.Advance(milliseconds);
        }
        UpdateInteractions();
    }
}
