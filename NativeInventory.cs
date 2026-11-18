using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Memory;
using Microsoft.Extensions.Logging;

namespace WeaponPaints;

public partial class WeaponPaints
{
    // Values only: never retain pointers into engine vectors across frames.
    private readonly record struct NativeAttribute(ushort Id, float Value, float Initial, int Refund, bool Bonus);
    private sealed record NativeEcon(ushort Definition, ulong Id, uint Low, uint High, uint Account,
        int Quality, bool Initialized, string Name, NativeAttribute[] Attributes, NativeAttribute[] Dynamic);
    private sealed record NativeWeapon(ulong Steam, uint Pawn, int Team, NativeEcon Econ, int Paint, int Seed, float Wear, ulong? Mesh);
    private readonly Dictionary<uint, NativeWeapon> _nativeWeapons = new();
    private readonly Dictionary<int, (ulong Steam, uint Pawn, int Team, NativeEcon Econ)> _nativeGloves = new();
    private static readonly Lazy<int> NativeAttributeStride = new(() =>
    {
        var size = Schema.GetClassSize("CEconItemAttribute");
        if (size < 16 || size > 256)
            throw new InvalidOperationException("Unexpected native attribute size");
        return size;
    });
    private static readonly Dictionary<ushort, string> NativeAttributeNames = new()
    {
        [113] = "sticker slot 0 id",
        [114] = "sticker slot 0 wear",
        [115] = "sticker slot 0 scale",
        [116] = "sticker slot 0 rotation",
        [117] = "sticker slot 1 id",
        [118] = "sticker slot 1 wear",
        [119] = "sticker slot 1 scale",
        [120] = "sticker slot 1 rotation",
        [121] = "sticker slot 2 id",
        [122] = "sticker slot 2 wear",
        [123] = "sticker slot 2 scale",
        [124] = "sticker slot 2 rotation",
        [125] = "sticker slot 3 id",
        [126] = "sticker slot 3 wear",
        [127] = "sticker slot 3 scale",
        [128] = "sticker slot 3 rotation",
        [129] = "sticker slot 4 id",
        [130] = "sticker slot 4 wear",
        [131] = "sticker slot 4 scale",
        [132] = "sticker slot 4 rotation",
        [133] = "sticker slot 5 id",
        [134] = "sticker slot 5 wear",
        [135] = "sticker slot 5 scale",
        [136] = "sticker slot 5 rotation",
        [278] = "sticker slot 0 offset x",
        [279] = "sticker slot 0 offset y",
        [280] = "sticker slot 1 offset x",
        [281] = "sticker slot 1 offset y",
        [282] = "sticker slot 2 offset x",
        [283] = "sticker slot 2 offset y",
        [284] = "sticker slot 3 offset x",
        [285] = "sticker slot 3 offset y",
        [286] = "sticker slot 4 offset x",
        [287] = "sticker slot 4 offset y",
        [288] = "sticker slot 5 offset x",
        [289] = "sticker slot 5 offset y",
        [290] = "sticker slot 0 schema",
        [291] = "sticker slot 1 schema",
        [292] = "sticker slot 2 schema",
        [293] = "sticker slot 3 schema",
        [294] = "sticker slot 4 schema",
        [295] = "sticker slot 5 schema",
        [299] = "keychain slot 0 id",
        [300] = "keychain slot 0 offset x",
        [301] = "keychain slot 0 offset y",
        [302] = "keychain slot 0 offset z",
        [306] = "keychain slot 0 seed",
        [314] = "keychain slot 0 highlight",
        [321] = "keychain slot 0 sticker",
        [80] = "kill eater",
        [81] = "kill eater score type",
        [6] = "set item texture prefab",
        [7] = "set item texture seed",
        [8] = "set item texture wear"
    };
    private static (nint First, int Count, int Stride) ReadAttributeVector(CAttributeList list)
    {
        var vector = list.Attributes;
        var count = vector.Count;
        if (count == 0) return default;

        if (count < 0 || count > 128)
            throw new InvalidOperationException("Unexpected native attribute vector count");

        // The indexed CSS helper uses the stride for handles. The econ vector
        // stores structs inline, so only the address at index zero is shared.
        var first = NativeAPI.GetNetworkVectorElementAt(vector.Handle, 0);
        if (first == 0) throw new InvalidOperationException("Null native attribute storage");
        return (first, count, NativeAttributeStride.Value);
    }

