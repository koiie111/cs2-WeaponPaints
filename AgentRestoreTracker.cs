namespace WeaponPaints;

// Keep only the engine model observed before the first override for a pawn.
// Slots, pawns and teams can all be reused independently.
internal sealed class AgentRestoreTracker
{
    private readonly Dictionary<int, (ulong SteamId, uint Pawn, int Team, string Model)> _models = new();
    internal void Remember(int slot, ulong steamId, uint pawn, int team, string? model)
    {
        if (_models.TryGetValue(slot, out var current) && (current.SteamId, current.Pawn, current.Team) == (steamId, pawn, team)) return;
        _models.Remove(slot);
        if (!string.IsNullOrWhiteSpace(model)) _models[slot] = (steamId, pawn, team, model);
    }
    internal string? Find(int slot, ulong steamId, uint pawn, int team) =>
        _models.TryGetValue(slot, out var current) && (current.SteamId, current.Pawn, current.Team) == (steamId, pawn, team) ? current.Model : null;
    internal void Forget(int slot) => _models.Remove(slot);
    internal void Clear() => _models.Clear();
}
