using System.Collections.Concurrent;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Memory;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;
using MySqlConnector;
using Vector3 = System.Numerics.Vector3;

namespace WeaponPaints;

// Pet entity work stays on the game thread. SQL is loaded asynchronously at connect and once per round.
public partial class WeaponPaints
{
    private sealed record PetChoice(bool Enabled, bool Customized, bool HasRow, int Id, int Stage, int Variant, uint Seed, string? Name, string? Hat);
    private sealed record LivePet(uint Handle, uint HatHandle, string SteamId, int SpawnTick);
    private static readonly PetChoice DefaultPet = new(true, false, false, 3, 1, 0, 0, null, null);
    private readonly ConcurrentDictionary<int, (string SteamId, bool Active, PetChoice? Choice)> _petChoices = new();
    private readonly ConcurrentDictionary<int, int> _petLoadVersions = new();
    private readonly Dictionary<int, LivePet> _livePets = new();
    private readonly Dictionary<int, int> _petEmoteVersions = new();
    private readonly Dictionary<int, DateTime> _petEmoteCooldowns = new();
    private readonly Dictionary<int, CounterStrikeSharp.API.Modules.Timers.Timer> _petExpiryTimers = new();
    private static int _ownerOffset;
    private static long _petLoadSequence;
    private const string PetEntityName = "weaponpaints_pet";
    private const string PetHatEntityName = "weaponpaints_pet_hat";
    private static readonly string[] PetModels = ["models/chicken/chick.vmdl", "models/chicken/chicken.vmdl", "models/chicken/chicken_silkie.vmdl", "models/chicken/chicken_polish.vmdl"];
    private static readonly (string Id, string Model, string Attachment)[] PetHats = [
        ("helmet", "models/photobooth/silly_hats/helmet.vmdl", "head_attach"),
        ("armor", "models/photobooth/silly_hats/armor_helmet.vmdl", "head_attach"),
        ("alien", "models/photobooth/silly_hats/alien.vmdl", "head_attach"),
        ("banana", "models/photobooth/silly_hats/banana.vmdl", "head_attach"),
        ("glasses", "models/photobooth/silly_hats/glasses.vmdl", "eyewear_attach"),
        ("nose_glasses", "models/photobooth/silly_hats/nose_glasses.vmdl", "eyewear_attach"),
        ("party", "models/photobooth/silly_hats/party.vmdl", "head_attach"),
        ("sprout", "models/photobooth/silly_hats/sprout.vmdl", "head_attach"),
        ("top_hat", "models/photobooth/silly_hats/top_hat.vmdl", "head_attach"),
        ("wizard_hat", "models/photobooth/silly_hats/wizard_hat.vmdl", "head_attach")
    ];

    private void RegisterPetListeners()
    {
        try { _ownerOffset = Schema.GetSchemaOffset("CChicken", "m_owner"); }
        catch (Exception) { _ownerOffset = 0; Logger.LogWarning("CChicken.m_owner is unavailable; custom pets are disabled to prevent duplicates"); }
        RegisterEventHandler<EventPlayerSpawn>(OnPetSpawn);
        RegisterEventHandler<EventRoundStart>(OnPetRoundStart);
        RegisterEventHandler<EventPlayerTeam>(OnPetTeam);
        RegisterListener<Listeners.OnMapEnd>(OnPetMapEnd);
        RegisterListener<Listeners.OnServerPrecacheResources>(PrecachePets);
        RegisterListener<Listeners.OnEntitySpawned>(OnChickenSpawned);
        RegisterListener<Listeners.OnEntityDeleted>(OnPetEntityDeleted);
        AddCommand("css_petemote", "Play a chicken emote: trick, squat, sleep, feed, shoulder, panic, idle", OnPetEmote);
        // One-time recovery for entities left by a plugin reload. No recurring entity sweep.
        foreach (var chicken in Utilities.FindAllEntitiesByDesignerName<CChicken>("chicken"))
            if (chicken.IsValid && chicken.Entity?.Name == PetEntityName) KillPetEntity(chicken);
        foreach (var prop in Utilities.FindAllEntitiesByDesignerName<CDynamicProp>("prop_dynamic"))
            if (prop.IsValid && prop.Entity?.Name == PetHatEntityName) KillPetEntity(prop);
    }

    private void PrecachePets(ResourceManifest manifest)
    {
        foreach (var model in PetModels) manifest.AddResource(model);
        foreach (var hat in PetHats) manifest.AddResource(hat.Model);
    }

