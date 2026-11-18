using CakePet.Core;
using Frame=CakePet.Core.Frame;
namespace CakePet.Windows;
internal static class NativePetPixels
{
    internal static byte[] Sprite(WpfImageCache images,Frame frame,int side,int anchor,int facing,Edge edge,bool canvasHit)=>images.Native.Get(frame,side,facing,edge,canvasHit);
}
