namespace WeaponPaints;
internal sealed class PreviewState<TRecipient> where TRecipient : class
{
    internal sealed record Entry(ulong SteamId, string Html, double ExpiresAt, TRecipient? Recipient, uint Controller);
    internal readonly Dictionary<int, Entry> Entries = new();
    internal void Show(int slot, ulong steamId, string image, double now, TRecipient? recipient = null, uint controller = 0)
    {
        if (string.IsNullOrEmpty(image)) { Entries.Remove(slot); return; }
        Entries[slot] = new(steamId, "<img src='" + image + "'</img>", now + 2, recipient, controller);
    }
    internal bool IsCurrent(int slot, ulong steamId, double now, uint controller = 0) =>
        Entries.TryGetValue(slot, out var entry) && entry.SteamId == steamId && entry.Controller == controller && now < entry.ExpiresAt;
}