    private static PetChoice ReadPet(MySqlDataReader reader, int start)
    {
        return new PetChoice(reader.GetBoolean(start), reader.GetBoolean(start + 1), true, reader.GetInt32(start + 2), reader.GetInt32(start + 3), reader.GetInt32(start + 4), reader.GetFieldValue<uint>(start + 5), reader.IsDBNull(start + 6) ? null : CleanPetName(reader.GetString(start + 6)), reader.IsDBNull(start + 7) ? null : reader.GetString(start + 7));
    }

    private static string? CleanPetName(string name)
    {
        var clean = new StringBuilder();
        foreach (var rune in name.EnumerateRunes())
        {
            if (clean.Length >= 32) break;
            if (System.Text.Rune.IsControl(rune) || rune.Value is '<' or '>' or '{' or '}') continue;
            clean.Append(rune.ToString());
        }
        var value = clean.ToString().Trim();
        return value.Length == 0 ? null : value;
    }

    private void LoadPetFor(CCSPlayerController player)
    {
        var slot = player.Slot;
        var steamId = player.SteamID.ToString();
        var version = (int)Interlocked.Increment(ref _petLoadSequence);
        _petLoadVersions[slot] = version;
        _ = Task.Run(async () =>
        {
            try
            {
                if (Database == null) return;
                await using var connection = await Database.GetConnectionAsync();
                await using var command = new MySqlCommand("SELECT TIMESTAMPDIFF(SECOND,CONVERT_TZ(UTC_TIMESTAMP(),'+00:00','+03:00'),a.expires_at),p.pet_enabled,p.pet_customized,p.pet_id,p.pet_stage,p.pet_variant,p.pet_seed,p.pet_name,p.pet_hat FROM plus_active a LEFT JOIN wp_player_pets p ON p.steamid=a.steam_id WHERE a.steam_id=@id LIMIT 1", connection);
                command.Parameters.AddWithValue("@id", steamId);
                await using var reader = await command.ExecuteReaderAsync();
                var active = await reader.ReadAsync();
                var seconds = active ? reader.GetInt32(0) : 0;
                var choice = active && !reader.IsDBNull(1) ? ReadPet(reader, 1) : active ? DefaultPet : null;
                Server.NextFrame(() => ApplyPetLoad(slot, steamId, version, active, choice, seconds));
            }
            catch (Exception ex) { Logger.LogWarning("Pet load failed for slot {Slot}: {Message}", slot, ex.Message); }
        });
    }

    private void ApplyPetLoad(int slot, string steamId, int version, bool active, PetChoice? choice, int seconds)
    {
        if (!_petLoadVersions.TryGetValue(slot, out var current) || current != version) return;
        var player = Utilities.GetPlayerFromSlot(slot);
        if (player == null || !player.IsValid || player.SteamID.ToString() != steamId) return;
        if (_petExpiryTimers.Remove(slot, out var previous)) previous.Kill();
        _petChoices[slot] = (steamId, active, choice);
        if (active)
        {
            // One timer per player, refreshed by the round batch. No polling for expiry.
            _petExpiryTimers[slot] = AddTimer(Math.Clamp(seconds + 1, 1, 86400), () =>
            {
                if (_petLoadVersions.TryGetValue(slot, out var current) && current == version)
                {
                    var owner = Utilities.GetPlayerFromSlot(slot);
                    if (owner != null && owner.IsValid && owner.SteamID.ToString() == steamId) LoadPetFor(owner);
                }
            }, TimerFlags.STOP_ON_MAPCHANGE);
        }
        if (!active || choice?.Enabled != true) RemovePetEntity(slot);
        else if (player.TeamNum is 2 or 3) AddTimer(.2f, () => TrySpawnPet(slot), TimerFlags.STOP_ON_MAPCHANGE);
    }

