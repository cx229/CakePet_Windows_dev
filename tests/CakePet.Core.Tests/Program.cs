using CakePet.Core;
static void Assert(bool condition,string message){if(!condition)throw new Exception(message);}
var root=Path.Combine(Path.GetTempPath(),"cakepet-"+Guid.NewGuid());
Directory.CreateDirectory(root);
try
{
 File.WriteAllText(Path.Combine(root,"a.png"),"test");
 File.WriteAllText(Path.Combine(root,"modes.jsonc"),"""
 {
 // real v1.12 schema
 "version":"1.12","id":"test","name":"Test","functions":{
 "standby":{"pool":{"modes":["bad","idle"]}},
 "pat_head":{"pat_head":{"modes":["event"],"thinking":{"policy":"hold","pool":{"a":{"min":5,"max":5}}}}},
 "snap":{"pool":{"modes":["idle"]}},"menu":{"pool":{"modes":["event"]}}},
 "modes":{
 "bad":{"frames":[{"res":"../escape.png","delay":1}]},
 "idle":{"loopMode":"infinite","nextMode":"missing","frames":[{"res":"a.png","delay":10,"dx":2}]},
 "event":{"loopMode":"off","nextMode":"tail","frames":[{"res":"a.png","delay":[5,5]}]},
 "tail":{"loopMode":"timed","duration":{"min":12,"max":12},"finishCycle":true,"frames":[{"res":"a.png","delay":10}]}
 },"thinkings":{"a":{"text":"hello"}}
 }
 """);
 var pack=RolePackage.Load(root);
 Assert(pack.Candidates("standby/pool").SequenceEqual(new[]{"idle"}),"optional invalid animation");
 var role=new RoleRuntime(pack,new Random(1));var second=new RoleRuntime(pack,new Random(2));
 Assert(role.Trigger("pat_head/pat_head"),"trigger");
 role.Advance(5);Assert(role.Animation.Mode.Id=="tail"&&role.FunctionPath=="pat_head/pat_head","chain context");
 Assert(role.CurrentThinking.Policy=="hold","thinking inheritance");
 role.Advance(19);Assert(role.Animation.Mode.Id=="tail","finish cycle");
 role.Advance(1);Assert(role.Animation.Mode.Id=="idle","resume standby");
 Assert(second.Animation.Mode.Id=="idle","independent cursor");
 role.SetEnvironment(true,PhysicalState.Free);
 Assert(role.Trigger("menu/pool")&&role.Snapped,"snapped menu event");
 Assert(!role.Trigger("pat_head/pat_head"),"normal event excluded when snapped");
 role.SetEnvironment(false,PhysicalState.Dragging);
 Assert(!role.Trigger("menu/pool"),"physical lock");
 var cursor=new AnimationCursor(pack,"idle",new Random(1));int impulses=0;
 cursor.FrameEntered+=f=>impulses+=f.Dx;cursor.Advance(30);
 Assert(impulses==6&&!cursor.Ended,"loop impulses");
 var geometry=FrameGeometry.Calculate(1000,500,128,new Frame("a",new Interval(1,1),0,0,-1,null,"bottom","none"));
 Assert(geometry.Width==128&&geometry.Height==64&&geometry.Top==64&&geometry.AnchorY==128,"logical canvas geometry");
 var rotated=FrameGeometry.Calculate(1000,500,128,new Frame("a",new Interval(1,1),0,0,-1,10,"top","rotate90"));
 Assert(rotated.Width==64&&rotated.Height==128&&rotated.AnchorX==64&&rotated.AnchorY==118,"rotated geometry");
 Assert(pack.Modes["idle"].Frames[0].Align=="bottom"&&pack.Modes["event"].FinishCycle,"Android defaults");
 var t=new ThinkingTimeline(pack.Texts,new Random(1),new Interval(10,10));
 var cfg=new Thinking("cycle",new Dictionary<string,Interval>{{"a",new Interval(5,5)}});
 t.Apply(cfg);Assert(t.CurrentId=="a","first thinking");t.Advance(5);Assert(t.CurrentId==null,"rest phase");
 t.Advance(10);Assert(t.CurrentId=="a","cycle resumes");t.Apply(cfg with {Policy="hold"});t.Advance(100);Assert(t.CurrentId=="a","hold locks");
 var other=new ThinkingTimeline(pack.Texts,new Random(2),new Interval(10,10));var coordinator=new ThinkingCoordinator{SingleAcrossRoles=true};coordinator.Add(t);coordinator.Add(other);other.Apply(cfg);
 Assert(t.CurrentId=="a"&&t.VisibleText==null&&other.VisibleText=="hello","cross-role display suppression only");
 coordinator.SingleAcrossRoles=false;Assert(t.VisibleText=="hello","suppression clearing");
 var sideBody=new PhysicsBody(new Point(5,50));sideBody.Grab();sideBody.Release(default,new Bounds(0,0,200,200));
 Assert(sideBody.SnappedEdge==Edge.Left&&sideBody.Position.X==0,"snap left");sideBody.Grab();Assert(sideBody.SnappedEdge==Edge.None,"pull out");
 var body=new PhysicsBody(new Point(10,10));body.Grab();body.Release(default);
 for(int i=0;i<100&&body.State!=PhysicalState.Free;i++)body.Advance(.05,new Bounds(0,0,100,100));
 Assert(body.State==PhysicalState.Free&&body.Position.Y==100,"landing");
 if(args.Length>0){var real=RolePackage.Load(args[0]);Assert(real.Version=="1.12"&&real.Candidates("standby/pool").Count>0,"Android template");Console.WriteLine($"Real template: {real.Modes.Count} modes, {real.Functions.Count} functions, {real.Warnings.Count} warnings");}
 Console.WriteLine("Core smoke tests passed.");
}
finally{Directory.Delete(root,true);}
