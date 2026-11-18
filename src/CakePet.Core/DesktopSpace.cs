namespace CakePet.Core;

// A union of axis-aligned screen activity rectangles, expressed in physical desktop pixels.
// Boundary segments are directed clockwise around occupied space (interior on the right).
public readonly record struct DesktopBoundary(Point Start,Point End,Edge Side)
{
    public double Length=>(End-Start).Length;
    public Point Tangent=>(End-Start)*(1/Length);
    public Point At(double distance)=>Start+Tangent*Math.Clamp(distance,0,Length);
    public double Along(Point p)=>Math.Clamp((p.X-Start.X)*Tangent.X+(p.Y-Start.Y)*Tangent.Y,0,Length);
    public Point Closest(Point p)=>At(Along(p));
}
public readonly record struct BoundaryContact(int Segment,Point Position,double Distance);
public readonly record struct BoundaryTravel(Point Position,Edge Side,int Segment);
public readonly record struct SpaceMotion(Point Position,Edge Hit,bool Wrapped);
public sealed class DesktopSpace
{
    private const double Eps=1e-7;
    private readonly Bounds[] areas;
    private readonly DesktopBoundary[] edges;
    private readonly int[] next,previous,component;
    public IReadOnlyList<Bounds> Areas=>areas;
    public IReadOnlyList<DesktopBoundary> Edges=>edges;
    public DesktopSpace(IEnumerable<Bounds> rectangles)
    {
        areas=rectangles.Where(a=>a.Right>a.Left&&a.Bottom>a.Top).Distinct().ToArray();
        if(areas.Length==0)throw new ArgumentException("Empty desktop space.");
        var xs=areas.SelectMany(a=>new[]{a.Left,a.Right}).Distinct().Order().ToArray();
        var ys=areas.SelectMany(a=>new[]{a.Top,a.Bottom}).Distinct().Order().ToArray();
        var occupied=new bool[xs.Length-1,ys.Length-1];
        for(int x=0;x<xs.Length-1;x++)for(int y=0;y<ys.Length-1;y++)occupied[x,y]=Contains(new Point((xs[x]+xs[x+1])/2,(ys[y]+ys[y+1])/2));
        bool Has(int x,int y)=>x>=0&&y>=0&&x<xs.Length-1&&y<ys.Length-1&&occupied[x,y];
        var segments=new List<DesktopBoundary>();
        for(int x=0;x<xs.Length-1;x++)for(int y=0;y<ys.Length-1;y++)if(Has(x,y))
        {
            double l=xs[x],r=xs[x+1],t=ys[y],b=ys[y+1];
            if(!Has(x,y-1))segments.Add(new(new(l,t),new(r,t),Edge.Top));
            if(!Has(x+1,y))segments.Add(new(new(r,t),new(r,b),Edge.Right));
            if(!Has(x,y+1))segments.Add(new(new(r,b),new(l,b),Edge.Bottom));
            if(!Has(x-1,y))segments.Add(new(new(l,b),new(l,t),Edge.Left));
        }
        edges=segments.ToArray();next=new int[edges.Length];previous=new int[edges.Length];component=new int[edges.Length];Array.Fill(next,-1);Array.Fill(previous,-1);Array.Fill(component,-1);
        var starts=Enumerable.Range(0,edges.Length).GroupBy(i=>edges[i].Start).ToDictionary(g=>g.Key,g=>g.ToArray());
        for(int i=0;i<edges.Length;i++)
        {
            if(!starts.TryGetValue(edges[i].End,out var candidates))continue;
            var tangent=edges[i].Tangent;
            // At a point-only screen contact choose the right turn, keeping the loops separate.
            next[i]=candidates.OrderByDescending(j=>TurnRank(tangent,edges[j].Tangent)).First();
            previous[next[i]]=i;
        }
        int c=0;for(int i=0;i<edges.Length;i++)if(component[i]<0){int j=i;while(j>=0&&component[j]<0){component[j]=c;j=next[j];}c++;}
    }
    private static int TurnRank(Point a,Point b){double cross=a.X*b.Y-a.Y*b.X,dot=a.X*b.X+a.Y*b.Y;return cross>0?3:dot>0?2:cross<0?1:0;}
    public bool Contains(Point p)=>areas.Any(a=>p.X>=a.Left-Eps&&p.X<=a.Right+Eps&&p.Y>=a.Top-Eps&&p.Y<=a.Bottom+Eps);
    public Point Clamp(Point p)
    {
        if(Contains(p))return p;
        return areas.Select(a=>new Point(Math.Clamp(p.X,a.Left,a.Right),Math.Clamp(p.Y,a.Top,a.Bottom))).MinBy(q=>(q-p).Length);
    }
    public BoundaryContact? Nearest(Point p,Edge allowed=Edge.All,int? sameLoop=null)
    {
        BoundaryContact? best=null;for(int i=0;i<edges.Length;i++){if(!allowed.HasFlag(edges[i].Side)||sameLoop is int loop&&component[i]!=component[loop])continue;var q=edges[i].Closest(p);var d=(p-q).Length;if(best==null||d<best.Value.Distance)best=new(i,q,d);}return best;
    }
    public BoundaryTravel Walk(int segment,Point position,double distance)
    {
        int i=segment;double along=edges[i].Along(position);int direction=distance>=0?1:-1;double left=Math.Abs(distance);
        // Modulo closed-loop length prevents excessive iteration on large frame displacements.
        double length=0;int j=i;do{length+=edges[j].Length;j=next[j];}while(j>=0&&j!=i);
        if(length>0)left%=length;
        for(int guard=0;guard<=edges.Length&&left>Eps;guard++)
        {
            var edge=edges[i];double available=direction>0?edge.Length-along:along;
            if(left<available-Eps){along+=direction*left;left=0;break;}
            left=Math.Max(0,left-available);int n=direction>0?next[i]:previous[i];if(n<0){along=direction>0?edge.Length:0;break;}i=n;along=direction>0?0:edges[i].Length;
        }
        return new(edges[i].At(along),edges[i].Side,i);
    }
    public (int Direction,double Distance)? Route(int segment,Point position,Point target)
    {
        var destination=Nearest(target,Edge.All,segment);if(destination==null)return null;
        double start=edges[segment].Along(position),end=edges[destination.Value.Segment].Along(destination.Value.Position);
        double forward=0,total=0;int i=segment;bool found=false;
        do{if(i==destination.Value.Segment&&!found){forward=total+end-start;found=true;}total+=edges[i].Length;i=next[i];}while(i>=0&&i!=segment);
        if(!found||total<=Eps)return null;forward=(forward%total+total)%total;var backward=total-forward;return forward<=backward?(1,forward):(-1,backward);
    }
    // Swept point movement: find the first outward crossing, never tunnel across a missing screen.
    public SpaceMotion Move(Point from,Point delta,bool wrap=false)
    {
        var p=Clamp(from);var remaining=delta;Edge hits=Edge.None;bool wrapped=false;
        double left=areas.Min(a=>a.Left),right=areas.Max(a=>a.Right);
        for(int iteration=0;iteration<12&&remaining.Length>Eps;iteration++)
        {
            double first=1+Eps;Edge hit=Edge.None;
            foreach(var e in edges)
            {
                bool vertical=e.Side is Edge.Left or Edge.Right;
                double d=vertical?remaining.X:remaining.Y;
                if(e.Side is Edge.Left or Edge.Top?d>=-Eps:d<=Eps)continue;
                double t=((vertical?e.Start.X:e.Start.Y)-(vertical?p.X:p.Y))/d;
                if(t<-Eps||t>1+Eps)continue;
                var q=p+remaining*Math.Clamp(t,0,1);
                double along=vertical?q.Y:q.X,a=vertical?e.Start.Y:e.Start.X,b=vertical?e.End.Y:e.End.X;
                if(along<Math.Min(a,b)-Eps||along>Math.Max(a,b)+Eps)continue;
                // An endpoint shared with an inward continuation is not a collision.
                if(Contains(q+remaining*(1e-5/Math.Max(remaining.Length,1e-5))))continue;
                if(t<first-Eps){first=Math.Max(0,t);hit=e.Side;}else if(Math.Abs(t-first)<=Eps)hit|=e.Side;
            }
            if(hit==Edge.None){p=Clamp(p+remaining);break;}
            p+=remaining*Math.Clamp(first,0,1);remaining*=Math.Max(0,1-first);
            if(wrap&&(hit==Edge.Left||hit==Edge.Right))
            {
                double target=hit==Edge.Left?right:left;
                if(Math.Abs(p.X-(hit==Edge.Left?left:right))<Eps&&Contains(new Point(target,p.Y))){p=new Point(target,p.Y);wrapped=true;continue;}
            }
            hits|=hit;if((hit&(Edge.Left|Edge.Right))!=0)remaining=remaining with{X=0};if((hit&(Edge.Top|Edge.Bottom))!=0)remaining=remaining with{Y=0};
        }
        return new(p,hits,wrapped);
    }
}