    // A single indexed batch query refreshes subscription expiry and website changes once per round.
    private void RefreshPetsForRound()
    {
        var players = Utilities.GetPlayers().Where(p => p.IsValid && !p.IsBot && !p.IsHLTV).ToArray();
        if (players.Length == 0 || Database == null) return;
        var slots = players.ToDictionary(p => p.Slot, p => p.SteamID.ToString());
        var versions = slots.ToDictionary(kv => kv.Key, kv => (int)Interlocked.Increment(ref _petLoadSequence));
        foreach (var kv in versions) _petLoadVersions[kv.Key] = kv.Value;
        _ = Task.Run(async () =>
        {
            try
            {
                await using var connection = await Database.GetConnectionAsync();
                var parameters = slots.Values.Select((_, i) => "@p" + i).ToArray();
                await using var command = new MySqlCommand($"SELECT a.steam_id,TIMESTAMPDIFF(SECOND,CONVERT_TZ(UTC_TIMESTAMP(),'+00:00','+03:00'),a.expires_at),p.pet_enabled,p.pet_customized,p.pet_id,p.pet_stage,p.pet_variant,p.pet_seed,p.pet_name,p.pet_hat FROM plus_active a LEFT JOIN wp_player_pets p ON p.steamid=a.steam_id WHERE a.steam_id IN ({string.Join(',', parameters)})", connection);
                var i = 0; foreach (var id in slots.Values) command.Parameters.AddWithValue(parameters[i++], id);
                var loaded = new Dictionary<string, (int Seconds, PetChoice Choice)>(StringComparer.Ordinal);
                await using (var reader = await command.ExecuteReaderAsync())
                    while (await reader.ReadAsync()) loaded[reader.GetString(0)] = (reader.GetInt32(1), reader.IsDBNull(2) ? DefaultPet : ReadPet(reader, 2));
                Server.NextFrame(() =>
                {
                    foreach (var (slot, id) in slots)
                    {
                        var active = loaded.TryGetValue(id, out var row);
                        ApplyPetLoad(slot, id, versions[slot], active, active ? row.Choice : null, active ? row.Seconds : 0);
                    }
                });
            }
            catch (Exception ex) { Logger.LogWarning("Round pet refresh failed: {Message}", ex.Message); }
        });
    }

    private HookResult OnPetSpawn(EventPlayerSpawn e, GameEventInfo info)
    {
        var player = e.Userid;
        if (player != null && player.IsValid && !player.IsBot)
        {
            RemovePetEntity(player.Slot);
            AddTimer(.25f, () => TrySpawnPet(player.Slot), TimerFlags.STOP_ON_MAPCHANGE);
            AddTimer(.6f, () => TrySpawnPet(player.Slot), TimerFlags.STOP_ON_MAPCHANGE);
        }
        return HookResult.Continue;
    }

    private HookResult OnPetRoundStart(EventRoundStart e, GameEventInfo info)
    {
        foreach (var slot in _livePets.Keys.ToArray()) RemovePetEntity(slot);
        RefreshPetsForRound();
        return HookResult.Continue;
    }

    private HookResult OnPetTeam(EventPlayerTeam e, GameEventInfo info)
    {
        var player = e.Userid;
        if (player != null && player.IsValid) RemovePetEntity(player.Slot);
        return HookResult.Continue;
    }

    private void OnPetMapEnd() => ClearAllPets();

    private void OnChickenSpawned(CEntityInstance entity)
    {
        if (entity.DesignerName != "chicken" || _ownerOffset <= 0) return;
        var handle = entity.EntityHandle.Raw;
        AddTimer(.15f, () =>
        {
            var chicken = new CHandle<CChicken>(handle).Value;
            if (chicken == null || !chicken.IsValid || chicken.Entity?.Name == PetEntityName || _livePets.Values.Any(p => p.Handle == handle)) return;
            var owner = Schema.GetRef<uint>(chicken.Handle, "CChicken", "m_owner");
            foreach (var player in Utilities.GetPlayers())
            {
                if (!player.IsValid || player.PlayerPawn.Value is not { } pawn || pawn.Controller.Raw != owner) continue;
                if (_petChoices.TryGetValue(player.Slot, out var state) && state.Active && state.Choice is { Enabled: true } choice)
                {
                    if (choice.Customized) KillPetEntity(chicken);
                    else RemovePetEntity(player.Slot);
                }
                break;
            }
        }, TimerFlags.STOP_ON_MAPCHANGE);
    }

    private void OnPetEntityDeleted(CEntityInstance entity)
    {
        if (_livePets.Count == 0 || entity.DesignerName is not ("chicken" or "prop_dynamic")) return;
        var handle = entity.EntityHandle.Raw;
        foreach (var (slot, live) in _livePets.ToArray())
        {
            if (live.Handle == handle)
            {
                _livePets.Remove(slot);
                if (live.HatHandle != 0)
                    Server.NextFrame(() => KillPetEntity(new CHandle<CDynamicProp>(live.HatHandle).Value));
            }
            else if (live.HatHandle == handle) _livePets[slot] = live with { HatHandle = 0 };
        }
    }

