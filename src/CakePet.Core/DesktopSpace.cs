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
    private readonly int[] wrapNext,wrapPrevious,wrapComponent;
    private readonly bool[] wrapOpen;
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
        LabelComponents(next,component);
        // Each vertical grid segment has identical y subdivision on both outer sides.
        // Glue opposite open segments together, removing them from the walkable contour.
        wrapOpen=new bool[edges.Length];var pair=new int[edges.Length];Array.Fill(pair,-1);
        double left=areas.Min(a=>a.Left),right=areas.Max(a=>a.Right);
        var rightEdges=Enumerable.Range(0,edges.Length).Where(i=>edges[i].Side==Edge.Right&&edges[i].Start.X==right)
            .ToDictionary(i=>(edges[i].Start.Y,edges[i].End.Y));
        for(int i=0;i<edges.Length;i++)
        {
            var e=edges[i];
            if(e.Side==Edge.Left&&e.Start.X==left&&rightEdges.TryGetValue((e.End.Y,e.Start.Y),out int other))
            {wrapOpen[i]=wrapOpen[other]=true;pair[i]=other;pair[other]=i;}
        }
        wrapNext=new int[edges.Length];wrapPrevious=new int[edges.Length];wrapComponent=new int[edges.Length];
        Array.Fill(wrapNext,-1);Array.Fill(wrapPrevious,-1);Array.Fill(wrapComponent,-1);
        for(int i=0;i<edges.Length;i++)
        {
            if(wrapOpen[i])continue;
            int j=next[i];
            for(int guard=0;j>=0&&wrapOpen[j]&&guard<edges.Length;guard++)j=next[pair[j]];
            if(j>=0&&!wrapOpen[j]){wrapNext[i]=j;wrapPrevious[j]=i;}
        }
        LabelComponents(wrapNext,wrapComponent,wrapOpen);
    }
    private static void LabelComponents(int[] links,int[] labels,bool[]? excluded=null)
    {
        int c=0;
        for(int i=0;i<links.Length;i++)
        {
            if(labels[i]>=0||excluded?[i]==true)continue;
            int j=i;while(j>=0&&labels[j]<0){labels[j]=c;j=links[j];}c++;
        }
    }
    public bool IsWalkable(int segment,bool wrap=false)=>!wrap||!wrapOpen[segment];
    private static int TurnRank(Point a,Point b){double cross=a.X*b.Y-a.Y*b.X,dot=a.X*b.X+a.Y*b.Y;return cross>0?3:dot>0?2:cross<0?1:0;}
    public bool Contains(Point p)=>areas.Any(a=>p.X>=a.Left-Eps&&p.X<=a.Right+Eps&&p.Y>=a.Top-Eps&&p.Y<=a.Bottom+Eps);
    public Point Clamp(Point p)
    {
        if(Contains(p))return p;
        return areas.Select(a=>new Point(Math.Clamp(p.X,a.Left,a.Right),Math.Clamp(p.Y,a.Top,a.Bottom))).MinBy(q=>(q-p).Length);
    }
    public BoundaryContact? Nearest(Point p,Edge allowed=Edge.All,int? sameLoop=null,bool wrap=false)
    {
        BoundaryContact? best=null;for(int i=0;i<edges.Length;i++){if(!IsWalkable(i,wrap)||!allowed.HasFlag(edges[i].Side)||sameLoop is int loop&&!SameLoop(i,loop,wrap))continue;var q=edges[i].Closest(p);var d=(p-q).Length;if(best==null||d<best.Value.Distance)best=new(i,q,d);}return best;
    }
    public bool SameLoop(int a,int b,bool wrap=false){var labels=wrap?wrapComponent:component;return labels[a]>=0&&labels[a]==labels[b];}
    // At horizontal wrap openings there is no solid wall to spawn food on.
    public bool IsWrapOpening(DesktopBoundary edge,Point p,bool wrap)
    {
        if(!wrap||edge.Side is not (Edge.Left or Edge.Right))return false;
        double left=areas.Min(a=>a.Left),right=areas.Max(a=>a.Right);
        return Math.Abs(p.X-(edge.Side==Edge.Left?left:right))<Eps&&Contains(new Point(edge.Side==Edge.Left?right:left,p.Y));
    }
    // Food center lies just inside a real perimeter segment; its whole rectangle must fit the union.
    public bool ContainsRectangle(Bounds rectangle)
    {
        var xs=areas.SelectMany(a=>new[]{a.Left,a.Right}).Where(x=>x>rectangle.Left&&x<rectangle.Right).Append(rectangle.Left).Append(rectangle.Right).Distinct().Order().ToArray();
        var ys=areas.SelectMany(a=>new[]{a.Top,a.Bottom}).Where(y=>y>rectangle.Top&&y<rectangle.Bottom).Append(rectangle.Top).Append(rectangle.Bottom).Distinct().Order().ToArray();
        for(int x=0;x<xs.Length-1;x++)for(int y=0;y<ys.Length-1;y++)if(!Contains(new Point((xs[x]+xs[x+1])/2,(ys[y]+ys[y+1])/2)))return false;
        return true;
    }
    public Point? SpawnEdge(int segment,double width,double height,bool wrap,Random random)
    {
        var candidates=Enumerable.Range(0,edges.Length).Where(i=>SameLoop(segment,i,wrap)).OrderBy(_=>random.Next()).ToArray();
        foreach(int i in candidates)
        {
            var e=edges[i];double margin=e.Side is Edge.Left or Edge.Right?height/2:width/2;
            if(e.Length+Eps<2*margin)continue;
            // A few positions per segment allow partially open wrap edges and stepped corners.
            foreach(double fraction in new[]{random.NextDouble(),.5,0.0,1.0})
            {
                var boundary=e.At(margin+Math.Max(0,e.Length-2*margin)*fraction);
                if(IsWrapOpening(e,boundary,wrap))continue;
                var inward=e.Side switch{Edge.Left=>new Point(width/2,0),Edge.Right=>new Point(-width/2,0),Edge.Top=>new Point(0,height/2),_=>new Point(0,-height/2)};
                var p=boundary+inward;
                if(!ContainsRectangle(new Bounds(p.X-width/2,p.Y-height/2,p.X+width/2,p.Y+height/2)))continue;
                // Use the same nearest real boundary as forage; never choose a different loop.
                var nearest=Nearest(p,wrap:wrap);if(nearest is BoundaryContact c&&SameLoop(segment,c.Segment,wrap))return p;
            }
        }
        return null;
    }
    public BoundaryTravel Walk(int segment,Point position,double distance,bool wrap=false)
    {
        var forward=wrap?wrapNext:next;var backward=wrap?wrapPrevious:previous;
        if(!IsWalkable(segment,wrap))throw new ArgumentException("Cannot walk an open boundary.",nameof(segment));
        int i=segment;double along=edges[i].Along(position);int direction=distance>=0?1:-1;double left=Math.Abs(distance);
        // Modulo closed-loop length prevents excessive iteration on large frame displacements.
        double length=0;int j=i;do{length+=edges[j].Length;j=forward[j];}while(j>=0&&j!=i);
        if(j==i&&length>0)left%=length;
        for(int guard=0;guard<=edges.Length&&left>Eps;guard++)
        {
            var edge=edges[i];double available=direction>0?edge.Length-along:along;
            if(left<available-Eps){along+=direction*left;left=0;break;}
            left=Math.Max(0,left-available);int n=direction>0?forward[i]:backward[i];if(n<0){along=direction>0?edge.Length:0;break;}i=n;along=direction>0?0:edges[i].Length;
        }
        return new(edges[i].At(along),edges[i].Side,i);
    }
    public (int Direction,double Distance)? Route(int segment,Point position,Point target,bool wrap=false)
    {
        var links=wrap?wrapNext:next;
        var destination=Nearest(target,wrap:wrap);if(destination==null||!SameLoop(segment,destination.Value.Segment,wrap))return null;
        double start=edges[segment].Along(position),end=edges[destination.Value.Segment].Along(destination.Value.Position);
        double forward=0,total=0;int i=segment;bool found=false;
        do{if(i==destination.Value.Segment&&!found){forward=total+end-start;found=true;}total+=edges[i].Length;i=links[i];}while(i>=0&&i!=segment);
        if(!found||total<=Eps||i!=segment)return null;forward=(forward%total+total)%total;var backward=total-forward;return forward<=backward?(1,forward):(-1,backward);
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
