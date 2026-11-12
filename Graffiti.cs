using System.Collections.Concurrent;
using System.Text.Json;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;

namespace WeaponPaints;

// Independent CounterStrikeSharp implementation of the native spray workflow.
// Behaviour reference: Letaryat/WeaponSkins cb561164 (SwiftlyS2); no Swiftly code dependency.
public partial class WeaponPaints
{
    internal static readonly ConcurrentDictionary<int, (int Kit, int Tint)> PlayerGraffiti = new();
    private void UpdateGraffitiEntitlement(CCSPlayerController player, DateTime expiry)
    { if (Config.Additional.GraffitiEnabled) PlayerGraffitiExpiry[player.Slot] = expiry; }
    private readonly Dictionary<ulong, CHandle<CPlayerSprayDecal>> _graffitiEntities = new();
    private readonly Dictionary<ulong, long> _graffitiCooldown = new();
    private readonly HashSet<(int Kit, int Tint)> _graffitiCatalog = new();

    internal bool IsValidGraffiti(int kit, int tint) => _graffitiCatalog.Contains((kit, tint));

    private void RegisterGraffiti()
    {
        var path = Path.Combine(ModuleDirectory, "data", "graffiti.json");
        if (File.Exists(path))
        {
            using var data = JsonDocument.Parse(File.ReadAllText(path));
            foreach (var item in data.RootElement.GetProperty("items").EnumerateArray())
                _graffitiCatalog.Add((item.GetProperty("graffiti").GetInt32(), item.GetProperty("tint").GetInt32()));
        }
        if (Config.Additional.GraffitiEnabled && _graffitiCatalog.Count == 0)
            throw new InvalidOperationException("Graffiti catalogue is missing");
        AddCommand("css_spray", "Spray the selected website graffiti", SprayGraffiti);
        AddCommand("css_graffiti", "Select graffiti on the website", (player, command) =>
            command.ReplyToCommand($"[WeaponPaints] Граффити: {Config.Website}. После «Применить»: !wp, затем !spray."));
        RegisterListener<Listeners.OnEntitySpawned>(entity =>
        {
            if (!Config.Additional.GraffitiEnabled || !Config.ReplaceNativeGraffiti || entity.DesignerName != "player_spray_decal") return;
            var handle = new CHandle<CPlayerSprayDecal>(entity.EntityHandle.Raw);
            var generation = DataGeneration;
            Server.NextWorldUpdate(() =>
            {
                if (generation != DataGeneration || handle.Value is not { IsValid: true } decal || decal.AccountID == 0) return;
                var player = Utilities.GetPlayers().FirstOrDefault(p => p.IsValid && !p.IsBot && (uint)p.SteamID == decal.AccountID);
                if (player == null || !HasGraffitiSubscription(player.Slot) || !PlayerGraffiti.TryGetValue(player.Slot, out var selected) || !IsValidGraffiti(selected.Kit, selected.Tint)) return;
                if (decal.Player == selected.Kit && decal.TintID == selected.Tint) return;
                decal.Player = selected.Kit; decal.TintID = selected.Tint;
                Utilities.SetStateChanged(decal, "CPlayerSprayDecal", "m_nPlayer");
                Utilities.SetStateChanged(decal, "CPlayerSprayDecal", "m_nTintID");
            });
        });
    }

    private static void CopyGraffitiVector(Vector target, Vector value)
    { target.X = value.X; target.Y = value.Y; target.Z = value.Z; }

