using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CakePet.Core;

namespace CakePet.Windows;

internal sealed class DebugWindow : Window
{
    private readonly TextBlock text=new(){FontFamily=new FontFamily("Consolas"),FontSize=12,Foreground=Brushes.White,TextWrapping=TextWrapping.Wrap};
    public DebugWindow(string name)
    {
        Title=name+" · 实时调试";Width=480;SizeToContent=SizeToContent.Height;MaxHeight=800;
        WindowStyle=WindowStyle.None;ResizeMode=ResizeMode.NoResize;AllowsTransparency=true;Background=Brushes.Transparent;
        ShowInTaskbar=false;Topmost=true;
        var panel=new StackPanel();
        var header=new DockPanel{Margin=new Thickness(0,0,0,8)};
        var close=new Button{Content="×",Width=28,Height=24};close.Click+=(_,_)=>Close();DockPanel.SetDock(close,Dock.Right);header.Children.Add(close);
        var label=new TextBlock{Text=Title,Foreground=Brushes.White,FontWeight=FontWeights.SemiBold,VerticalAlignment=VerticalAlignment.Center};header.Children.Add(label);
        label.MouseLeftButtonDown+=(_,e)=>{if(e.ButtonState==System.Windows.Input.MouseButtonState.Pressed)DragMove();};
        panel.Children.Add(header);panel.Children.Add(text);
        Content=new Border{Background=new SolidColorBrush(Color.FromArgb(225,25,28,33)),Padding=new Thickness(12),CornerRadius=new CornerRadius(8),Child=panel};
    }
    public void Refresh(PetEngine e,string monitor,double dpi,int foodCount,bool pass,bool pixel,Guid instanceId,int number,double baseScale,double actualScale,int count)
    {
        var r=e.Role;var a=r.Animation;var b=e.Body;var f=a.Current;var p=b.Position;var v=b.Velocity;var box=e.Bounds;var anchor=e.AnchorOffset;
        text.Text=$"实例：{number}/{count} · {instanceId.ToString("N")[..8]} · 基础 {baseScale:F2} / 当前 {actualScale:F2} 倍\n"+
            $"素材包：{r.Package.Name} · {r.Package.Version}\n"+
            $"功能：{r.FunctionPath}\n持续功能：{r.StateFunction??"—"}\n"+
            $"动画链：[{string.Join(" > ",a.Chain)}]\n"+
            $"当前动画：{a.Mode.Id} · {a.FrameIndex+1}/{a.Mode.Frames.Count} 帧\n"+
            $"播放：{a.Mode.Loop} · 帧 {a.FrameElapsed}/{a.FrameDuration}ms · {(a.Ended?"已结束":"播放中")}\n"+
            $"动画计时：{a.ModeElapsed}ms / {a.TargetDuration?.ToString()??"—"} · 后续 {a.Mode.Next??"—"}\n"+
            $"图片：{f.Resource}\n帧参数：dx={f.Dx}, dy={f.Dy}, 锚点={f.AnchorX},{f.AnchorY} · {f.Align}/{f.Transform}\n"+
            $"物理：{b.State} / {r.Physics} · 吸附 {b.SnappedEdge} · 攀爬 {b.ClimbEdge}\n"+
             $"飞跃：起跳 {b.LeapLaunchEdges} {e.FlightLaunchAnchor?.ToString()??"—"}\n"+
            $"飞跃目标：{b.LeapTargetEdge} {b.LeapTarget?.ToString()??"—"} · 碰撞 {e.LastFlightImpact}\n"+
            $"位置：({p.X:F1}, {p.Y:F1}) · 速度 ({v.X:F1}, {v.Y:F1}) px/s\n"+
            $"锚点：({p.X+anchor.X:F1}, {p.Y+anchor.Y:F1}) · 大小 {e.DisplaySize:F1}px\n"+
            $"边界：({box.Left:F0}, {box.Top:F0}) — ({box.Right:F0}, {box.Bottom:F0})\n"+
            $"重力：{b.GravitySurface} · 朝向 {e.RenderEdge} · 镜像 {e.Facing}\n"+
            $"屏幕：{monitor} · DPI {dpi*96:F0} · 穿透 {pass} · 像素命中 {pixel}\n"+
            $"食物：{foodCount} · 觅食 {e.Foraging} · 吸引 {e.IsLuring}\n"+
            $"碎碎念：{r.CurrentThinking.Policy} · {r.Thoughts.CurrentId??"—"} · 隐藏 {r.Thoughts.Suppressed}";
    }
}
