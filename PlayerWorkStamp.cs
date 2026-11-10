namespace WeaponPaints;
internal readonly record struct PlayerWorkStamp(int Slot, ulong SteamId, uint Controller, uint Pawn, long Generation)
{
    internal bool Matches(int slot, ulong steamId, uint controller, uint pawn, long generation) =>
        this == new PlayerWorkStamp(slot, steamId, controller, pawn, generation);
}
