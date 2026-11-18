using System.Text.Json.Nodes;
using CakePet.Core;
namespace CakePet.Windows;
// Merge only UI edits into the current host catalog: unrelated live changes,
// random settings and typing totals must not be overwritten by a stale UI copy.
internal static class CatalogMerge
{
    public static RoleCatalog Merge(string baseline,string desired,string current)
    {
        var a=JsonNode.Parse(baseline);var b=JsonNode.Parse(desired);var c=JsonNode.Parse(current);
        return RoleCatalog.Parse(Combine(a,b,c)?.ToJsonString()??throw new InvalidDataException());
    }
    private static JsonNode? Combine(JsonNode? a,JsonNode? b,JsonNode? c)
    {
        if(JsonNode.DeepEquals(a,b))return c?.DeepClone();
        if(a is JsonObject ao&&b is JsonObject bo&&c is JsonObject co)
        {
            var result=(JsonObject)co.DeepClone();
            foreach(var key in ao.Select(p=>p.Key).Union(bo.Select(p=>p.Key)))
            {
                if(!bo.ContainsKey(key)){result.Remove(key);continue;}
                result[key]=Combine(ao[key],bo[key],co[key]);
            }
            return result;
        }
        if(a is JsonArray aa&&b is JsonArray ba&&c is JsonArray ca&&Keyed(aa)&&Keyed(ba)&&Keyed(ca))
        {
            string Id(JsonNode? n)=>n!["Id"]!.GetValue<string>();
            var old=aa.ToDictionary(Id);var wanted=ba.ToDictionary(Id);var live=ca.ToDictionary(Id);
            var result=new JsonArray();
            foreach(var item in ba)
            {
                string id=Id(item);
                if(!old.TryGetValue(id,out var before)){result.Add(item?.DeepClone());continue;}
                if(live.TryGetValue(id,out var existing))result.Add(Combine(before,item,existing));
                else if(!JsonNode.DeepEquals(before,item))result.Add(item?.DeepClone());
            }
            foreach(var item in ca)if(!old.ContainsKey(Id(item))&&!wanted.ContainsKey(Id(item)))result.Add(item?.DeepClone());
            return result;
        }
        return b?.DeepClone();
    }
    private static bool Keyed(JsonArray array)=>array.All(n=>n is JsonObject o&&o["Id"] is JsonValue);
}