    private static NativeAttribute[] CaptureAttributes(CAttributeList list)
    {
        var (first, count, stride) = ReadAttributeVector(list);
        if (count == 0) return [];
        var result = new NativeAttribute[count];
        for (var i = 0; i < result.Length; i++)
        {
            var a = new CEconItemAttribute(first + i * stride);
            if (!NativeAttributeNames.ContainsKey(a.AttributeDefinitionIndex))
                throw new InvalidOperationException($"Unknown native attribute {a.AttributeDefinitionIndex}; keeping inventory item unchanged");
            result[i] = new(a.AttributeDefinitionIndex, a.Value, a.InitialValue, a.RefundableCurrency, a.SetBonus);
        }
        return result;
    }
    private static NativeEcon CaptureEcon(CEconItemView item) => new(item.ItemDefinitionIndex, item.ItemID,
        item.ItemIDLow, item.ItemIDHigh, item.AccountID, item.EntityQuality, item.Initialized, item.CustomName,
        CaptureAttributes(item.AttributeList), CaptureAttributes(item.NetworkedDynamicAttributes));
    private static void RestoreAttributes(CAttributeList list, NativeAttribute[] saved)
    {
        list.Attributes.RemoveAll();
        if (saved.Length == 0) return;
        foreach (var a in saved)
            CAttributeListSetOrAddAttributeValueByName.Invoke(list.Handle, NativeAttributeNames[a.Id], a.Value);

        // Insertions can reallocate the vector. Resolve it once after the last write.
        var (first, count, stride) = ReadAttributeVector(list);
        Dictionary<ushort, NativeAttribute>? byId = null;
        for (var i = 0; i < count; i++)
        {
            var restored = new CEconItemAttribute(first + i * stride);
            var id = restored.AttributeDefinitionIndex;
            NativeAttribute snapshot;
            if (count == saved.Length && saved[i].Id == id)
                snapshot = saved[i];
            else
            {
                if (byId == null)
                {
                    byId = new(saved.Length);
                    foreach (var attribute in saved) byId[attribute.Id] = attribute;
                }
                if (!byId.TryGetValue(id, out snapshot)) continue;
            }
            restored.InitialValue = snapshot.Initial;
            restored.RefundableCurrency = snapshot.Refund;
            restored.SetBonus = snapshot.Bonus;
        }
    }
    private static void RestoreEcon(CEconItemView item, NativeEcon saved)
    {
        RestoreAttributes(item.AttributeList, saved.Attributes);
        RestoreAttributes(item.NetworkedDynamicAttributes, saved.Dynamic);
        item.ItemDefinitionIndex = saved.Definition; item.ItemID = saved.Id;
        item.ItemIDLow = saved.Low; item.ItemIDHigh = saved.High; item.AccountID = saved.Account;
        item.EntityQuality = saved.Quality; item.Initialized = saved.Initialized; item.CustomName = saved.Name;
    }
    private bool RememberNativeWeapon(CCSPlayerController player, CBasePlayerWeapon weapon)
    {
        var handle = weapon.EntityHandle.Raw;
        if (_nativeWeapons.TryGetValue(handle, out var existing))
            return existing.Steam == player.SteamID && existing.Pawn == player.PlayerPawn.Raw && existing.Team == (int)player.Team;
        try
        {
            _nativeWeapons[handle] = new(player.SteamID, player.PlayerPawn.Raw, (int)player.Team,
                CaptureEcon(weapon.AttributeManager.Item), weapon.FallbackPaintKit, weapon.FallbackSeed, weapon.FallbackWear, weapon.CBodyComponent?.SceneNode?.GetSkeletonInstance().ModelState.MeshGroupMask);
            return true;
        }
        catch (Exception error) { Logger.LogWarning(error, "Cannot safely customize native item"); return false; }
    }
    private void RestoreNativeWeapon(CCSPlayerController player, CBasePlayerWeapon weapon)
    {
        if (!_nativeWeapons.TryGetValue(weapon.EntityHandle.Raw, out var saved) || saved.Steam != player.SteamID ||
            saved.Pawn != player.PlayerPawn.Raw || saved.Team != (int)player.Team) return;
        if (weapon.AttributeManager.Item.ItemDefinitionIndex != saved.Econ.Definition) SubclassChange(weapon, saved.Econ.Definition);
        RestoreEcon(weapon.AttributeManager.Item, saved.Econ);
        weapon.FallbackPaintKit = saved.Paint; weapon.FallbackSeed = saved.Seed; weapon.FallbackWear = saved.Wear;
        if (saved.Mesh is { } mask && weapon.CBodyComponent?.SceneNode is { } node)
            node.GetSkeletonInstance().ModelState.MeshGroupMask = mask;
        NotifyWeaponAppearanceChanged(weapon);
        _nativeWeapons.Remove(weapon.EntityHandle.Raw);
    }
    private bool HasWeaponOverride(CCSPlayerController player, CBasePlayerWeapon weapon) =>
        _nativeWeapons.TryGetValue(weapon.EntityHandle.Raw, out var state) && state.Steam == player.SteamID &&
        state.Pawn == player.PlayerPawn.Raw && state.Team == (int)player.Team;
    private static bool UsesInventory(CCSPlayerController player, int definition, bool knife)
    {
        if (knife) return !HasChangedKnife(player, out _);
        return !GPlayerWeaponsInfo.TryGetValue(player.Slot, out var teams) || !teams.TryGetValue(player.Team, out var items) ||
            !items.TryGetValue(definition, out var choice) || choice.Paint <= 0;
    }
}
