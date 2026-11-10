namespace CounterStrikeSharp.API.Modules.Utils
{
    public sealed class CHandle<T>(uint raw) where T : CounterStrikeSharp.API.Core.Entity
    {
        public uint Raw = raw;
        public T? Value => CounterStrikeSharp.API.Core.Entity.All.GetValueOrDefault(Raw) is T entity && entity.IsValid ? entity : null;
        public bool IsValid => Value != null;
    }
}
namespace CounterStrikeSharp.API.Core
{
    using CounterStrikeSharp.API.Modules.Utils;
    public enum gear_slot_t { GEAR_SLOT_RIFLE, GEAR_SLOT_PISTOL, GEAR_SLOT_KNIFE, GEAR_SLOT_GRENADES }
    public class Entity
    {
        public static readonly Dictionary<uint, Entity> All = new();
        public bool IsValid = true;
        public CHandle<Entity> EntityHandle = new(0);
        public Entity(uint raw) { EntityHandle.Raw = raw; All[raw] = this; }
        public T As<T>() where T : Entity => (T)this;
    }
    public class CBasePlayerPawn(uint raw) : Entity(raw);
    public sealed class CCSPlayerPawn(uint raw) : CBasePlayerPawn(raw)
    {
        public CHandle<CCSPlayerController> Controller = new(0);
        public CCSPlayer_WeaponServices? WeaponServices = new();
    }
    public class CBasePlayerWeapon(uint raw) : Entity(raw)
    {
        public CHandle<CCSPlayerPawn> OwnerEntity = new(0);
    }
    public class CCSWeaponBase(uint raw, gear_slot_t slot) : CBasePlayerWeapon(raw)
    {
        public WeaponData? VData = new() { GearSlot = slot };
    }
    public sealed class WeaponData { public gear_slot_t GearSlot; }
    public sealed class CCSPlayer_WeaponServices
    {
        public CHandle<CBasePlayerWeapon> ActiveWeapon = new(0);
        public List<CHandle<CBasePlayerWeapon>> MyWeapons = new();
    }
    public sealed class CCSPlayerController(uint raw) : Entity(raw)
    {
        public int Slot;
        public ulong SteamID = 10;
        public bool PawnIsAlive = true, IsBot;
        public CHandle<CCSPlayerPawn> PlayerPawn = new(0);
        public readonly List<string> Commands = new();
        public void ExecuteClientCommand(string command)
        {
            Commands.Add(command);
            if (IsBot) return;
            var slot = command switch { "slot1" => gear_slot_t.GEAR_SLOT_RIFLE, "slot2" => gear_slot_t.GEAR_SLOT_PISTOL, "slot3" => gear_slot_t.GEAR_SLOT_KNIFE, _ => throw new Exception(command) };
            var services = PlayerPawn.Value!.WeaponServices!;
            var weapon = services.MyWeapons.Select(h => h.Value).OfType<CCSWeaponBase>().Single(w => w.VData?.GearSlot == slot);
            services.ActiveWeapon.Raw = weapon.EntityHandle.Raw;
        }
    }
}
namespace CounterStrikeSharp.API
{
    using Core;
    public static class Server
    {
        public static readonly Queue<Action> Frames = new();
        public static void NextFrame(Action callback) => Frames.Enqueue(callback);
        public static void Flush() { while (Frames.TryDequeue(out var callback)) callback(); }
    }
    public static class Utilities
    {
        public static CCSPlayerController? Current;
        public static CCSPlayerController? GetPlayerFromSlot(int slot) => Current?.Slot == slot ? Current : null;
        public static void SetStateChanged(Entity entity, string type, string field) { }
    }
}
namespace Microsoft.Extensions.Logging
{
    public static class Logging { public static void LogWarning(this List<Exception> log, Exception error, string text) => log.Add(error); }
}
namespace WeaponPaints
{
    public partial class WeaponPaints
    {
        internal static long DataGeneration;
        internal static readonly Dictionary<int, long> PlayerDataLoading = new();
        public readonly List<Exception> Logger = new();
        public readonly Queue<Action> Timers = new();
        private void AddOneShot(float delay, Action callback) => Timers.Enqueue(callback);
        public void Start(CounterStrikeSharp.API.Core.CCSPlayerController owner, uint target, uint last, Action complete) => RedeployRefreshedWeapon(DeferredPlayer.Capture(owner), target, last, complete);
        public void FlushTimers() { while (Timers.TryDequeue(out var callback)) callback(); }
        public int Holstered => _holsteredWeapons.Count;
        public void Cleanup()
        {
            foreach (var (identity, handle) in _holsteredWeapons) RestoreHolsteredWeapon(identity, handle);
            _holsteredWeapons.Clear(); Timers.Clear();
        }
    }
}
