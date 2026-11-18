using System.Runtime.InteropServices;
using CakePet.Core;
namespace CakePet.Windows;
internal sealed record ThoughtMessage(string Key,string Name,string Text,ThinkingStyle Style,long Order);

// One click-through layered HWND for all messages, with GDI+ text and alpha.
// No WPF visual tree or text layout is needed while pets are running.
internal sealed class ThinkingPresenter:IDisposable
{
    private sealed class Item(ThoughtMessage message,long born)
    {
        public ThoughtMessage Message=message;public long Born=born;public long? Removed;
    }
    private readonly Dictionary<string,Item> items=new();
    private NativeLayer? layer;
    private nuint token;
    private ThoughtMessage[] previous=Array.Empty<ThoughtMessage>();
    private ThinkingLocation previousLocation=(ThinkingLocation)(-1);
    private ThinkingDisplayPolicy previousPolicy=ThinkingDisplayPolicy.None;
    private long lastLayout;
    private bool visible,disposed;
    public void Clear()
    {
        items.Clear();previous=Array.Empty<ThoughtMessage>();
        if(layer!=null){layer.Dispose();layer=null;}visible=false;
        if(token!=0){Gdi.GdiplusShutdown(token);token=0;}
    }
    private static uint Alpha(uint color,double alpha)=>((uint)Math.Round(Math.Clamp(alpha,0,1)*255)<<24)|(color&0xffffff);
    public void Render(IEnumerable<ThoughtMessage> thoughts,ThinkingLocation location,ThinkingDisplayPolicy policy)
    {
        if(disposed)return;
        if(policy==ThinkingDisplayPolicy.None){if(items.Count>0||visible)Clear();previousPolicy=policy;return;}
        var entries=thoughts.OrderByDescending(x=>x.Order).ToArray();
        if(policy==ThinkingDisplayPolicy.Latest)entries=entries.Take(1).ToArray();
        var now=Environment.TickCount64;bool policyChanged=policy!=previousPolicy;
        if(policyChanged)Clear();
        bool changed=policyChanged||!entries.SequenceEqual(previous)||location!=previousLocation;
        foreach(var key in items.Keys.Except(entries.Select(e=>e.Key)).ToArray())
        {
            var item=items[key];
            if(!item.Message.Style.FlashOut||policy==ThinkingDisplayPolicy.Latest&&entries.Length>0)items.Remove(key);
            else item.Removed??=now;
        }
        foreach(var entry in entries)
        {
            if(!items.TryGetValue(entry.Key,out var item))items.Add(entry.Key,new Item(entry,now));
            else{item.Message=entry;item.Removed=null;}
        }
        foreach(var key in items.Where(p=>p.Value.Removed is long removed&&now-removed>=160).Select(p=>p.Key).ToArray()){items.Remove(key);changed=true;}
        previous=entries;previousLocation=location;previousPolicy=policy;
        if(items.Count==0){if(visible)Clear();return;}
        bool animated=items.Values.Any(i=>i.Removed.HasValue||i.Message.Style.FlashIn&&now-i.Born<180);
        if(!changed&&!animated&&now-lastLayout<500)return;
        lastLayout=now;
        if(token==0){var start=new Gdi.StartupInput{Version=1};Gdi.Check(Gdi.GdiplusStartup(out token,ref start,0));}
        if(layer==null){layer=new NativeLayer();layer.Pass(true);}
        var dpi=layer.Dpi;var work=DesktopPlatform.Primary.Work;
        var taskbar=location==ThinkingLocation.PrimaryBottom?null:DesktopPlatform.Taskbar();
        bool onBar=false;double x=0,y=0;int w=1,h=1;
        if(taskbar is {} data&&data.Bar.Right-data.Bar.Left>data.Bar.Bottom-data.Bar.Top&&data.Bar.Bottom-data.Bar.Top>=20&&(location!=ThinkingLocation.TrayLeft||data.Tray.HasValue))
        {
            var bar=data.Bar;var right=location==ThinkingLocation.TrayLeft?data.Tray!.Value.Left-8:Math.Min(data.Tray?.Left??bar.Right,bar.Left+Math.Min(440,(bar.Right-bar.Left)*.35));
            var left=bar.Left+8;var available=right-left;
            if(available>=120){onBar=true;w=Math.Max(1,(int)Math.Min(available,740*dpi));h=Math.Max(1,(int)(bar.Bottom-bar.Top-4));x=location==ThinkingLocation.TrayLeft?right-w:left;y=bar.Top+2;}
        }
        if(!onBar)
        {
            w=Math.Max(1,(int)Math.Min(work.Right-work.Left-16,740*dpi));
            double limit=Math.Max(1,(work.Bottom-work.Top)*.6);
            h=Math.Max(1,(int)Math.Min(limit,Math.Max(44,items.Values.Max(i=>(Math.Max(0,i.Message.Style.Offset)+i.Message.Style.Size*3+12)*dpi))));
            x=work.Left+(work.Right-work.Left-w)/2;y=work.Bottom-h-6;
        }
        var pixels=new byte[checked(w*h*4)];var pinned=GCHandle.Alloc(pixels,GCHandleType.Pinned);
        nint bitmap=0,graphics=0,family=0;
        try
        {
            Gdi.Check(Gdi.GdipCreateBitmapFromScan0(w,h,w*4,Gdi.Pargb32,pinned.AddrOfPinnedObject(),out bitmap));
            Gdi.Check(Gdi.GdipGetImageGraphicsContext(bitmap,out graphics));
            Gdi.Check(Gdi.GdipGraphicsClear(graphics,0));
            Gdi.Check(GdipSetTextRenderingHint(graphics,4));
            if(GdipCreateFontFamilyFromName("Microsoft YaHei UI",0,out family)!=0)Gdi.Check(GdipGetGenericFontFamilySansSerif(out family));
            foreach(var item in items.Values.OrderBy(i=>i.Message.Order))
            {
                var style=item.Message.Style.Normalize();
                var fade=item.Removed is long removed?Math.Clamp(1-(now-removed)/160.0,0,1):style.FlashIn?Math.Clamp((now-item.Born)/160.0,0,1):1;
                DrawMessage(graphics,family,item.Message.Text,style,fade,dpi,w,h,onBar,location);
            }
            Gdi.Check(Gdi.GdipFlush(graphics,1));
        }
        finally
        {
            if(family!=0)GdipDeleteFontFamily(family);
            if(graphics!=0)Gdi.GdipDeleteGraphics(graphics);
            if(bitmap!=0)Gdi.GdipDisposeImage(bitmap);
            pinned.Free();
        }
        layer.Paint(pixels,w,h,x,y,1);if(!visible){layer.Show();visible=true;}
    }
    private static void DrawMessage(nint graphics,nint family,string text,ThinkingStyle style,double fade,double dpi,int w,int h,bool onBar,ThinkingLocation location)
    {
        nint font=0,format=0,brush=0,background=0;
        try
        {
            Gdi.Check(GdipCreateFont(family,(float)(style.Size*dpi),0,2,out font));
            Gdi.Check(GdipCreateStringFormat(onBar?0x1000:0,0,out format));
            Gdi.Check(GdipSetStringFormatAlign(format,1));Gdi.Check(GdipSetStringFormatLineAlign(format,1));Gdi.Check(GdipSetStringFormatTrimming(format,3));
            float pad=(float)(9*dpi),vertical=(float)(3*dpi);
            float offset=(float)Math.Clamp(style.Offset*dpi,0,Math.Max(0,(onBar?w-80*dpi:h-24*dpi)));
            float available=Math.Max(1,w-2*pad-(onBar?offset:0));
            var box=new RectF{Width=available,Height=Math.Max(1,h-vertical*2)};
            Gdi.Check(GdipMeasureString(graphics,text,text.Length,font,ref box,format,out var measured,out _,out _));
            float bw=Math.Min(available+2*pad,Math.Max(1,measured.Width+2*pad));
            float bh=Math.Min(h,Math.Max((float)(style.Size*dpi+2*vertical),measured.Height+2*vertical));
            float bx=onBar?(location==ThinkingLocation.TrayLeft?w-offset-bw:offset):(w-bw)/2;
            float by=onBar?(h-bh)/2:Math.Max(0,h-bh-offset);
            if(style.Background)
            {
                Gdi.Check(GdipCreateSolidFill(Alpha(style.BackgroundColor,style.BackgroundAlpha*fade),out background));
                Gdi.Check(GdipFillRectangle(graphics,background,bx,by,bw,bh));
            }
            Gdi.Check(GdipCreateSolidFill(Alpha(style.Color,style.Alpha*fade),out brush));
            var layout=new RectF{X=bx+pad,Y=by+vertical,Width=Math.Max(1,bw-2*pad),Height=Math.Max(1,bh-2*vertical)};
            Gdi.Check(GdipDrawString(graphics,text,text.Length,font,ref layout,format,brush));
        }
        finally
        {
            if(background!=0)GdipDeleteBrush(background);if(brush!=0)GdipDeleteBrush(brush);
            if(format!=0)GdipDeleteStringFormat(format);if(font!=0)GdipDeleteFont(font);
        }
    }
    public void Dispose(){if(disposed)return;disposed=true;Clear();}
    [StructLayout(LayoutKind.Sequential)]private struct RectF{public float X,Y,Width,Height;}
    [DllImport("gdiplus.dll",CharSet=CharSet.Unicode,ExactSpelling=true)]private static extern int GdipCreateFontFamilyFromName(string name,nint collection,out nint family);
    [DllImport("gdiplus.dll")]private static extern int GdipGetGenericFontFamilySansSerif(out nint family);
    [DllImport("gdiplus.dll")]private static extern int GdipDeleteFontFamily(nint family);
    [DllImport("gdiplus.dll")]private static extern int GdipCreateFont(nint family,float size,int style,int unit,out nint font);
    [DllImport("gdiplus.dll")]private static extern int GdipDeleteFont(nint font);
    [DllImport("gdiplus.dll")]private static extern int GdipCreateStringFormat(int flags,int language,out nint format);
    [DllImport("gdiplus.dll")]private static extern int GdipDeleteStringFormat(nint format);
    [DllImport("gdiplus.dll")]private static extern int GdipSetStringFormatAlign(nint format,int align);
    [DllImport("gdiplus.dll")]private static extern int GdipSetStringFormatLineAlign(nint format,int align);
    [DllImport("gdiplus.dll")]private static extern int GdipSetStringFormatTrimming(nint format,int trimming);
    [DllImport("gdiplus.dll")]private static extern int GdipSetTextRenderingHint(nint graphics,int mode);
    [DllImport("gdiplus.dll")]private static extern int GdipCreateSolidFill(uint color,out nint brush);
    [DllImport("gdiplus.dll")]private static extern int GdipDeleteBrush(nint brush);
    [DllImport("gdiplus.dll")]private static extern int GdipFillRectangle(nint graphics,nint brush,float x,float y,float w,float h);
    [DllImport("gdiplus.dll",CharSet=CharSet.Unicode,ExactSpelling=true)]private static extern int GdipMeasureString(nint graphics,string text,int length,nint font,ref RectF layout,nint format,out RectF bounds,out int fitted,out int lines);
    [DllImport("gdiplus.dll",CharSet=CharSet.Unicode,ExactSpelling=true)]private static extern int GdipDrawString(nint graphics,string text,int length,nint font,ref RectF layout,nint format,nint brush);
}