    private void ClearAllPets()
    {
        foreach (var timer in _petExpiryTimers.Values) timer.Kill();
        _petExpiryTimers.Clear();
        foreach (var slot in _livePets.Keys.ToArray()) RemovePetEntity(slot);
        _petChoices.Clear(); _petLoadVersions.Clear(); _petEmoteVersions.Clear(); _petEmoteCooldowns.Clear();
    }

    private void RemovePetFor(int slot)
    {
        if (_petExpiryTimers.Remove(slot, out var timer)) timer.Kill();
        RemovePetEntity(slot);
        _petChoices.TryRemove(slot, out _);
        _petLoadVersions.TryRemove(slot, out _);
        _petEmoteVersions.Remove(slot);
        _petEmoteCooldowns.Remove(slot);
    }

    private void RemovePetEntity(int slot)
    {
        if (!_livePets.Remove(slot, out var live)) return;
        if (live.HatHandle != 0) KillPetEntity(new CHandle<CDynamicProp>(live.HatHandle).Value);
        KillPetEntity(new CHandle<CChicken>(live.Handle).Value);
        _petEmoteVersions.Remove(slot);
    }

    private static void KillPetEntity(CEntityInstance? entity)
    {
        if (entity != null && entity.IsValid) entity.AddEntityIOEvent("Kill", entity, null, "", 0f);
    }

    // Called only on spawn/connect/round. A native pet is identified by its owner; map chickens are untouched.
    private List<CChicken> NativePets(CCSPlayerController player)
    {
        var found = new List<CChicken>();
        if (_ownerOffset <= 0 || player.PlayerPawn.Value is not { } pawn) return found;
        var owned = _livePets.Values.Select(x => x.Handle).ToHashSet();
        foreach (var chicken in Utilities.FindAllEntitiesByDesignerName<CChicken>("chicken"))
        {
            if (!chicken.IsValid || chicken.Entity?.Name == PetEntityName || owned.Contains(chicken.EntityHandle.Raw)) continue;
            if (Schema.GetRef<uint>(chicken.Handle, "CChicken", "m_owner") == pawn.Controller.Raw) found.Add(chicken);
        }
        return found;
    }

    private void TrySpawnPet(int slot)
    {
        if (_ownerOffset <= 0 || !_petChoices.TryGetValue(slot, out var loaded) || !loaded.Active) return;
        var choice = loaded.Choice;
        if (choice?.Enabled != true) { RemovePetEntity(slot); return; }
        var player = Utilities.GetPlayerFromSlot(slot);
        if (player == null || !player.IsValid || player.SteamID.ToString() != loaded.SteamId || player.TeamNum is not (2 or 3)) return;
        var pawn = player.PlayerPawn.Value;
        if (pawn == null || !pawn.IsValid || pawn.LifeState != (byte)LifeState_t.LIFE_ALIVE || pawn.AbsOrigin is not { } origin) return;
        if (_livePets.ContainsKey(slot)) return;
        var natives = NativePets(player);
        if (!choice.Customized && natives.Count != 0) { RemovePetEntity(slot); return; }
        if (choice.Customized && natives.Count != 0)
        {
            foreach (var native in natives) KillPetEntity(native);
            AddTimer(.1f, () => SpawnPetEntity(slot, loaded.SteamId, choice), TimerFlags.STOP_ON_MAPCHANGE);
            return;
        }
        SpawnPetEntity(slot, loaded.SteamId, choice);
        if (!choice.Customized && !choice.HasRow)
        {
            _petChoices[slot] = (loaded.SteamId, true, choice with { HasRow = true });
            _ = PersistDefaultPet(loaded.SteamId);
        }
    }

    private async Task PersistDefaultPet(string steamId)
    {
        try
        {
            if (Database == null) return;
            await using var connection = await Database.GetConnectionAsync();
            await using var command = new MySqlCommand("INSERT IGNORE INTO wp_player_pets (steamid,pet_enabled,pet_customized,pet_id,pet_stage,pet_variant,pet_seed) VALUES (@id,1,0,3,1,0,0)", connection);
            command.Parameters.AddWithValue("@id", steamId);
            await command.ExecuteNonQueryAsync();
        }
        catch (Exception ex) { Logger.LogWarning("Could not save default pet for {SteamId}: {Message}", steamId, ex.Message); }
    }

