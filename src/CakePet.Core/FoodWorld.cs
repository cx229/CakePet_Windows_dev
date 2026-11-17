namespace CakePet.Core;

public sealed class FoodInstance
{
    internal FoodInstance(PetEngine owner,AnimationCursor animation,Point center,double size,long? lifetime)
    {Owner=owner;Animation=animation;Center=center;Size=size;RemainingMs=lifetime;}
    public Guid Id { get; }=Guid.NewGuid();
    public PetEngine Owner { get; }
    public AnimationCursor Animation { get; }
    public Point Center { get; internal set; }
    public double Size { get; }
    public bool Held { get; internal set; }
    public long? RemainingMs { get; internal set; }
}

// One world owns food lifetime and cross-role interactions. Each food has its own cursor.
public sealed class FoodWorld
{
    private readonly Random random;
    private readonly List<PetEngine> pets=new();
    private readonly List<FoodInstance> foods=new();
    private readonly Dictionary<PetEngine,FoodInstance> lures=new();
    private readonly Dictionary<PetEngine,FoodInstance> forage=new();
    public IReadOnlyList<FoodInstance> Foods=>foods.AsReadOnly();
    public event Action<FoodInstance>? Spawned;
    public event Action<FoodInstance>? Removed;
    public FoodWorld(Random? random=null)=>this.random=random??new Random();
    public void Add(PetEngine pet)
    {
        if(pets.Contains(pet))return;
        pets.Add(pet);pet.ChooseForage=()=>ChooseForage(pet);
    }
    public void Remove(PetEngine pet)
    {
        if(!pets.Remove(pet))return;
        pet.ChooseForage=null;lures.Remove(pet);forage.Remove(pet);pet.EndLure(false);pet.StopForage();
        foreach(var food in foods.Where(f=>f.Owner==pet).ToArray())Remove(food);
    }
    public FoodInstance? Spawn(PetEngine owner,Bounds area,double size=48,long? lifetimeMs=60000,bool edge=true,int maximum=10)
    {
        if(!pets.Contains(owner)||!owner.FeedingEnabled||foods.Count(f=>f.Owner==owner)>=maximum)return null;
        var candidates=owner.Role.Package.Candidates("feed/food");
        if(candidates.Count==0||!double.IsFinite(size)||size<=0||lifetimeMs<=0)return null;
        var half=size/2;
        var b=new Bounds(area.Left+half,area.Top+half,Math.Max(area.Left+half,area.Right-half),Math.Max(area.Top+half,area.Bottom-half));
        var p=new Point(b.Left+random.NextDouble()*(b.Right-b.Left),b.Top+random.NextDouble()*(b.Bottom-b.Top));
        if(edge)p=SurfaceMotion.OnEdge(p,b,SurfaceMotion.Sides[random.Next(4)]);
        var cursor=new AnimationCursor(owner.Role.Package,candidates[random.Next(candidates.Count)],new Random(random.Next()));
        var food=new FoodInstance(owner,cursor,p,size,lifetimeMs);foods.Add(food);Spawned?.Invoke(food);return food;
    }
    public bool Hold(FoodInstance food)
    {if(!foods.Contains(food))return false;food.Held=true;UpdateInteractions();return true;}
    public void Move(FoodInstance food,Point center)
    {
        if(!foods.Contains(food)||!food.Held)return;
        food.Center=center;UpdateInteractions();
        // Contact gives automatic forage a chance to select immediately, without forcing an animation by ID.
        foreach(var pet in pets.Where(p=>p.CanForage&&p.Contains(center)&&p.Role.IsNaturalStandby&&!p.Foraging))
            ChooseForage(pet);
    }
    public bool Release(FoodInstance food,Point pointer,bool cancelled=false)
    {
        if(!foods.Contains(food)||!food.Held)return false;
        food.Held=false;
        var eater=cancelled?null:FindPet(pointer);
        if(eater!=null)Consume(food,eater);
        UpdateInteractions();return eater!=null;
    }
    public void Remove(FoodInstance food)
    {
        if(!foods.Remove(food))return;
        food.Held=false;
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
        var b=pet.Bounds;return f.Center.X>=b.Left&&f.Center.X<=b.Right+pet.DisplaySize&&f.Center.Y>=b.Top&&f.Center.Y<=b.Bottom+pet.DisplaySize;
    }
    private FoodInstance? BestForage(PetEngine pet)=>foods.Where(f=>InRange(pet,f)).MinBy(f=>
        PerimeterRoute.Calculate(pet.Bounds,pet.Body.Position,pet.Body.ClimbEdge==Edge.None?pet.RenderEdge:pet.Body.ClimbEdge,
            f.Center-new Point(pet.DisplaySize/2,pet.DisplaySize/2)).Distance);
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
            if(pet.StartLure(held.Center))lures[pet]=held;
            else if(lures.Remove(pet))pet.EndLure(false);
        }
    }
    public void Advance(long milliseconds)
    {
        if(milliseconds<0)throw new ArgumentOutOfRangeException(nameof(milliseconds));
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
