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
        internal HashSet<int> PreviousDefinitions = new();
        internal HashSet<int>? Definitions = defindex.HasValue ? [defindex.Value] : null;
        internal readonly List<Action<bool>> Completions = new();
        internal void Include(int? value) { Revision++; if (!value.HasValue) Definitions = null; else Definitions?.Add(value.Value); }
    }
    private sealed record RefreshItem(uint Handle, string Classname, int Clip1, int Clip2, int[] Reserve, bool Active, bool Last,
        bool Burst, bool Silencer, int PrimaryTick, float PrimaryRatio, int SecondaryTick, float SecondaryRatio);
    private readonly Dictionary<int, RefreshRequest> _weaponRefreshes = new();
    private readonly HashSet<uint> _retiringWeapons = new();
    private void ClearWeaponRefreshes()
    {
        foreach (var (identity, handle) in _holsteredWeapons) RestoreHolsteredWeapon(identity, handle);
        _holsteredWeapons.Clear();
        var cancelled = _weaponRefreshes.Values.ToArray();
        _weaponRefreshes.Clear();
        _retiringWeapons.Clear();
        foreach (var request in cancelled) CompleteWeaponRefresh(request, false);
    }
    private void RefreshWeapons(CCSPlayerController? player, int? defindex = null) => QueueWeaponRefresh(player, defindex, null);
    private void RefreshWeaponsWithCompletion(CCSPlayerController? player, Action<bool> completed, IReadOnlySet<int>? previousDefinitions = null) => QueueWeaponRefresh(player, null, completed, previousDefinitions);
    private void CompleteWeaponRefresh(RefreshRequest request, bool success)
    {
        foreach (var callback in request.Completions)
            try { callback(success); } catch (Exception error) { Logger.LogError(error, "Weapon refresh completion failed"); }
        request.Completions.Clear();
    }
    private void QueueWeaponRefresh(CCSPlayerController? player, int? defindex, Action<bool>? completed, IReadOnlySet<int>? previousDefinitions = null)
    {
        if (!_gBCommandsAllowed || player == null || !player.IsValid || !player.PawnIsAlive) { completed?.Invoke(false); return; }
        var identity = DeferredPlayer.Capture(player);
        if (_weaponRefreshes.TryGetValue(player.Slot, out var existing))
        {
            if (existing.Identity == identity) { existing.Include(defindex); if (previousDefinitions != null) existing.PreviousDefinitions.UnionWith(previousDefinitions); if (completed != null) existing.Completions.Add(completed); return; }
            _weaponRefreshes.Remove(player.Slot); CompleteWeaponRefresh(existing, false);
        }
        var request = new RefreshRequest(identity, defindex);
        if (previousDefinitions != null) request.PreviousDefinitions.UnionWith(previousDefinitions);
        if (completed != null) request.Completions.Add(completed);
        _weaponRefreshes[player.Slot] = request;
        ScheduleWeaponRefresh(request);
    }
    private void ScheduleWeaponRefresh(RefreshRequest request)
    {
        var identity = request.Identity;
        Server.NextFrame(() =>
        {
            if (!_weaponRefreshes.TryGetValue(identity.Slot, out var current) || !ReferenceEquals(current, request)) return;
            var revision = request.Revision;
            void Finish(bool success)
            {
                if (!_weaponRefreshes.TryGetValue(identity.Slot,out var pending) || !ReferenceEquals(pending,request)) return;
                _weaponRefreshes.Remove(identity.Slot);
                if (request.Revision != revision && identity.TryResolve(out var owner) && owner.PawnIsAlive)
                {
                    var next = new RefreshRequest(identity, null) { Definitions = request.Definitions?.ToHashSet(), PreviousDefinitions = request.PreviousDefinitions };
                    next.Completions.AddRange(request.Completions); request.Completions.Clear();
                    _weaponRefreshes[identity.Slot] = next; ScheduleWeaponRefresh(next);
                    return;
                }
                CompleteWeaponRefresh(request, success && request.Revision == revision);
            }
            if (!identity.TryResolve(out var owner) || !owner.PawnIsAlive || PlayerDataLoading.ContainsKey(owner.Slot)) { Finish(false); return; }
            try { ApplyWeaponRefresh(owner, request.Definitions, request.PreviousDefinitions, Finish); }
            catch (Exception ex) { Finish(false); Logger.LogError(ex, "Weapon refresh failed"); }
        });
    }
    private void ApplyWeaponRefresh(CCSPlayerController player, HashSet<int>? definitions, HashSet<int> previousDefinitions, Action<bool> completed)
    {
        var pawn = player.PlayerPawn.Value;
        var services = pawn?.WeaponServices;
        if (!_gBCommandsAllowed || pawn == null || !pawn.IsValid || services == null || pawn.ItemServices == null) { completed(false); return; }
        var identity = DeferredPlayer.Capture(player);
        var plans = new List<RefreshItem>();
        var preferredActive = services.ActiveWeapon.Raw;
        var preferredLast = services.LastWeapon.Raw;
        var snapshot = services.MyWeapons.Select(h => h.Raw).ToArray();
        var failedRetirement = false;
        foreach (var handle in snapshot)
        {
            if (_retiringWeapons.Contains(handle)) { failedRetirement = true; continue; }
            var old = new CHandle<CBasePlayerWeapon>(handle).Value;
            if (old == null || !old.IsValid || old.OwnerEntity.Raw != pawn.EntityHandle.Raw) continue;
            var data = old.As<CCSWeaponBase>().VData;
            if (data == null || data.GearSlot is not (gear_slot_t.GEAR_SLOT_RIFLE or gear_slot_t.GEAR_SLOT_PISTOL or gear_slot_t.GEAR_SLOT_KNIFE)) continue;
            var knife = data.GearSlot == gear_slot_t.GEAR_SLOT_KNIFE;
            var index = (int)old.AttributeManager.Item.ItemDefinitionIndex;
            if (definitions != null && !definitions.Contains(index) && !(knife && definitions.Any(d => d is 42 or 59 || WeaponDefindex.TryGetValue(d,out var name) && (name.Contains("knife") || name.Contains("bayonet"))))) continue;
            var hasKnifeChoice = false;
            if (knife && GPlayersKnife.TryGetValue(player.Slot, out var knives) && knives.TryGetValue(player.Team, out var knifeChoice))
            {
                hasKnifeChoice = knifeChoice == "weapon_knife" || KnifeDefindexes.Value.ContainsKey(knifeChoice);
            }
            var hasPreference = GPlayerWeaponsInfo.TryGetValue(player.Slot,out var teams) && teams.TryGetValue(player.Team,out var items) && items.ContainsKey(index);
            // A row or knife choice can disappear from the newly loaded snapshot.
            // The old weapon still needs replacement to remove that override.
            if (!hasPreference && !hasKnifeChoice && !previousDefinitions.Contains(index) && !(knife && definitions == null)) continue;
            var classname = "weapon_knife";
            if (!knife && !WeaponDefindex.TryGetValue(index,out classname)) continue;
            var settings=old.As<CCSWeaponBase>();
            var plan = new RefreshItem(handle,classname,old.Clip1,old.Clip2,old.ReserveAmmo.ToArray(),preferredActive==handle,preferredLast==handle,
                settings.BurstMode,settings.SilencerOn,old.NextPrimaryAttackTick,old.NextPrimaryAttackTickRatio,old.NextSecondaryAttackTick,old.NextSecondaryAttackTickRatio);
            _retiringWeapons.Add(handle);
            // Entity destruction and RemovePlayerItem alone leave this serial in
            // MyWeapons on CS2 build 10955. The normal item-services drop path
            // updates the inventory vector before we destroy the retired entity.
            try
            {
                RetireRefreshWeapon(pawn, old);
                plans.Add(plan);
            }
            catch(Exception ex)
            {
                failedRetirement = true;
                _retiringWeapons.Remove(handle);
                Logger.LogWarning(ex,"Unable to retire refresh item {Classname}",classname);
            }
        }
        if(plans.Count==0){completed(!failedRetirement);return;}
        var attempts=0;
        uint redeployActive=0;
        void FinishSelection()
        {
            // Native drops/equips can update LastWeapon after the give loop.
            // Restore it once those actions finish, while the captured held item
            // is still selected. Never overwrite a different manual selection.
            Server.NextFrame(() =>
            {
                var success = false;
                try
                {
                    if (!identity.TryResolve(out var owner) || !owner.PawnIsAlive) return;
                    success = !failedRetirement;
                    var currentPawn = owner.PlayerPawn.Value;
                    var currentServices = currentPawn?.WeaponServices;
                    var last = new CHandle<CBasePlayerWeapon>(preferredLast).Value;
                    if (currentPawn == null || currentServices == null || currentServices.ActiveWeapon.Raw != preferredActive ||
                        last is not { IsValid: true } || last.OwnerEntity.Raw != currentPawn.EntityHandle.Raw) return;
                    currentServices.LastWeapon.Raw = preferredLast;
                    Utilities.SetStateChanged(currentPawn, nameof(CBasePlayerPawn), "m_pWeaponServices");
                }
                catch (Exception ex) { success = false; Logger.LogError(ex, "Unable to restore weapon selection"); }
                finally { completed(success); }
            });
        }
        void GiveRemoved()
        {
            if (!identity.TryResolve(out var current) || !current.PawnIsAlive) { completed(false); return; }
            var currentPawn=current.PlayerPawn.Value;
            var currentServices=currentPawn?.WeaponServices;
            if(currentPawn==null || currentServices==null || currentPawn.ItemServices==null){completed(false);return;}
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
                    replacement.Clip1=plan.Clip1;replacement.Clip2=plan.Clip2;
                    var settings=replacement.As<CCSWeaponBase>();settings.BurstMode=plan.Burst;settings.SilencerOn=plan.Silencer;
                    replacement.NextPrimaryAttackTick=plan.PrimaryTick;replacement.NextPrimaryAttackTickRatio=plan.PrimaryRatio;
                    replacement.NextSecondaryAttackTick=plan.SecondaryTick;replacement.NextSecondaryAttackTickRatio=plan.SecondaryRatio;
                    var reserve=replacement.ReserveAmmo;
                    for(var i=0;i<Math.Min(reserve.Length,plan.Reserve.Length);i++)reserve[i]=plan.Reserve[i];
                    if(plan.Active)preferredActive=replacement.EntityHandle.Raw;
                    if(plan.Active)redeployActive=replacement.EntityHandle.Raw;
                    if(plan.Last)preferredLast=replacement.EntityHandle.Raw;
                    if(new CHandle<CBasePlayerWeapon>(preferredActive).Value is {IsValid:true} selected && selected.OwnerEntity.Raw==currentPawn.EntityHandle.Raw)currentServices.ActiveWeapon.Raw=preferredActive;
                    if(new CHandle<CBasePlayerWeapon>(preferredLast).Value is {IsValid:true} previous && previous.OwnerEntity.Raw==currentPawn.EntityHandle.Raw)currentServices.LastWeapon.Raw=preferredLast;
                    Utilities.SetStateChanged(currentPawn,nameof(CBasePlayerPawn),"m_pWeaponServices");
                    plans.RemoveAt(n);
                }
                catch(Exception ex)
                {
                    // A failed acquisition may leave an unowned newly-created entity.
                    if(replacement is {IsValid:true} && (replacement.OwnerEntity.Raw==currentPawn.EntityHandle.Raw || !replacement.OwnerEntity.IsValid))
                    {
                        try {
                            if(replacement.OwnerEntity.Raw==currentPawn.EntityHandle.Raw)RetireRefreshWeapon(currentPawn,replacement);
                            else replacement.Remove();
                        } catch(Exception cleanup) { Logger.LogWarning(cleanup,"Unable to remove failed replacement"); }
                    }
                    Logger.LogWarning(ex,"Weapon replacement attempt {Attempt} failed",attempts);
                }
            }
            if(plans.Count==0){
                if(redeployActive!=0) RedeployRefreshedWeapon(identity,redeployActive,preferredLast,FinishSelection);
                else FinishSelection();
                return;
            }
            if(attempts>=8){Logger.LogError("Weapon refresh exhausted retries for slot {Slot}; remaining item classes: {Items}",identity.Slot,string.Join(",",plans.Select(p=>p.Classname)));completed(false);return;}
            Server.NextFrame(GiveRemoved);
        }
        Server.NextFrame(GiveRemoved);
    }
}
