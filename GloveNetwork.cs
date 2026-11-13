using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;

namespace WeaponPaints;

internal static class GloveNetwork
{
    internal static void Notify(CCSPlayerPawn pawn)
    {
        pawn.EconGlovesChanged++;
        Utilities.SetStateChanged(pawn, nameof(CCSPlayerPawn), "m_EconGloves");
        Utilities.SetStateChanged(pawn, nameof(CCSPlayerPawn), "m_nEconGlovesChanged");
    }

    internal static void Clear(CCSPlayerPawn pawn)
    {
        var item = pawn.EconGloves;
        item.AttributeList.Attributes.RemoveAll();
        item.NetworkedDynamicAttributes.Attributes.RemoveAll();
        item.ItemDefinitionIndex = 0; item.ItemID = 0; item.ItemIDLow = 0; item.ItemIDHigh = 0; item.AccountID = 0;
        item.Initialized = false;
        Notify(pawn);
    }
}