    private void SpawnPetEntity(int slot, string steamId, PetChoice choice)
    {
        var player = Utilities.GetPlayerFromSlot(slot);
        if (player == null || !player.IsValid || player.SteamID.ToString() != steamId || player.TeamNum is not (2 or 3)) return;
        if (_livePets.ContainsKey(slot)) return;
        var pawn = player.PlayerPawn.Value;
        if (pawn == null || !pawn.IsValid || pawn.AbsOrigin is not { } origin) return;
        var stage = Math.Clamp(choice.Stage, 1, 3);
        var model = stage == 1 ? PetModels[0] : choice.Id switch { 4 => PetModels[2], 5 => PetModels[3], _ => PetModels[1] };
        var yaw = pawn.EyeAngles.Y;
        var radians = yaw * MathF.PI / 180f;
        var position = new Vector3(origin.X - MathF.Cos(radians) * 40f, origin.Y - MathF.Sin(radians) * 40f, origin.Z + 2f);
        var angles = new Vector3(0, yaw, 0);
        var chicken = Utilities.CreateEntityByName<CChicken>("chicken");
        if (chicken == null || !chicken.IsValid) return;
        try
        {
            var item = chicken.AttributeManager.Item;
            item.ItemDefinitionIndex = 4681;
            item.EntityQuality = 4;
            item.EntityLevel = 1;
            UpdatePlayerEconItemId(item);
            item.AccountID = (uint)player.SteamID;
            item.Initialized = true;
            item.CustomName = choice.Name ?? "";
            WritePetNameOverride(item, "m_szCustomNameOverride2", stage == 2 ? choice.Name : null);
            WritePetNameOverride(item, "m_szCustomNameOverride3", stage == 3 ? choice.Name : null);
            WritePetAttributes(item.NetworkedDynamicAttributes.Handle, choice, stage);
            WritePetAttributes(item.AttributeList.Handle, choice, stage);
            Schema.GetRef<uint>(chicken.Handle, "CChicken", "m_owner") = pawn.Controller.Raw;
            var keys = new CEntityKeyValues();
            keys.SetString("targetname", PetEntityName);
            keys.SetVector("origin", position.X, position.Y, position.Z);
            keys.SetAngle("angles", angles.X, angles.Y, angles.Z);
            chicken.DispatchSpawn(keys);
            var currentModel = chicken.CBodyComponent?.SceneNode?.GetSkeletonInstance()?.ModelState.ModelName;
            if (!string.Equals(currentModel, model, StringComparison.OrdinalIgnoreCase)) chicken.SetModel(model);
            chicken.AcceptInput("Skin", value: (stage == 1 ? 0 : Math.Clamp(choice.Variant, 0, choice.Id == 3 ? 13 : choice.Id == 4 ? 9 : 12)).ToString(CultureInfo.InvariantCulture));
            Schema.GetRef<uint>(chicken.Handle, "CChicken", "m_owner") = pawn.Controller.Raw;
            Utilities.SetStateChanged(chicken, "CChicken", "m_owner");
            chicken.Teleport(position, angles, Vector3.Zero);
            chicken.Leader.Raw = Utilities.InvalidEHandleIndex;
            chicken.MoveType = MoveType_t.MOVETYPE_NONE;
            chicken.ActualMoveType = MoveType_t.MOVETYPE_NONE;
            Utilities.SetStateChanged(chicken, "CBaseEntity", "m_MoveType");
            var hat = SpawnPetHat(chicken, choice.Hat, stage);
            _livePets[slot] = new LivePet(chicken.EntityHandle.Raw, hat, steamId, Server.TickCount);
        }
        catch (Exception ex)
        {
            Logger.LogWarning("Could not spawn chicken for {SteamId}: {Message}", steamId, ex.Message);
            KillPetEntity(chicken);
        }
    }

    private static void WritePetAttributes(nint list, PetChoice choice, int stage)
    {
        var fedUntil = (uint)DateTimeOffset.UtcNow.AddDays(30).ToUnixTimeSeconds();
        CAttributeListSetOrAddAttributeValueByName.Invoke(list, "pet id", ViewAsFloat((uint)choice.Id));
        CAttributeListSetOrAddAttributeValueByName.Invoke(list, "upgrade level", ViewAsFloat((uint)stage));
        CAttributeListSetOrAddAttributeValueByName.Invoke(list, "pet seed", ViewAsFloat(choice.Seed));
        CAttributeListSetOrAddAttributeValueByName.Invoke(list, "pet food expiration date", ViewAsFloat(fedUntil));
    }

