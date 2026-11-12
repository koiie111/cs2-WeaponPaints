namespace WeaponPaints;

internal static class StickerAnchor
{
    // Historical rows never sent schema when offsets were zero and forced 0 otherwise.
    // Preserve their actual appearance until the web editor writes an explicit v1 composition.
    internal static uint Resolve(int version, uint stored, uint slot, float x, float y) =>
        version >= 1 ? stored : x != 0 || y != 0 ? 0u : slot;
}
