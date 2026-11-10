using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;

namespace WeaponPaints;

public partial class WeaponPaints
{
    private void RegisterSkinPreviews()
    {
        if (Config.Additional.ShowSkinImage)
            RegisterListener<Listeners.OnTick>(OnTick);
    }

    private readonly List<int> _expiredPreviews = new();

    private void ClearSkinPreviews()
    {
        _previews.Entries.Clear();
        _expiredPreviews.Clear();
    }

    private void RemoveSkinPreview(int slot) => _previews.Entries.Remove(slot);

    private void OnTick()
    {
        if (!Config.Additional.ShowSkinImage) return;
        if (_previews.Entries.Count == 0) return;
        var now = Server.EngineTime;
        foreach (var (slot, preview) in _previews.Entries)
        {
            var player = preview.Recipient;
            if (player == null || !player.IsValid || !_previews.IsCurrent(slot, player.SteamID, now, player.EntityHandle.Raw))
            {
                _expiredPreviews.Add(slot);
                continue;
            }
            player.PrintToCenterHtml(preview.Html);
        }
        foreach (var slot in _expiredPreviews) _previews.Entries.Remove(slot);
        _expiredPreviews.Clear();
    }
}
