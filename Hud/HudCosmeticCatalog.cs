using System.Text.Json;
using System.Text.Json.Serialization;

namespace WeaponPaints.Hud;

internal sealed record HudCosmeticEntry
{
    [JsonPropertyName("economyId")] public int EconomyId { get; init; }
    [JsonPropertyName("kind")] public string Kind { get; init; } = "";
    [JsonPropertyName("team")] public int Team { get; init; }
    [JsonPropertyName("id")] public int Id { get; init; }
    [JsonPropertyName("tint")] public int Tint { get; init; }
    [JsonPropertyName("model")] public string? Model { get; init; }
}
internal sealed class HudPetCatalogIdentity
{
    [JsonPropertyName("id")] public int Id { get; init; }
    [JsonPropertyName("stage")] public int Stage { get; init; }
    [JsonPropertyName("variant")] public int Variant { get; init; }
    [JsonPropertyName("label")] public string Label { get; init; } = "";
}
internal sealed class HudCosmeticCatalogDocument
{
    [JsonPropertyName("version")] public int Version { get; init; }
    [JsonPropertyName("entries")] public HudCosmeticEntry[] Entries { get; init; } = [];
    [JsonPropertyName("pets")] public HudPetCatalogIdentity[] Pets { get; init; } = [];
    [JsonPropertyName("hats")] public string[] Hats { get; init; } = [];
}

/// <summary>Immutable site/plugin identities, never reconstructed from artwork or display names.</summary>
internal sealed class HudCosmeticCatalog
{
    private readonly Dictionary<int,HudCosmeticEntry> _entries;
    private readonly HudPetIdentity[] _pets;
    private readonly string[] _hats;
    internal IReadOnlyList<HudPetCatalogIdentity> PetChoices { get; }
    private HudCosmeticCatalog(HudCosmeticCatalogDocument document)
    {
        if(document.Version!=1 || document.Entries.Any(x=>x.EconomyId<0 || x.Id<0 || x.Kind is not ("agent" or "music" or "pin" or "patch" or "graffiti") || (x.Kind=="agent" && (x.Team is not (2 or 3) || string.IsNullOrWhiteSpace(x.Model))) || (x.Kind!="agent" && x.Team!=0) || (x.Kind=="graffiti" && x.Tint<0))) throw new InvalidDataException("Invalid cosmetic metadata");
        _entries=document.Entries.ToDictionary(x=>x.EconomyId);
        PetChoices=Array.AsReadOnly(document.Pets.ToArray());
        _pets=document.Pets.Select(x=>new HudPetIdentity(x.Id,x.Stage,x.Variant)).ToArray();
        _hats=document.Hats.ToArray();
        if(_pets.Length==0 || _hats.Length==0 || _pets.Any(x=>x.Id<0 || x.Stage<1 || x.Variant<0) || _hats.Any(x=>x==null)) throw new InvalidDataException("Invalid pet metadata");
    }
    internal static HudCosmeticCatalog Load(string path) => new(JsonSerializer.Deserialize<HudCosmeticCatalogDocument>(File.ReadAllText(path)) ?? throw new InvalidDataException("Missing cosmetic metadata"));
    internal bool TryGet(int economyId,out HudCosmeticEntry entry) => _entries.TryGetValue(economyId,out entry!);

    // These live inputs come from the already-loaded plugin lists, not client input.
    internal HudCosmeticPolicy CreatePolicy(IReadOnlySet<(int Team,string Model)> runtimeAgents,IReadOnlySet<int> runtimeMusic,IReadOnlySet<int> runtimePins)
    {
        var sets=new Dictionary<HudCosmeticScope,HashSet<HudCosmeticValue>>();
        void Add(HudCosmeticKind kind,int team,HudCosmeticValue value) {
            var key=new HudCosmeticScope(kind,team);if(!sets.TryGetValue(key,out var set)) sets[key]=set=[];set.Add(value);
        }
        foreach(int team in new[]{2,3}) {
            Add(HudCosmeticKind.Agent,team,new HudAgentValue(null));
            Add(HudCosmeticKind.Patch,team,new HudNumberValue(0));
            Add(HudCosmeticKind.Music,team,new HudNumberValue(0));
            Add(HudCosmeticKind.Pin,team,new HudNumberValue(0));
        }
        Add(HudCosmeticKind.Graffiti,0,new HudGraffitiValue(0,0));
        foreach(var e in _entries.Values) switch(e.Kind) {
            case "agent" when e.Model!=null && runtimeAgents.Contains((e.Team,e.Model)): Add(HudCosmeticKind.Agent,e.Team,new HudAgentValue(e.Model));break;
            case "music" when runtimeMusic.Contains(e.Id): foreach(int team in new[]{2,3}) Add(HudCosmeticKind.Music,team,new HudNumberValue(e.Id));break;
            case "pin" when runtimePins.Contains(e.Id): foreach(int team in new[]{2,3}) Add(HudCosmeticKind.Pin,team,new HudNumberValue(e.Id));break;
            case "patch": foreach(int team in new[]{2,3}) Add(HudCosmeticKind.Patch,team,new HudNumberValue(e.Id));break;
            case "graffiti": Add(HudCosmeticKind.Graffiti,0,new HudGraffitiValue(e.Id,e.Tint));break;
        }
        return new HudCosmeticPolicy(sets.ToDictionary(k=>k.Key,v=>(IReadOnlySet<HudCosmeticValue>)v.Value),_pets.ToHashSet(),_hats.ToHashSet(StringComparer.Ordinal));
    }
}
