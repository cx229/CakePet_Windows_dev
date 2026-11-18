using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace CakePet.Windows;

// CHOOSECOLOR is the native dialog previously wrapped by WinForms.ColorDialog.
internal static class NativeColorDialog
{
    private static readonly int[] customColors=Enumerable.Repeat(0x00FFFFFF,16).ToArray();
    private static uint ToColorRef(uint argb)=>((argb>>16)&255)|(argb&0xFF00)|((argb&255)<<16);
    private static uint FromColorRef(uint color)=>0xFF000000|ToColorRef(color);
    internal static bool TryChoose(Window? owner,uint initial,out uint color)
    {
        color=initial;var pin=GCHandle.Alloc(customColors,GCHandleType.Pinned);
        try
        {
            var dialog=new ChooseColorData{
                Size=(uint)Marshal.SizeOf<ChooseColorData>(),
                Owner=owner==null?0:new WindowInteropHelper(owner).Handle,
                Result=ToColorRef(initial),CustomColors=pin.AddrOfPinnedObject(),Flags=0x1|0x2
            };
            if(!ChooseColor(ref dialog))return false;
            color=FromColorRef(dialog.Result);return true;
        }
        finally{pin.Free();}
    }
    [StructLayout(LayoutKind.Sequential)]private struct ChooseColorData
    {
        public uint Size;public nint Owner,Instance;public uint Result;public nint CustomColors;
        public uint Flags;public nint CustomData,Hook,Template;
    }
    [DllImport("comdlg32.dll",EntryPoint="ChooseColorW")]private static extern bool ChooseColor(ref ChooseColorData dialog);
}
