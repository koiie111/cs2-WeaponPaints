namespace CounterStrikeSharp.API
{
    public static class Utilities
    {
        public static readonly Dictionary<int, Core.CCSPlayerController> Players = new();
        public static Core.CCSPlayerController? GetPlayerFromSlot(int slot) => Players.GetValueOrDefault(slot);
    }
}
namespace CounterStrikeSharp.API.Core
{
    public sealed class Handle { public uint Raw; }
    public sealed class CCSPlayerController
    {
        public int Slot;
        public ulong SteamID = 10;
        public bool IsValid = true, PawnIsAlive = true;
        public Handle EntityHandle = new() { Raw = 100 }, PlayerPawn = new() { Raw = 200 };
        public string Active = "pistol", Last = "knife";
        public readonly List<string> Commands = new();
        public void ExecuteClientCommand(string command)
        {
            Commands.Add(command);
            if (command != "lastinv") throw new Exception("Unexpected command");
            (Active, Last) = (Last, Active);
        }
    }
}
namespace Microsoft.Extensions.Logging
{
    public static class Logging
    {
        public static void LogWarning(this List<Exception> logger, Exception error, string message) => logger.Add(error);
    }
}
namespace WeaponPaints
{
    using CounterStrikeSharp.API.Core;
    internal static class Utility { internal static bool IsPlayerValid(CCSPlayerController player) => player.IsValid; }
    public partial class WeaponPaints
    {
        public sealed class Configuration { public AdditionalFlags Additional = new(); }
        public sealed class AdditionalFlags { public bool GloveEnabled = true; }
        public Configuration Config = new();
        public int ClearedGloves;
        internal static long DataGeneration;
        public bool GlovesEnabled = true, HasGloves = true, FailApply, DeferredClear, FailClear;
        public readonly List<Exception> Logger = new();
        public readonly Queue<Action> Pending = new();
        public readonly List<string> Events = new();
        private bool TryGetGlovePaint(CCSPlayerController player, out ushort definition, out object? paint)
        {
            definition = 5030; paint = null;
            return GlovesEnabled && HasGloves;
        }
        private void AddOneShot(float delay, Action callback) { Pending.Enqueue(callback); }
        private void ApplyPlayerGloves(CCSPlayerController player, Action<bool> completed)
        {
            if (!HasGloves) { completed(false); return; }
            if (FailApply) throw new Exception("Injected native boundary failure");
            Events.Add("gloves");
            Pending.Enqueue(() => completed(true));
        }
        private void ClearPlayerGloves(CCSPlayerController player, Action<bool> completed) {
            ClearedGloves++;
            if (DeferredClear) Pending.Enqueue(() => completed(!FailClear));
            else completed(!FailClear);
        }
        private void RefreshWeapons(CCSPlayerController player) => Events.Add("weapons:" + player.Active + ":" + player.Last);
        private void RefreshWeaponsWithCompletion(CCSPlayerController player, Action<bool> completed, IReadOnlySet<int>? previousDefinitions = null) { RefreshWeapons(player); completed(true); }
        public void Refresh(CCSPlayerController player) => RefreshPlayerEquipment(player);
        public void RefreshWithCompletion(CCSPlayerController player, Action<bool> callback) => RefreshPlayerEquipmentWithCompletion(player, callback);
        public void GloveMenu(CCSPlayerController player) => GivePlayerGloves(player);
        public void FlushOne() { if (Pending.TryDequeue(out var callback)) callback(); }
        public void Flush() { while (Pending.TryDequeue(out var callback)) callback(); }
    }
}
