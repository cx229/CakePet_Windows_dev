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
 "touch":{"roll":{"modes":["flight"]}},
 "pat_head":{"pat_head":{"modes":["event"],"thinking":{"policy":"hold","pool":{"a":{"min":5,"max":5}}}}},
 "snap":{"pool":{"modes":["idle"]},"feed":{"lure":{"modes":["lure"]},"lure_fail":{"modes":["fail"]},"eat":{"modes":["eat"]}}},
 "feed":{"food":{"modes":["fish"]},"lure":{"modes":["lure"]},"lure_fail":{"modes":["fail"]},"eat":{"modes":["eat"]}},"menu":{"pool":{"modes":["event"]}},
 "climb":{"leap":{"leap":{"modes":["flight"]},"landing":{"modes":["land"]}},"forage":{"move":{"modes":["walk"]}}}},
 "modes":{
 "bad":{"frames":[{"res":"../escape.png","delay":1}]},
 "idle":{"loopMode":"infinite","nextMode":"missing","frames":[{"res":"a.png","delay":10,"dx":2}]},
 "event":{"loopMode":"off","nextMode":"tail","frames":[{"res":"a.png","delay":[5,5]}]},
 "tail":{"loopMode":"timed","duration":{"min":12,"max":12},"finishCycle":true,"frames":[{"res":"a.png","delay":10}]},
 "flight":{"loopMode":"infinite","frames":[{"res":"a.png","delay":10}]},
 "land":{"loopMode":"off","frames":[{"res":"a.png","delay":100}]},
 "fish":{"loopMode":"infinite","frames":[{"res":"a.png","delay":10}]},
 "lure":{"loopMode":"infinite","frames":[{"res":"a.png","delay":10}]},
 "fail":{"loopMode":"off","frames":[{"res":"a.png","delay":20}]},
 "eat":{"loopMode":"off","frames":[{"res":"a.png","delay":30}]},
 "walk":{"loopMode":"infinite","frames":[{"res":"a.png","delay":10,"dx":8}]}
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
 Assert(role.TriggerMode("menu/pool","event")&&role.Snapped&&role.FunctionPath=="menu/pool"&&role.Animation.Mode.Id=="event","snapped named menu event");
 Assert(!role.TriggerMode("menu/pool","idle"),"menu rejects unbound animation");
 Assert(!role.Trigger("pat_head/pat_head"),"normal event excluded when snapped");
 role.SetEnvironment(false,PhysicalState.Dragging);
 Assert(!role.TriggerMode("menu/pool","event"),"named menu physical lock");
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
 var other=new ThinkingTimeline(pack.Texts,new Random(2),new Interval(10,10));var coordinator=new ThinkingCoordinator{Policy=ThinkingDisplayPolicy.Latest};coordinator.Add(t);coordinator.Add(other);other.Apply(cfg);
 Assert(t.CurrentId=="a"&&t.VisibleText==null&&other.VisibleText=="hello","cross-role display suppression only");
 coordinator.Policy=ThinkingDisplayPolicy.Overlap;Assert(t.VisibleText==null,"no interrupted replay on policy change");
 var sideBody=new PhysicsBody(new Point(5,50));sideBody.Grab();sideBody.Release(default,new Bounds(0,0,200,200));
 Assert(sideBody.SnappedEdge==Edge.Left&&sideBody.Position.X==0,"snap left");sideBody.Grab();Assert(sideBody.SnappedEdge==Edge.None,"pull out");
 sideBody.Release(new Point(1000,1000));sideBody.Reset(new Point(50,200),new Bounds(0,0,200,200));
 Assert(sideBody.State==PhysicalState.Free&&sideBody.SnappedEdge==Edge.None&&sideBody.Position.Y==200,"home reset");
 role.ResetToStandby();Assert(role.Physics==PhysicalState.Free&&role.FunctionPath=="standby/pool","role home reset");
 var body=new PhysicsBody(new Point(10,10));body.Grab();body.Release(default);
 for(int i=0;i<100&&body.State!=PhysicalState.Free;i++)body.Advance(.05,new Bounds(0,0,100,100));
 Assert(body.State==PhysicalState.Free&&body.Position.Y==100,"landing");
 Assert(SurfaceMotion.ToScreen(new Point(5,2),Edge.Left)==new Point(-2,5),"surface rotation");
 var wallBody=new PhysicsBody(new Point(50,100));
 Assert(wallBody.TryAttach(Edge.Bottom,new Bounds(0,0,100,100)),"ground attachment");
 Assert(!wallBody.TryAttach(Edge.Left,new Bounds(0,0,100,100)),"no teleport attachment");
 wallBody.MoveFrame(new Point(60,0),new Bounds(0,0,100,100));
 Assert(wallBody.TryAttach(Edge.Right,new Bounds(0,0,100,100))&&wallBody.Position==new Point(100,100),"corner switch");
 using var engine=new PetEngine(new RoleRuntime(pack,new Random(3)),new Bounds(0,0,100,100),128,new Point(50,100),new Random(4));
 engine.SetClimbing(true);engine.SetLeapEnabled(true);
 Assert(engine.Body.State==PhysicalState.Climbing&&engine.Role.Physics==PhysicalState.Climbing,"engine climbing state");
 Assert(!engine.TryStartLeap(Edge.Bottom),"reject launch wall");
 Assert(engine.TryStartLeap(Edge.Top)&&engine.Role.FunctionPath=="climb/leap/leap"&&engine.Body.State==PhysicalState.Flying,"leap physical entry");
 Assert(!engine.PatHead(),"interaction blocked during leap");
 for(int i=0;i<100&&engine.Body.State==PhysicalState.Flying;i++)engine.Step(5);
 Assert(engine.Body.State==PhysicalState.Climbing&&engine.Body.ClimbEdge==Edge.Top&&engine.Role.FunctionPath=="climb/leap/landing","landing interrupts infinite flight");
 engine.BeginDrag();Assert(engine.Body.State==PhysicalState.Dragging&&engine.Body.ClimbEdge==Edge.None,"drag interrupts climbing");
 engine.ReturnHome();Assert(engine.Body.Position.Y==100&&engine.Body.State==PhysicalState.Climbing,"home then attach");
 var noThrow=new PhysicsBody(new Point(50,20)){ThrowEnabled=false,SnapEnabled=false};
 noThrow.Grab();noThrow.Release(new Point(1000,1000),new Bounds(0,0,200,200));
 Assert(noThrow.State==PhysicalState.Free&&noThrow.Velocity==default,"disabled throw falls without rolling");
 noThrow.Advance(.1,new Bounds(0,0,200,200));Assert(noThrow.Position.Y>20,"gravity independent of throw");
 var route=PerimeterRoute.Calculate(new Bounds(0,0,400,200),new Point(100,200),Edge.Bottom,new Point(350,200));
 Assert(route.Direction==1&&route.Distance==250,"perimeter direction");
 using var feeder=new PetEngine(new RoleRuntime(pack,new Random(7)),new Bounds(0,0,400,200),100,new Point(50,200));
 using var eater=new PetEngine(new RoleRuntime(pack,new Random(8)),new Bounds(0,0,400,200),100,new Point(250,200));
 var world=new FoodWorld(new Random(9));world.Add(feeder);world.Add(eater);
 feeder.GrowOnFeed=true;feeder.FeedGrowth=10;
 var fish=world.Spawn(feeder,new Bounds(0,0,500,300),40,1000,false)!;
 var fish2=world.Spawn(feeder,new Bounds(0,0,500,300),40,1000,false)!;
 Assert(fish.Animation!=fish2.Animation,"independent food animation");
 world.Hold(fish);world.Move(fish,eater.Center);
 Assert(eater.IsLuring&&eater.Role.FunctionPath=="feed/lure","held food lure");
 world.Release(fish,new Point(0,0));
 Assert(eater.Role.FunctionPath=="feed/lure_fail"&&!eater.IsLuring,"miss event");
 eater.Role.Advance(20);world.Hold(fish);world.Move(fish,eater.Center);
 Assert(world.Release(fish,eater.Center)&&!world.Foods.Contains(fish),"food consumed");
 Assert(eater.Role.FunctionPath=="feed/eat"&&eater.DisplaySize==110&&feeder.DisplaySize==100,"cross-role grow and eat");
 world.Hold(fish2);eater.BeginDrag();world.UpdateInteractions();
 Assert(!eater.IsLuring,"drag cancels lure");
 eater.ReturnHome();world.Release(fish2,new Point(0,0),true);world.Advance(1000);
 Assert(world.Foods.Count==0,"expiry");
 eater.SetClimbing(true);eater.SetAutoForage(true);
 var target=world.Spawn(feeder,new Bounds(0,0,500,300),40,null,false)!;
 world.Hold(target);world.Move(target,new Point(450,290));world.Release(target,new Point(0,0),true);
 Assert(eater.ChooseForage!()&&eater.Foraging,"forage natural selection hook");
 var before=eater.Body.Position;eater.Step(10);Assert(eater.Body.Position!=before,"forage frame movement");
 world.Remove(feeder);Assert(world.Foods.Count==0&&!eater.Foraging,"owner recall clears owned food");
 world.Add(feeder);world.SetAutomatic(feeder,true,new Interval(100,100),()=>world.Spawn(feeder,new Bounds(0,0,500,300),40,null,false,1));
 world.Advance(99);Assert(world.Foods.Count==0,"spawn waits");world.Advance(1);Assert(world.Foods.Count==1,"scheduled spawn");
 world.Advance(100);Assert(world.Foods.Count==1,"spawn cap");world.SetAutomatic(feeder,false,new Interval(100,100),()=>null);Assert(world.Foods.Count==0,"stop clears owner food");
 var shaped=world.Spawn(feeder,new Bounds(0,0,500,300),40,null,true,3,_=>new FoodExtent(80,20))!;
 Assert(shaped.Width==80&&shaped.Height==20&&
  (shaped.Center.X==40||shaped.Center.X==460||shaped.Center.Y==10||shaped.Center.Y==290),"actual cropped edge bounds");
 world.Remove(shaped);
 eater.BeginDrag();eater.Body.SnapDistance=100;eater.EndDrag(default);
 Assert(eater.Role.Snapped,"snap for food");
 var snapFish=world.Spawn(feeder,new Bounds(0,0,500,300),40,null,false)!;
 var otherFish=world.Spawn(feeder,new Bounds(0,0,500,300),40,null,false)!;
 world.Hold(snapFish);Assert(eater.Role.FunctionPath=="snap/feed/lure","snap lure independent");
 world.Hold(otherFish);world.Release(snapFish,new Point(0,0),true);
 Assert(eater.IsLuring&&eater.Role.FunctionPath=="snap/feed/lure","one released while other held");
 world.Release(otherFish,new Point(0,0),allowConsume:false);
 Assert(eater.Role.FunctionPath=="snap/feed/lure_fail","last held release");
 world.Clear();
 eater.ReturnHome();eater.BeginDrag();eater.Body.SnapEnabled=false;eater.EndDrag(new Point(1000,-500));
 var oldVelocity=eater.Body.Velocity;eater.Grow(5);
 Assert(oldVelocity.Length>0&&eater.Body.Velocity==oldVelocity&&eater.Body.State==PhysicalState.Throwing,"resize preserves physics");
 var screens=new[]{new DesktopScreen("left",new Bounds(-1920,0,0,1080),new Bounds(-1920,0,0,1040),false),new DesktopScreen("primary",new Bounds(0,0,2560,1440),new Bounds(0,0,2560,1400),true)};
 Assert(DesktopGeometry.Nearest(screens,new Point(-100,50)).Id=="left","negative monitor origin");
 Assert(DesktopGeometry.Nearest(screens,new Point(200,100)).Id=="primary","monitor transition");
 Assert(DesktopGeometry.Fit(screens[0].Work,160,160)==new Bounds(-1920,0,-160,880),"physical work area");
 var area=new Bounds(-1000,0,0,800);
 var foot=new Point(80,120);
 Assert(AnchorGeometry.WindowBounds(area,160,foot,Edge.Bottom).Bottom==680,"anchor bottom overhang");
 var leftFoot=AnchorGeometry.Transform(foot,160,Edge.Left,1);
 Assert(leftFoot==new Point(40,80)&&AnchorGeometry.WindowBounds(area,160,leftFoot,Edge.Left).Left==-1040,"rotated left anchor");
 foreach(var side in SurfaceMotion.Sides)foreach(var face in new[]{-1,1})
 Assert(AnchorGeometry.Inverse(AnchorGeometry.Transform(new Point(40,70),160,side,face),160,side,face)==new Point(40,70),"inverse hit geometry");
 using var anchored=new PetEngine(new RoleRuntime(pack),new Bounds(0,0,800,600),128,new Point(200,200));
 anchored.SetActivityArea(new Bounds(0,0,800,600),_=>new FrameGeometry(0,0,128,128,64,96));
 var pos=anchored.Body.Position;anchored.RefreshGeometry();Assert(anchored.Body.Position==pos,"anchor no interior jump");
 anchored.ReturnHome();Assert(anchored.Body.Position.Y+anchored.AnchorOffset.Y==600,"home foot contact");
 anchored.Resize(160);Assert(anchored.Body.Position.Y+anchored.AnchorOffset.Y==600,"resized foot contact");
 var catalog=new RoleCatalog();var packEntry=new PackEntry(Guid.NewGuid(),"test","test");catalog.Packs.Add(packEntry);
 var firstRole=catalog.AddRole(packEntry.Id,"A");var anotherRole=catalog.AddRole(packEntry.Id,"B");
 catalog.Update(firstRole with{Settings=firstRole.Settings with{Count=3,Scale=2}});
 var catalogPath=Path.Combine(root,"catalog.json");catalog.Save(catalogPath);var restored=RoleCatalog.Load(catalogPath);
 Assert(restored.Roles.Count==2&&restored.Roles[0].Settings.Count==3&&restored.Roles[1].Settings.Count==1,"independent persistent roles");
 Assert(restored.Roles.All(x=>x.PackId==packEntry.Id),"shared pack references");
 var custom=new RoleSettings{FoodMinSeconds=80,FoodMaxSeconds=20,Gravity=-1,SnapEdges=(Edge)255,Scale=2,MaxScale=1}.Normalize();
 Assert(custom.FoodMaxSeconds==80&&custom.Gravity==0&&custom.SnapEdges==Edge.All&&custom.MaxScale>=custom.Scale,"settings normalization");
 catalog.Defaults=new RoleSettings{Count=4,Gravity=900};var future=catalog.AddRole(packEntry.Id,"future");
 Assert(future.Settings.Count==4&&future.Settings.Gravity==900&&catalog.Roles[0].Settings.Count==3,"defaults only new roles");
 var effective=new RoleSettings{Move=false,Throw=true}.Effective(pack);
 Assert(effective.Move,"missing static pool forces motion");
 var imported=ArchiveIO.Overlay(new RoleSettings{Gravity=900,Count=2},new System.Text.Json.Nodes.JsonObject{["Gravity"]="invalid",["Count"]=4,["unknown"]=true});
 Assert(imported.Gravity==900&&imported.Count==4,"partial settings overlay");
 var editor=new PackageEditor(root);var oldDoc=File.ReadAllText(Path.Combine(root,"modes.jsonc"));
 var oldMode="idle";editor.RenameMode(oldMode,"renamed");
 Assert(!editor.Modes.ContainsKey(oldMode)&&editor.Modes.ContainsKey("renamed"),"animation rename");
 Assert(!editor.FunctionPaths().SelectMany(p=>editor.Function(p)["modes"]!.AsArray()).Any(x=>x?.ToString()==oldMode),"rename references");
 editor.Save();Assert(RolePackage.Load(root).Modes.ContainsKey("renamed"),"editor save");
 editor.SetPool("standby/pool",Array.Empty<string>());bool rejected=false;
 try{editor.Save();}catch(InvalidDataException){rejected=true;}
 Assert(rejected&&RolePackage.Load(root).Candidates("standby/pool").Count>0,"invalid save rollback");
 var archivePath=Path.Combine(root,"unsafe.zip");ArchiveIO.Zip(archivePath,z=>ArchiveIO.Text(z,"../escape.txt","bad"));
 rejected=false;try{ArchiveIO.Extract(archivePath,Path.Combine(root,"extract"));}catch(InvalidDataException){rejected=true;}Assert(rejected,"ZIP path traversal rejected");
 var regions=new[]{new ScreenRegion("L",new Bounds(-800,0,0,600),new Bounds(-800,0,0,560),new Bounds(-800,0,-100,460)),new ScreenRegion("R",new Bounds(0,0,800,600),new Bounds(0,0,800,560),new Bounds(0,0,700,460))};
 var anchor=new Point(50,100);
 var extended=ConnectedDesktop.Limits(regions,regions[0],new Point(-60,460),anchor,Edge.Bottom);
 Assert(extended.Right==-50,"open seam extends to anchor");
 var crossing=ConnectedDesktop.Project(regions,regions[0],new Point(-60,460),new Point(-40,460),anchor,Edge.Bottom);
 Assert(crossing.Region.Id=="R"&&crossing.Position.X==-40&&crossing.Position.Y==460,"walk across seam");
 var wrapped=ConnectedDesktop.Project(regions,regions[1],new Point(740,460),new Point(760,460),anchor,Edge.Bottom);
 Assert(wrapped.Region.Id=="L"&&wrapped.Position.X==-840,"horizontal wrap");
 var offsetRegions=new[]{regions[0],regions[1] with{Area=new Bounds(0,200,800,760),Full=new Bounds(0,200,800,800),Limits=new Bounds(0,200,700,660)}};
 Assert(!ConnectedDesktop.OpenEdges(offsetRegions,offsetRegions[0],new Point(-100,50),Edge.None).HasFlag(Edge.Right),"no crossing into missing screen space");
 var moving=new PhysicsBody(new Point(-60,100)){GravityEnabled=false,ThrowEnabled=true};moving.Grab();moving.GravityEnabled=true;moving.Release(new Point(1000,0));moving.GravityEnabled=false;
 var currentRegion=regions[0];moving.ProjectMotion=(from,next)=>{var tr=ConnectedDesktop.Project(regions,currentRegion,from,next,anchor,Edge.None);currentRegion=tr.Region;return new MotionProjection(tr.Position,tr.Limits);};
 moving.Advance(.03,regions[0].Limits);Assert(currentRegion.Id=="R"&&moving.Velocity.X==1000,"throw crosses without bounce");
 var typing=new TypingThinking();var typingConfig=new TypingSettings{Enabled=true,Seconds=5};typing.Restore("2026-11-17",10,"2026-11-17");var arbitration=new ThinkingCoordinator{Policy=ThinkingDisplayPolicy.Latest};var speaker=new ThinkingTimeline(pack.Texts,new Random(1));arbitration.Add(typing);arbitration.Add(speaker);
 typing.Press(0,"2026-11-17",typingConfig);Assert(typing.Today==11&&!typing.Suppressed&&typing.CurrentText!=null,"typing event");
 speaker.Apply(cfg);Assert(typing.Suppressed&&speaker.VisibleText!=null,"role interrupts typing");
 typing.Press(1000,"2026-11-17",typingConfig);Assert(!typing.Suppressed&&speaker.Suppressed&&typing.Today==12,"typing refresh interrupts role");
 typing.Advance(5999);Assert(typing.CurrentText!=null,"typing refresh extends duration");typing.Advance(6000);Assert(typing.CurrentText==null&&speaker.Suppressed,"no interrupted replay");
 typing.Press(62000,"2026-11-18",typingConfig);Assert(typing.Today==1&&typing.CurrentText!.Contains("1 次/分钟"),"rolling count and new day");
 typing.Stop();typing.Press(63000,"2026-11-18",typingConfig with{Enabled=false});Assert(typing.Today==1&&typing.CurrentText==null,"disabled no counting");
 var layered=new ThinkingCoordinator{Policy=ThinkingDisplayPolicy.Overlap};var x1=new TypingThinking();var x2=new TypingThinking();var sameGroup=Guid.NewGuid();layered.Add(x1,sameGroup);layered.Add(x2,sameGroup);
 x1.Press(0,"2026-11-17",typingConfig);x2.Press(1,"2026-11-17",typingConfig);Assert(!x1.Suppressed&&!x2.Suppressed&&layered.Order(x2)>layered.Order(x1),"overlap ordering");
 layered.Policy=ThinkingDisplayPolicy.Latest;Assert(x1.Suppressed&&!x2.Suppressed,"latest one even same group");
 x1.Press(2,"2026-11-17",typingConfig);Assert(!x1.Suppressed&&x2.Suppressed,"latest refresh");
 layered.Policy=ThinkingDisplayPolicy.Overlap;Assert(x2.Suppressed,"overlap no revival");
 layered.Policy=ThinkingDisplayPolicy.None;x2.Press(3,"2026-11-17",typingConfig);Assert(x1.Suppressed&&x2.Suppressed,"global none");
 layered.Policy=ThinkingDisplayPolicy.Overlap;Assert(x1.Suppressed&&x2.Suppressed,"global resume no revival");
 x2.Press(4,"2026-11-17",typingConfig);Assert(!x2.Suppressed&&x1.Suppressed,"new event after resume");
 var paused=new ThinkingTimeline(pack.Texts,new Random(1));paused.Apply(cfg);paused.Enabled=false;paused.Advance(60000);Assert(paused.CurrentText==null&&paused.CurrentId==null,"disabled timeline stopped");paused.Enabled=true;Assert(paused.CurrentText==null,"resume cycle starts at rest");
 var randomConfig=new RandomSettings{Enabled=true,Items=RandomItems.VerticalGravity|RandomItems.HorizontalGravity,MinMinutes=1,MaxMinutes=1};var random=new Random(42);var baseSettings=new RoleSettings{Scale=1.4,GravityEdges=Edge.None};
 for(int i=0;i<100;i++){var v=randomConfig.Apply(baseSettings,random);Assert(v.Scale==baseSettings.Scale&&v.GravityEdges!=Edge.None&&!(v.GravityEdges.HasFlag(Edge.Top)&&v.GravityEdges.HasFlag(Edge.Bottom))&&!(v.GravityEdges.HasFlag(Edge.Left)&&v.GravityEdges.HasFlag(Edge.Right)),"random direction constraints");}
 var schedule=new RandomSchedule(random);var randomId=Guid.NewGuid();Assert(!schedule.Due(randomId,randomConfig,0)&&!schedule.Due(randomId,randomConfig,59999)&&schedule.Due(randomId,randomConfig,60000),"random schedule");
 Assert(!schedule.Due(randomId,randomConfig with{Enabled=false},120000)&&!schedule.Due(randomId,randomConfig,120000),"random disable and restart");
 var limited=SettingLimits.Clamp(new RoleSettings{Scale=2,Limits=new(){["Scale"]=new(.8,1.5)}});Assert(limited.Scale==1.5,"setting threshold clamps value");
 var habit=SettingLimits.ApplyGroup(new RoleSettings{Gravity=900,Count=3},limited with{Count=2},"显示与交互");Assert(habit.Count==2&&habit.Scale==1.5&&habit.Gravity==900&&habit.Limits.ContainsKey("Scale"),"habit group applies values and thresholds only");
 var styleLimited=SettingLimits.Clamp(new RoleSettings{ThoughtStyle=new ThinkingStyle{Size=30},Limits=new(){["ThoughtStyle.Size"]=new(12,20)}});Assert(styleLimited.ThoughtStyle.Size==20,"thought style threshold");
 var itemApplied=SettingLimits.ApplyItem(new RoleSettings{Count=5,Scale=2},limited,"Scale");Assert(itemApplied.Count==5&&itemApplied.Scale==1.5&&itemApplied.Limits.ContainsKey("Scale"),"single habit item");
 var styleApplied=SettingLimits.ApplyItem(new RoleSettings{ThoughtStyle=new ThinkingStyle{Alpha=.3}},styleLimited,"ThoughtStyle.Size");Assert(styleApplied.ThoughtStyle.Size==20&&styleApplied.ThoughtStyle.Alpha==.3,"nested habit item");
 if(args.Length>0){var real=RolePackage.Load(args[0]);Assert(real.Version=="1.12"&&real.Candidates("standby/pool").Count>0,"Android template");var menuModes=real.Candidates("menu/pool");Assert(menuModes.Count>1,"Android menu pool");var menuRole=new RoleRuntime(real,new Random(7));foreach(var mode in menuModes){Assert(menuRole.TriggerMode("menu/pool",mode)&&menuRole.Animation.Mode.Id==mode,"exact Android menu mode "+mode);}Console.WriteLine($"Real template: {real.Modes.Count} modes, {real.Functions.Count} functions, {real.Warnings.Count} warnings");}
 Console.WriteLine("Core smoke tests passed.");
}
finally{Directory.Delete(root,true);}
