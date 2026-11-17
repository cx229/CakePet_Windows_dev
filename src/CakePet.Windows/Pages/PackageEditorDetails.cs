using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using CakePet.Core;
namespace CakePet.Windows;
internal sealed partial class PackageEditorPage
{
    private static void UpdateCaption(StackPanel panel,string note)
    {
        if(panel.Children.Count==1)panel.Children.Add(Ui.Text(note,12,true));else ((TextBlock)panel.Children[1]).Text=note;
    }
    private bool TrySetFunctionPool(string path,IEnumerable<string> ids)
    {
        var result=ids.Distinct().ToArray();var pack=editor.Preview();
        if(path=="standby/pool"&&!result.Any(id=>pack.Modes.ContainsKey(id)&&ValidChain(pack,id))){MessageBox.Show("待机池至少需要一个有效动画。");return false;}
        editor.SetPool(path,result);Changed();return true;
    }
    private static bool ValidChain(RolePackage pack,string id)
    {
        var visited=new HashSet<string>();string? next=id;while(next!=null&&visited.Add(next)){if(!pack.Modes.TryGetValue(next,out var mode))return false;if(mode.Loop==LoopKind.Infinite)return true;next=mode.Next;}return true;
    }
    private string BoundFunctions(string id)
    {
        var paths=editor.FunctionPaths().Where(path=>editor.Function(path)["modes"]!.AsArray().Any(x=>x?.ToString()==id)).Select(FunctionTitle).ToArray();return paths.Length==0?"未绑定":string.Join(" · ",paths);
    }
    private bool EditFunctionBindings(string id)
    {
        var paths=requiredPaths.Concat(editor.FunctionPaths()).Distinct().ToArray();var picked=paths.Where(path=>editor.Function(path)["modes"]!.AsArray().Any(x=>x?.ToString()==id)).ToHashSet();
        var dialog=EditorDialog("绑定到功能",600,620);var list=new StackPanel();var state=new HashSet<string>();
        foreach(var group in paths.GroupBy(x=>requiredPaths.Contains(x)?x.Split('/')[0]:"未定义"))
        {
            var body=new StackPanel();var folder=ResourceFolder(group.Key,FunctionTitle(group.Key),group.Count()+" 个功能",body,state:state);
            LoadOnExpand(folder,()=>{foreach(var path in group){var check=ResourceCheck(picked.Contains(path));check.IsEnabled=requiredPaths.Contains(path);check.Click+=(_,_)=>{if(check.IsChecked==true)picked.Add(path);else picked.Remove(path);};body.Children.Add(Ui.Card(Ui.Pair(Ui.Caption(FunctionTitle(path)),check)));}});list.Children.Add(folder);
        }
        DialogBody(dialog,list,()=>{
            var updates=paths.Where(requiredPaths.Contains).Select(path=>(Path:path,Old:editor.Function(path)["modes"]!.AsArray().Select(x=>x?.ToString()??"").ToArray())).Where(x=>x.Old.Contains(id)!=picked.Contains(x.Path)).Select(x=>(x.Path,Ids:picked.Contains(x.Path)?x.Old.Append(id).Distinct().ToArray():x.Old.Where(n=>n!=id).ToArray())).ToArray();
            var pack=editor.Preview();if(updates.Any(x=>x.Path=="standby/pool"&&!x.Ids.Any(n=>ValidChain(pack,n)))){MessageBox.Show("待机池至少需要一个有效动画。");return;}
            var impact=updates.Where(x=>x.Ids.Length==0).Select(x=>FunctionTitle(x.Path)).ToArray();if(impact.Length>0&&MessageBox.Show("以下功能将失去所有绑定动画："+Environment.NewLine+string.Join(Environment.NewLine,impact),"确认解绑",MessageBoxButton.YesNo)!=MessageBoxResult.Yes)return;
            foreach(var update in updates)editor.SetPool(update.Path,update.Ids);if(updates.Length>0)Changed();dialog.DialogResult=true;
        });return dialog.ShowDialog()==true;
    }
    private bool EditThinkingPool(JsonObject node)
    {
        var draft=(node["thinking"]?["pool"] as JsonObject)?.DeepClone().AsObject()??new JsonObject();var dialog=EditorDialog("编辑碎碎念",620,600);var page=new DockPanel();var list=new StackPanel();var top=Ui.Pair(Ui.Caption("候选文本","选择文本并调整每条文本的播放时长。"),Ui.Button("选择文本 ›",()=>{}));var button=(Button)top.Children[1];DockPanel.SetDock(top,Dock.Top);page.Children.Add(top);page.Children.Add(list);
        void Refresh(){list.Children.Clear();foreach(var pair in draft.ToArray()){var id=pair.Key;var lo=ReadInt(pair.Value?["min"],500);var hi=Math.Max(lo,ReadInt(pair.Value?["max"],4000));var label=Ui.Caption(editor.Thinkings[id]?["text"]?.ToString()??"缺失文本 · "+id,lo+"–"+hi+" ms");var actions=new StackPanel{Orientation=Orientation.Horizontal};actions.Children.Add(Ui.Light("时长 ›",()=>{var result=EditInterval("碎碎念播放时长",lo,hi,0);if(result is {} value){draft[id]=new JsonObject{["min"]=value.Min,["max"]=value.Max};UpdateCaption(label,value.Min+"–"+value.Max+" ms");}}));actions.Children.Add(Ui.Light("移除",()=>{draft.Remove(id);Refresh();}));list.Children.Add(Ui.Card(Ui.Pair(label,actions)));}}
        button.Click+=(_,_)=>{var result=PickResources("碎碎念",draft.Select(x=>x.Key),true);if(result==null)return;var next=new JsonObject();foreach(var id in result)next[id]=draft[id]?.DeepClone()??new JsonObject{["min"]=500,["max"]=4000};draft=next;Refresh();};Refresh();
        DialogBody(dialog,page,()=>{if(node["thinking"] is not JsonObject config){config=new JsonObject{["policy"]="cycle"};node["thinking"]=config;}config["pool"]=draft;Changed();dialog.DialogResult=true;});return dialog.ShowDialog()==true;
    }
}
