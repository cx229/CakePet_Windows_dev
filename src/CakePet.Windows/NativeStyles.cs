using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
namespace CakePet.Windows;

internal static class NativeStyles
{
    private const int GwlExStyle=-20;
    private const long WsExNoActivate=0x08000000,WsExToolWindow=0x80,WsExTransparent=0x20;
    [DllImport("user32.dll",EntryPoint="GetWindowLongPtrW")]private static extern nint GetWindowLongPtr(nint hwnd,int index);
    [DllImport("user32.dll",EntryPoint="SetWindowLongPtrW")]private static extern nint SetWindowLongPtr(nint hwnd,int index,nint value);
    public static void NonActivating(Window window,bool clickThrough)
    {
        var handle=new WindowInteropHelper(window).Handle;if(handle==0)return;
        var old=GetWindowLongPtr(handle,GwlExStyle).ToInt64();
        var next=(old|WsExNoActivate|WsExToolWindow)&~WsExTransparent;
        if(clickThrough)next|=WsExTransparent;
        if(next!=old)SetWindowLongPtr(handle,GwlExStyle,(nint)next);
    }
}
