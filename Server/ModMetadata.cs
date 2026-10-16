using SPTarkov.Server.Core.Models.Spt.Mod;

namespace K2Original;

public record ModMetadata : IModMetadata
{
    public string ModGuid { get; init; } = "com.rf.k2projectoriginal";
    public string Name { get; init; } = "K2-Project-Original";
    public string Author { get; init; } = "R_F";
    public SemanticVersioning.Version Version { get; init; } = new("0.6.1");
    public SemanticVersioning.Range SptVersion { get; init; } = new("~4.1.0");
    public string License { get; init; } = "All rights reserved";
    public bool HasPrepatcher { get; init; } = false;
    public Dictionary<string, SemanticVersioning.Range>? ModDependencies { get; init; } = [];
    public string? Url { get; init; } = "https://github.com/danyhappy564-cmyk/K2-Project-Original";
    public List<string>? Contributors { get; init; } = [];
    public List<string>? Incompatibilities { get; init; } = [];
}
