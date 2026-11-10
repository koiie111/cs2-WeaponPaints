using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;
namespace WeaponPaints;
public partial class WeaponPaints
{
    private sealed class RefreshRequest(DeferredPlayer identity, int? defindex)
    {
        internal readonly DeferredPlayer Identity = identity;
        internal int Revision;
        internal HashSet<int>? Definitions = defindex.HasValue ? [defindex.Value] : null;
        internal void Include(int? value) { Revision++; if (!value.HasValue) Definitions = null; else Definitions?.Add(value.Value); }
    }
    private sealed record RefreshItem(uint Handle, string Classname, int Clip1, int Clip2, int[] Reserve, bool Active, bool Last,
        bool Burst, bool Silencer, int PrimaryTick, float PrimaryRatio, int SecondaryTick, float SecondaryRatio);
    private readonly Dictionary<int, RefreshRequest> _weaponRefreshes = new();
    private readonly HashSet<uint> _retiringWeapons = new();
    private void ClearWeaponRefreshes() { _weaponRefreshes.Clear(); _retiringWeapons.Clear(); }
    private void RefreshWeapons(CCSPlayerController? player, int? defindex = null)
    {
        if (!_gBCommandsAllowed || player == null || !player.IsValid || !player.PawnIsAlive) return;
        var identity = DeferredPlayer.Capture(player);
        if (_weaponRefreshes.TryGetValue(player.Slot, out var existing) && existing.Identity == identity) { existing.Include(defindex); return; }
        var request = new RefreshRequest(identity, defindex);
        _weaponRefreshes[player.Slot] = request;
        Server.NextFrame(() =>
        {
            if (!_weaponRefreshes.TryGetValue(identity.Slot, out var current) || !ReferenceEquals(current, request)) return;
            var revision = request.Revision;
            void Finish()
            {
                if (!_weaponRefreshes.TryGetValue(identity.Slot,out var pending) || !ReferenceEquals(pending,request)) return;
                _weaponRefreshes.Remove(identity.Slot);
                if (request.Revision != revision && identity.TryResolve(out var owner)) RefreshWeapons(owner);
            }
            if (!identity.TryResolve(out var owner) || !owner.PawnIsAlive || PlayerDataLoading.ContainsKey(owner.Slot)) { Finish(); return; }
            try { ApplyWeaponRefresh(owner, request.Definitions, Finish); }
            catch (Exception ex) { Finish(); Logger.LogError(ex, "Weapon refresh failed"); }
        });
    }
    private void ApplyWeaponRefresh(CCSPlayerController player, HashSet<int>? definitions, Action completed)
    {
        var pawn = player.PlayerPawn.Value;
        var services = pawn?.WeaponServices;
        if (!_gBCommandsAllowed || pawn == null || !pawn.IsValid || services == null) { completed(); return; }
        var identity = DeferredPlayer.Capture(player);
        var plans = new List<RefreshItem>();
        var preferredActive = services.ActiveWeapon.Raw;
        var preferredLast = services.LastWeapon.Raw;
        var snapshot = services.MyWeapons.Select(h => h.Raw).ToArray();
        foreach (var handle in snapshot)
        {
            if (_retiringWeapons.Contains(handle)) continue;
            var old = new CHandle<CBasePlayerWeapon>(handle).Value;
            if (old == null || !old.IsValid || old.OwnerEntity.Raw != pawn.EntityHandle.Raw) continue;
            var data = old.As<CCSWeaponBase>().VData;
            if (data == null || data.GearSlot is not (gear_slot_t.GEAR_SLOT_RIFLE or gear_slot_t.GEAR_SLOT_PISTOL or gear_slot_t.GEAR_SLOT_KNIFE)) continue;
            var knife = data.GearSlot == gear_slot_t.GEAR_SLOT_KNIFE;
            var index = (int)old.AttributeManager.Item.ItemDefinitionIndex;
            if (definitions != null && !definitions.Contains(index) && !(knife && definitions.Any(d => WeaponDefindex.TryGetValue(d,out var name) && (name.Contains("knife") || name.Contains("bayonet"))))) continue;
            var hasPreference = GPlayerWeaponsInfo.TryGetValue(player.Slot,out var teams) && teams.TryGetValue(player.Team,out var items) && items.ContainsKey(index);
            if (!hasPreference && !(knife && HasChangedKnife(player,out _))) continue;
            if (!WeaponDefindex.TryGetValue(index,out var classname)) continue;
            if (knife) classname = "weapon_knife";
            var settings=old.As<CCSWeaponBase>();
            plans.Add(new RefreshItem(handle,classname,old.Clip1,old.Clip2,old.ReserveAmmo.ToArray(),preferredActive==handle,preferredLast==handle,
                settings.BurstMode,settings.SilencerOn,old.NextPrimaryAttackTick,old.NextPrimaryAttackTickRatio,old.NextSecondaryAttackTick,old.NextSecondaryAttackTickRatio));
            _retiringWeapons.Add(handle);
            old.Remove();
        }
        if(plans.Count==0){completed();return;}
        var attempts=0;
        void GiveRemoved()
        {
            if (!identity.TryResolve(out var current) || !current.PawnIsAlive) { completed(); return; }
            var currentPawn=current.PlayerPawn.Value;
            var currentServices=currentPawn?.WeaponServices;
            if(currentPawn==null || currentServices==null){completed();return;}
            attempts++;
            for(var n=plans.Count-1;n>=0;n--)
            {
                var plan=plans[n];
                // Entity removal is deferred. Never grant while the captured old
                // serial is still valid: this is the duplicate-prevention barrier.
                if(new CHandle<CBasePlayerWeapon>(plan.Handle).Value is {IsValid:true})continue;
                CBasePlayerWeapon? replacement=null;
                try
                {
                    var raw=current.GiveNamedItem(plan.Classname);
                    if(raw==0)throw new InvalidOperationException("GiveNamedItem returned null");
                    replacement=new CBasePlayerWeapon(raw);
                    if(!replacement.IsValid || replacement.OwnerEntity.Raw!=currentPawn.EntityHandle.Raw)throw new InvalidOperationException("Replacement was not acquired");
                    GivePlayerWeaponSkin(current,replacement);
                    IncrementWearForWeaponWithStickers(current,replacement);
                    replacement.Clip1=plan.Clip1;replacement.Clip2=plan.Clip2;
                    var settings=replacement.As<CCSWeaponBase>();settings.BurstMode=plan.Burst;settings.SilencerOn=plan.Silencer;
                    replacement.NextPrimaryAttackTick=plan.PrimaryTick;replacement.NextPrimaryAttackTickRatio=plan.PrimaryRatio;
                    replacement.NextSecondaryAttackTick=plan.SecondaryTick;replacement.NextSecondaryAttackTickRatio=plan.SecondaryRatio;
                    var reserve=replacement.ReserveAmmo;
                    for(var i=0;i<Math.Min(reserve.Length,plan.Reserve.Length);i++)reserve[i]=plan.Reserve[i];
                    if(plan.Active)preferredActive=replacement.EntityHandle.Raw;
                    if(plan.Last)preferredLast=replacement.EntityHandle.Raw;
                    if(new CHandle<CBasePlayerWeapon>(preferredActive).Value is {IsValid:true} selected && selected.OwnerEntity.Raw==currentPawn.EntityHandle.Raw)currentServices.ActiveWeapon.Raw=preferredActive;
                    if(new CHandle<CBasePlayerWeapon>(preferredLast).Value is {IsValid:true} previous && previous.OwnerEntity.Raw==currentPawn.EntityHandle.Raw)currentServices.LastWeapon.Raw=preferredLast;
                    Utilities.SetStateChanged(currentPawn,nameof(CBasePlayerPawn),"m_pWeaponServices");
                    plans.RemoveAt(n);
                }
                catch(Exception ex)
                {
                    // A failed acquisition may leave an unowned newly-created entity.
                    if(replacement is {IsValid:true} && (replacement.OwnerEntity.Raw==currentPawn.EntityHandle.Raw || !replacement.OwnerEntity.IsValid))replacement.Remove();
                    Logger.LogWarning(ex,"Weapon replacement attempt {Attempt} failed",attempts);
                }
            }
            if(plans.Count==0){completed();return;}
            if(attempts>=8){Logger.LogError("Weapon refresh exhausted retries for slot {Slot}; remaining item classes: {Items}",identity.Slot,string.Join(",",plans.Select(p=>p.Classname)));completed();return;}
            Server.NextFrame(GiveRemoved);
        }
        Server.NextFrame(GiveRemoved);
    }
}
