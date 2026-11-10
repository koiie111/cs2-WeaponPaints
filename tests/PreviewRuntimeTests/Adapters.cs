namespace CounterStrikeSharp.API
{
    public static class Server
    {
        public static double Now;
        public static int ClockReads;
        public static double EngineTime { get { ClockReads++; return Now; } }
    }
}

namespace CounterStrikeSharp.API.Core
{
    public static class Listeners { public delegate void OnTick(); }
    public sealed class EntityHandle { public uint Raw; }
    public sealed class CCSPlayerController
    {
        public bool IsValid = true;
        public bool PawnIsAlive = true;
        public ulong SteamID;
        public EntityHandle EntityHandle = new();
        public int Sends;
        public string? LastHtml;
        public void PrintToCenterHtml(string html) { Sends++; LastHtml = html; }
    }
}

namespace WeaponPaints
{
    using CounterStrikeSharp.API.Core;
    public sealed class Additional { public bool ShowSkinImage = true; }
    public sealed class Config { public Additional Additional = new(); }
    public partial class WeaponPaints
    {
        public Config Config = new();
        private readonly PreviewState<CCSPlayerController> _previews = new();
        public readonly List<Listeners.OnTick> TickListeners = new();
        private void RegisterListener<T>(T callback) where T : Delegate => TickListeners.Add((Listeners.OnTick)(Delegate)callback);
        public void Start() => RegisterSkinPreviews();
        public void Tick() { foreach (var callback in TickListeners) callback(); }
        public void Show(int slot, CCSPlayerController player, string image, double now) =>
            _previews.Show(slot, player.SteamID, image, now, player, player.EntityHandle.Raw);
        public void Disconnect(int slot) => RemoveSkinPreview(slot);
        public void Reset() => ClearSkinPreviews();
        public int Count => _previews.Entries.Count;
    }
}
