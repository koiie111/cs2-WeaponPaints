namespace WeaponPaints;
internal sealed class PreviewState
{
    internal sealed record Entry(ulong SteamId, string Html, double ExpiresAt);
    internal readonly Dictionary<int, Entry> Entries = new();
    internal void Show(int slot, ulong steamId, string image, double now)
    {
        if (string.IsNullOrEmpty(image)) { Entries.Remove(slot); return; }
        Entries[slot] = new(steamId, "<img src='" + image + "'</img>", now + 2);
    }
    internal bool IsCurrent(int slot, ulong steamId, double now) =>
        Entries.TryGetValue(slot, out var entry) && entry.SteamId == steamId && now < entry.ExpiresAt;
}
