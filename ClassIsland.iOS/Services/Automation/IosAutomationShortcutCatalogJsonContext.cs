using System.Text.Json.Serialization;
using ClassIsland.Services.Automation;

namespace ClassIsland.iOS.Services.Automation;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(IosAutomationShortcutCatalogEntry[]))]
internal partial class IosAutomationShortcutCatalogJsonContext : JsonSerializerContext;