    private void SprayGraffiti(CCSPlayerController? player, CommandInfo command)
    {
        if (!Config.Additional.GraffitiEnabled || player is not { IsValid: true, IsBot: false, PawnIsAlive: true }
            || player.PlayerPawn.Value is not { IsValid: true } pawn || pawn.AbsOrigin is not { } origin) return;
        if (!HasGraffitiSubscription(player.Slot))
        { command.ReplyToCommand("[WeaponPaints] Граффити доступны с подпиской 4ill+."); return; }
        if (!PlayerGraffiti.TryGetValue(player.Slot, out var choice) || !IsValidGraffiti(choice.Kit, choice.Tint))
        { command.ReplyToCommand("[WeaponPaints] Выберите граффити на сайте и выполните !wp."); return; }
        var now = Environment.TickCount64;
        if (_graffitiCooldown.TryGetValue(player.SteamID, out var until) && now < until)
        { command.ReplyToCommand($"[WeaponPaints] Граффити будет доступно через {(until - now + 999) / 1000} с."); return; }
        var pitch = pawn.EyeAngles.X * MathF.PI / 180; var yaw = pawn.EyeAngles.Y * MathF.PI / 180;
        var start = new Vector(origin.X + pawn.ViewOffset.X, origin.Y + pawn.ViewOffset.Y, origin.Z + pawn.ViewOffset.Z);
        var end = new Vector(start.X + 128 * MathF.Cos(pitch) * MathF.Cos(yaw), start.Y + 128 * MathF.Cos(pitch) * MathF.Sin(yaw), start.Z - 128 * MathF.Sin(pitch));
        var trace = Trace.TraceEndShape(start, end, pawn, new TraceOptions { InteractsWith = Contents.Solid });
        var hit = trace.HitEntity();
        if (!trace.DidHit() || trace.IsAllSolid || hit is not { IsValid: true } || hit.DesignerName is not ("worldent" or "worldspawn" or "prop_static"))
        { command.ReplyToCommand("[WeaponPaints] Подойдите к неподвижной поверхности."); return; }
        var n = trace.Normal;
        var left = new System.Numerics.Vector3(-MathF.Sin(yaw), MathF.Cos(yaw), 0);
        var normal = new System.Numerics.Vector3(n.X, n.Y, n.Z);
        left -= normal * System.Numerics.Vector3.Dot(left, normal);
        if (!float.IsFinite(left.LengthSquared()) || left.LengthSquared() < .01f) return;
        left = System.Numerics.Vector3.Normalize(left);
        var decal = Utilities.CreateEntityByName<CPlayerSprayDecal>("player_spray_decal");
        if (decal is not { IsValid: true }) return;
        try
        {
            CopyGraffitiVector(decal.Start, trace.EndPos); CopyGraffitiVector(decal.EndPos, trace.EndPos);
            CopyGraffitiVector(decal.Normal, n); CopyGraffitiVector(decal.Left, new Vector(left.X, left.Y, left.Z));
            decal.AccountID = (uint)player.SteamID; decal.Player = choice.Kit; decal.TintID = choice.Tint;
            decal.DispatchSpawn();
            if (_graffitiEntities.Remove(player.SteamID, out var previous) && previous.Value is { IsValid: true } old) old.Remove();
            _graffitiEntities[player.SteamID] = new CHandle<CPlayerSprayDecal>(decal.EntityHandle.Raw);
            _graffitiCooldown[player.SteamID] = now + Math.Clamp(Config.GraffitiCooldownSeconds, 1, 3600) * 1000L;
            player.EmitSound("SprayCan.Paint");
        }
        catch { if (decal.IsValid) decal.Remove(); throw; }
    }

    private void ForgetGraffiti(int slot, ulong steamId)
    {
        PlayerGraffiti.TryRemove(slot, out _); PlayerGraffitiExpiry.TryRemove(slot, out _); _graffitiCooldown.Remove(steamId);
        if (_graffitiEntities.Remove(steamId, out var handle) && handle.Value is { IsValid: true } decal) decal.Remove();
    }
    private void ClearGraffiti(bool removeEntities)
    {
        if (removeEntities) foreach (var handle in _graffitiEntities.Values) if (handle.Value is { IsValid: true } decal) decal.Remove();
        _graffitiEntities.Clear(); _graffitiCooldown.Clear();
        if (removeEntities) { PlayerGraffiti.Clear(); PlayerGraffitiExpiry.Clear(); }
    }
}
