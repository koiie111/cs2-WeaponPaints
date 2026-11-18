using System.Text.Json;

namespace WeaponPaints.Hud;

/// <summary>Authored values generated from the exact versioned native XML.</summary>
internal sealed class HudLayoutContract
{
    private sealed class Document
    {
        public int Version { get; set; }
        public Dictionary<string, string[]> AuthoredClasses { get; set; } = new(StringComparer.Ordinal);
        public string[] Variables { get; set; } = [];
        public string[] Buttons { get; set; } = [];
    }
    private readonly Dictionary<string, HashSet<string>> _classes;
    private readonly HashSet<string> _variables;
    internal IReadOnlySet<string> Buttons { get; }
    private HudLayoutContract(Document doc)
    {
        if (doc.Version != 1 || !doc.AuthoredClasses.ContainsKey("wp_root")) throw new InvalidDataException("Bad HUD contract");
        _classes = doc.AuthoredClasses.ToDictionary(x => x.Key, x => x.Value.ToHashSet(StringComparer.Ordinal), StringComparer.Ordinal);
        _variables = doc.Variables.ToHashSet(StringComparer.Ordinal);
        Buttons = doc.Buttons.ToHashSet(StringComparer.Ordinal);
    }
    internal static HudLayoutContract Load(string path)
    {
        using var stream = File.OpenRead(path);
        var doc = JsonSerializer.Deserialize<Document>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        return new HudLayoutContract(doc ?? throw new InvalidDataException("Empty HUD contract"));
    }
    internal string Authored(HudWriteKey key)
    {
        if (key.Variable) return ""; // layout dialog variables have no authored runtime value
        return _classes.TryGetValue(key.Panel, out var classes) && classes.Contains(key.Name) ? "1" : "0";
    }
    internal bool HasPanel(string id) => _classes.ContainsKey(id);
    internal bool HasVariable(string name) => _variables.Contains(name);
    internal IReadOnlyCollection<string> AuthoredClasses(string panel) =>
        _classes.TryGetValue(panel, out var classes) ? classes : Array.Empty<string>();
}
