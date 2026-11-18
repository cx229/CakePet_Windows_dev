using System.Runtime.InteropServices;
namespace CakePet.Windows;

internal sealed class NativeMenu
{
    public sealed record Item(string? Label,Action? Run=null,bool Enabled=true,bool Checked=false,IReadOnlyList<Item>? Children=null);
    public static void Show(nint owner,IReadOnlyList<Item> items,Action<Action>? dispatch=null)
    {
        var actions=new List<Action?>();
        nint Build(IReadOnlyList<Item> list)
        {
            var menu=CreatePopupMenu();
            foreach(var item in list)
            {
                if(item.Label==null){AppendMenu(menu,0x800,0,null);continue;}
                uint flags=(item.Enabled?0u:1u)|(item.Checked?8u:0u);
                if(item.Children!=null){var sub=Build(item.Children);AppendMenu(menu,flags|0x10,(nuint)sub,item.Label);}
                else{actions.Add(item.Run);AppendMenu(menu,flags,(nuint)actions.Count,item.Label);}
            }
            return menu;
        }
        var root=Build(items);uint chosen=0;
        try{GetCursorPos(out var p);SetForegroundWindow(owner);chosen=TrackPopupMenu(root,0x100|2,p.X,p.Y,0,owner,0);PostMessage(owner,0,0,0);}
        finally{DestroyMenu(root);}
        if(chosen>0&&chosen<=(uint)actions.Count&&actions[(int)chosen-1] is Action run){if(dispatch==null)run();else dispatch(run);}
    }
    [StructLayout(LayoutKind.Sequential)]private struct Point{public int X,Y;}
    [DllImport("user32.dll")]private static extern nint CreatePopupMenu();
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]private static extern bool AppendMenu(nint menu,uint flags,nuint id,string? label);
    [DllImport("user32.dll")]private static extern bool DestroyMenu(nint menu);
    [DllImport("user32.dll")]private static extern bool GetCursorPos(out Point p);
    [DllImport("user32.dll")]private static extern bool SetForegroundWindow(nint h);
    [DllImport("user32.dll")]private static extern uint TrackPopupMenu(nint menu,uint flags,int x,int y,int reserved,nint h,nint rect);
    [DllImport("user32.dll",EntryPoint="PostMessageW")]private static extern bool PostMessage(nint h,uint msg,nint w,nint l);
}