    private static void WritePetNameOverride(CEconItemView item, string field, string? name)
    {
        try
        {
            var offset = Schema.GetSchemaOffset("CEconItemView", field);
            if (offset <= 0) return;
            var bytes = Encoding.UTF8.GetBytes(name ?? "");
            if (bytes.Length >= 161) return;
            Marshal.Copy(bytes, 0, item.Handle + offset, bytes.Length);
            Marshal.WriteByte(item.Handle + offset, bytes.Length, 0);
        }
        catch (Exception) { /* Older server builds do not expose the stage-specific name buffers. */ }
    }

    private uint SpawnPetHat(CChicken chicken, string? id, int stage)
    {
        var hat = PetHats.FirstOrDefault(h => h.Id == id);
        if (hat.Id == null) return 0;
        var prop = Utilities.CreateEntityByName<CDynamicProp>("prop_dynamic");
        if (prop == null || !prop.IsValid) return 0;
        try
        {
            var keys = new CEntityKeyValues();
            keys.SetString("targetname", PetHatEntityName);
            keys.SetString("model", hat.Model);
            keys.SetInt("solid", 0);
            if (chicken.AbsOrigin is { } origin) keys.SetVector("origin", origin.X, origin.Y, origin.Z);
            prop.CreateNonSolid = true;
            prop.DispatchSpawn(keys);
            prop.SetModel(hat.Model);
            prop.AcceptInput("SetParent", chicken, prop, "!activator");
            prop.AcceptInput("SetParentAttachment", chicken, prop, hat.Attachment);
            if (prop.CBodyComponent?.SceneNode is { } node) node.Scale = stage == 1 ? 1f : stage == 2 ? .43f : .55f;
            return prop.EntityHandle.Raw;
        }
        catch (Exception ex) { Logger.LogWarning("Could not attach pet hat {Hat}: {Message}", id, ex.Message); KillPetEntity(prop); return 0; }
    }

    private void OnPetEmote(CCSPlayerController? player, CommandInfo command)
    {
        if (player == null || !player.IsValid || !_petChoices.TryGetValue(player.Slot, out var state) || !state.Active) return;
        var activities = new Dictionary<string, int> { ["trick"] = 7, ["squat"] = 1, ["sleep"] = 10, ["feed"] = 9, ["shoulder"] = 11, ["panic"] = 6, ["idle"] = 0 };
        var name = command.ArgString.Trim().Trim('"').ToLowerInvariant();
        if (!activities.TryGetValue(name, out var activity))
        {
            player.PrintToChat("[4ill+] !petemote trick|squat|sleep|feed|shoulder|panic|idle");
            return;
        }
        var chicken = _livePets.TryGetValue(player.Slot, out var live) ? new CHandle<CChicken>(live.Handle).Value : NativePets(player).FirstOrDefault();
        if (chicken == null || !chicken.IsValid) { player.PrintToChat("[4ill+] Питомец пока не появился."); return; }
        if (_petEmoteCooldowns.TryGetValue(player.Slot, out var readyAt) && readyAt > DateTime.UtcNow) return;
        _petEmoteCooldowns[player.Slot] = DateTime.UtcNow.AddSeconds(3);
        var version = _petEmoteVersions.TryGetValue(player.Slot, out var last) ? last + 1 : 1;
        _petEmoteVersions[player.Slot] = version;
        var slot = player.Slot; var handle = chicken.EntityHandle.Raw; var expires = DateTime.UtcNow.AddSeconds(30);
        void Tick()
        {
            if (!_petEmoteVersions.TryGetValue(slot, out var current) || current != version || DateTime.UtcNow >= expires) return;
            var target = new CHandle<CChicken>(handle).Value;
            if (target == null || !target.IsValid) return;
            try
            {
                if (Schema.GetRef<int>(target.Handle, "CChicken", "m_currentActivity") == activity) return;
                Schema.GetRef<int>(target.Handle, "CChicken", "m_desiredActivity") = activity;
                AddTimer(.1f, Tick, TimerFlags.STOP_ON_MAPCHANGE);
            }
            catch (Exception ex) { Logger.LogWarning("Pet emote failed: {Message}", ex.Message); }
        }
        Tick();
    }
}
