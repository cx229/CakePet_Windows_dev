using System.Runtime.InteropServices;
namespace CakePet.Windows;

// Opt-in raw-input sink. Inspect only the keyboard make/break flag; never read
// the key identity, translate a key, retain input content or send it elsewhere.
internal sealed class TypingInput:IDisposable
{
    [StructLayout(LayoutKind.Sequential)]private struct Device{public ushort Page,Usage;public uint Flags;public nint Target;}
    [DllImport("user32.dll",SetLastError=true)]private static extern bool RegisterRawInputDevices(Device[] devices,uint count,uint size);
    [DllImport("user32.dll")]private static extern uint GetRawInputData(nint input,uint command,nint data,ref uint size,uint headerSize);
    private readonly Action pressed;private NativeLayer? source;
    public TypingInput(Action pressed)=>this.pressed=pressed;
    public bool Active=>source!=null;
    public void SetEnabled(bool enabled)
    {
        if(enabled==Active)return;
        if(!enabled){Dispose();return;}
        var candidate=new NativeLayer();candidate.RawInput+=OnMessage;
        if(!RegisterRawInputDevices(new[]{new Device{Page=1,Usage=6,Flags=0x100,Target=candidate.Handle}},1,(uint)Marshal.SizeOf<Device>()))
        {candidate.Dispose();throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());}
        source=candidate;
    }
    private void OnMessage(nint lParam)
    {
        uint bytes=0;uint header=(uint)(8+2*IntPtr.Size);
        if(GetRawInputData(lParam,0x10000003,0,ref bytes,header)!=0||bytes<header+16||bytes>4096)return;
        var buffer=Marshal.AllocHGlobal((int)bytes);
        try
        {
            if(GetRawInputData(lParam,0x10000003,buffer,ref bytes,header)==uint.MaxValue)return;
            // RAWINPUTHEADER.dwType == keyboard; RAWKEYBOARD.Flags contains RI_KEY_BREAK.
            if(Marshal.ReadInt32(buffer,0)==1&&(Marshal.ReadInt16(buffer,(int)header+2)&1)==0)pressed();
        }finally{Marshal.FreeHGlobal(buffer);}
        return;
    }
    public void Dispose()
    {
        if(source==null)return;
        RegisterRawInputDevices(new[]{new Device{Page=1,Usage=6,Flags=1,Target=0}},1,(uint)Marshal.SizeOf<Device>());
        source.RawInput-=OnMessage;source.Dispose();source=null;
    }
}
