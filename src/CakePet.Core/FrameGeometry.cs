namespace CakePet.Core;

// Logical square canvas and anchors match Android ImageModeManager.
// Decode resolution is independent from this coordinate system.
public readonly record struct FrameGeometry(int Left,int Top,int Width,int Height,int AnchorX,int AnchorY)
{
    public static FrameGeometry Calculate(int sourceWidth,int sourceHeight,int side,Frame frame)
    {
        if(sourceWidth<=0||sourceHeight<=0||side<=0)throw new ArgumentOutOfRangeException(nameof(side));
        if(frame.Transform is "rotate90" or "rotate270") (sourceWidth,sourceHeight)=(sourceHeight,sourceWidth);
        var scale=(double)side/Math.Max(sourceWidth,sourceHeight);
        var w=Math.Max(1,(int)Math.Floor(sourceWidth*scale+.5));
        var h=Math.Max(1,(int)Math.Floor(sourceHeight*scale+.5));
        var left=(side-w)/2;
        var top=frame.Align switch {"top"=>0,"center"=>(side-h)/2,_=>side-h};
        return new FrameGeometry(left,top,w,h,left+(frame.AnchorX<0?w/2:frame.AnchorX),top+h-(frame.AnchorY??0));
    }
}
