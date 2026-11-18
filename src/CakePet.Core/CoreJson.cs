using System.Text.Json;
using System.Text.Json.Serialization;
namespace CakePet.Core;

// Explicit metadata keeps catalog, settings and limits compatible with trimming and Native AOT.
// Preserve the existing property names and numeric enum format.
[JsonSourceGenerationOptions(PropertyNameCaseInsensitive=true,GenerationMode=JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(RoleCatalog))]
[JsonSerializable(typeof(RoleSettings))]
[JsonSerializable(typeof(GlobalFeatureLimits))]
[JsonSerializable(typeof(PackEntry))]
[JsonSerializable(typeof(RoleEntry))]
[JsonSerializable(typeof(TypingSettings))]
[JsonSerializable(typeof(Dictionary<string,SettingLimit>),TypeInfoPropertyName="DictionaryStringSettingLimit")]
public partial class CoreJson:JsonSerializerContext
{
    public static CoreJson Indented {get;}=new(new JsonSerializerOptions{PropertyNameCaseInsensitive=true,WriteIndented=true});
}
