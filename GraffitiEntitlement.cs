using System.Collections.Concurrent;
namespace WeaponPaints;
public partial class WeaponPaints
{
    internal static readonly ConcurrentDictionary<int, DateTime> PlayerGraffitiExpiry = new();
    // Checked at every plugin spray/override, so expiry needs no polling timer.
    internal static bool HasGraffitiSubscription(int slot) => PlayerGraffitiExpiry.TryGetValue(slot, out var expiry) && expiry > DateTime.UtcNow;
}
