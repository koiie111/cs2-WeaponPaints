namespace WeaponPaints;

internal sealed record PetChoice(bool Enabled, bool Customized, bool HasRow, int Id, int Stage, int Variant, uint Seed, string? Name, string? Hat)
{
    // Persisting the default row does not change the visible pet.
    public bool HasSameAppearance(PetChoice? other) =>
        other != null && this with { HasRow = true } == other with { HasRow = true };
}

internal sealed record PetState(string SteamId, DateTime ExpiresAtUtc, PetChoice? Choice)
{
    // Entitlement expires even while a refresh is pending or MySQL is unavailable.
    public bool IsActive(DateTime utcNow) => ExpiresAtUtc > utcNow;
}

// Accessed only on the game thread, including SQL completion callbacks.
internal sealed class PetWorkVersions
{
    private readonly Dictionary<int, long> _versions = new();
    private long _sequence;

    public long Next(int slot) => _versions[slot] = ++_sequence;
    public long GetOrCreate(int slot) => _versions.TryGetValue(slot, out var version) ? version : Next(slot);
    public bool IsCurrent(int slot, long version) => _versions.TryGetValue(slot, out var current) && current == version;
    public void Remove(int slot) => _versions.Remove(slot);
    public void InvalidateAll()
    {
        foreach (var slot in _versions.Keys.ToArray()) Next(slot);
    }
    public void Clear() => _versions.Clear(); // Never reset the sequence: old callbacks may still arrive.
}
