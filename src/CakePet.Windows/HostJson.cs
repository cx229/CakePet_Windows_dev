using System.Text.Json.Serialization;
namespace CakePet.Windows;
[JsonSourceGenerationOptions(GenerationMode=JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(HostRequest))]
[JsonSerializable(typeof(HostReply))]
internal partial class HostJson:JsonSerializerContext {}
