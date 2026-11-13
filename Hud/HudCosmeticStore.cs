using System.Globalization;
using System.Text;
using Dapper;
using MySqlConnector;

namespace WeaponPaints.Hud;

internal enum HudCosmeticKind { Agent, Patch, Music, Pin, Graffiti, Pet }
internal sealed record HudCosmeticKey(string SteamId, HudCosmeticKind Kind, int Team, int Slot = 0);
internal abstract record HudCosmeticValue;
internal sealed record HudAgentValue(string? Model) : HudCosmeticValue;
internal sealed record HudNumberValue(int Id) : HudCosmeticValue;
internal sealed record HudGraffitiValue(int Id, int Tint) : HudCosmeticValue;
internal sealed record HudPetValue(bool Enabled, bool Customized, int Id, int Stage, int Variant, uint Seed, string? Name, string? Hat) : HudCosmeticValue;
internal sealed record HudCosmeticSnapshot(HudCosmeticKey Key, bool Exists, int? SourceTeam, HudCosmeticValue? Value);
internal sealed record HudCosmeticChange(HudCosmeticSnapshot Expected, HudCosmeticValue Value);
internal sealed record HudPetIdentity(int Id, int Stage, int Variant);
internal sealed record HudCosmeticScope(HudCosmeticKind Kind, int Team);
internal sealed record HudCosmeticPolicy(IReadOnlyDictionary<HudCosmeticScope, IReadOnlySet<HudCosmeticValue>> Allowed,
    IReadOnlySet<HudPetIdentity> Pets, IReadOnlySet<string> Hats);
internal enum HudCosmeticResult { Saved, Conflict, PlusRequired, Invalid }

/// <summary>Transactional, per-account cosmetic writes. Policy and snapshots are trusted server state.</summary>
internal sealed class HudCosmeticStore(Func<Task<MySqlConnection>> connect, OrderedOperations operations, Func<string, SemaphoreSlim> stripe)
{
    private static bool ValidKey(HudCosmeticKey k) => k.SteamId.Length==17 && k.SteamId.All(char.IsAsciiDigit) && k.Kind switch {
        HudCosmeticKind.Agent or HudCosmeticKind.Music or HudCosmeticKind.Pin => k.Team is 2 or 3 && k.Slot==0,
        HudCosmeticKind.Patch => k.Team is 2 or 3 && k.Slot is >=0 and <3,
        HudCosmeticKind.Graffiti or HudCosmeticKind.Pet => k.Team==0 && k.Slot==0,
        _ => false
    };
    private static bool CorrectValue(HudCosmeticKind kind,HudCosmeticValue v) => (kind,v) switch {
        (HudCosmeticKind.Agent,HudAgentValue a) => a.Model == null || (a.Model.Length<=64 && !a.Model.Any(char.IsControl)),
        (HudCosmeticKind.Patch or HudCosmeticKind.Music or HudCosmeticKind.Pin,HudNumberValue n) => n.Id>=0,
        (HudCosmeticKind.Graffiti,HudGraffitiValue g) => g.Id>=0 && g.Tint>=0,
        (HudCosmeticKind.Pet,HudPetValue p) => p.Customized && p.Name is not null && p.Hat is not null && ValidPetName(p.Name),
        _=>false
    };
    private static bool ValidPetName(string name)
    {
        for(int i=0;i<name.Length;i++) {
            if(char.IsHighSurrogate(name[i])) { if(i+1>=name.Length || !char.IsLowSurrogate(name[++i])) return false; }
            else if(char.IsLowSurrogate(name[i])) return false;
        }
        if(name.EnumerateRunes().Count()>32 || name.Any(ch => ch is '<' or '>' or '{' or '}')) return false;
        foreach(var rune in name.EnumerateRunes())
            if (Rune.GetUnicodeCategory(rune) is UnicodeCategory.Control or UnicodeCategory.Format or UnicodeCategory.Surrogate or UnicodeCategory.PrivateUse or UnicodeCategory.OtherNotAssigned) return false;
        return true;
    }
    internal async Task<HudCosmeticSnapshot> LoadAsync(HudCosmeticKey key,CancellationToken token=default)
    {
        if(!ValidKey(key)) throw new ArgumentException("Invalid cosmetic identity",nameof(key));
        using var order=await EnterAsync(key.SteamId,token).ConfigureAwait(false);
        await using var db=await ConnectAsync(token).ConfigureAwait(false);
        await using var tx=await db.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead,token).ConfigureAwait(false);
        var result=await ReadAsync(db,tx,key,false,token).ConfigureAwait(false);
        await tx.CommitAsync(token).ConfigureAwait(false); return result;
    }
    internal async Task<HudCosmeticResult> ApplyAsync(IReadOnlyList<HudCosmeticChange> changes,HudCosmeticPolicy policy,CancellationToken token=default)
    {
        var input=changes.ToArray(); var allowed=policy.Allowed.ToDictionary(p=>p.Key,p=>p.Value.ToHashSet()); var pets=policy.Pets.ToHashSet(); var hats=policy.Hats.ToHashSet(StringComparer.Ordinal);
        if(input.Length is <1 or >32 || input.Any(c=>!ValidKey(c.Expected.Key) || !CorrectValue(c.Expected.Key.Kind,c.Value)) || input.Select(c=>c.Expected.Key).Distinct().Count()!=input.Length || input.Any(c=>c.Expected.Key.SteamId!=input[0].Expected.Key.SteamId) || input.Any(c=>c.Value is HudPetValue pet ? !pets.Contains(new HudPetIdentity(pet.Id,pet.Stage,pet.Variant)) || !hats.Contains(pet.Hat ?? "") : !allowed.TryGetValue(new HudCosmeticScope(c.Expected.Key.Kind,c.Expected.Key.Team),out var set)||!set.Contains(c.Value))) return HudCosmeticResult.Invalid;
        input=input.Select(c=>c.Value is HudPetValue p ? c with { Value = p with { Name = p.Name!.Trim(' ') } } : c).ToArray();
        string steam=input[0].Expected.Key.SteamId;
        using var order=await EnterAsync(steam,token).ConfigureAwait(false);
        var gate=stripe(steam); await gate.WaitAsync(token).ConfigureAwait(false);
        try {
            await using var db=await ConnectAsync(token).ConfigureAwait(false);
            string name="wp_player_skins:"+steam;
            if(await db.ExecuteScalarAsync<int?>(new CommandDefinition("SELECT GET_LOCK(@name,2)",new{name},cancellationToken:token)).ConfigureAwait(false)!=1) throw new TimeoutException("HUD cosmetic lock unavailable");
            try {
                await using var tx=await db.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead,token).ConfigureAwait(false);
                try {
                    if(!await HasPlus(db,tx,steam,token).ConfigureAwait(false)) return await Rollback(tx,HudCosmeticResult.PlusRequired);
                    foreach(var change in input.OrderBy(c=>c.Expected.Key.Kind).ThenBy(c=>c.Expected.Key.Team).ThenBy(c=>c.Expected.Key.Slot))
                        if(await ReadAsync(db,tx,change.Expected.Key,true,token).ConfigureAwait(false)!=change.Expected) return await Rollback(tx,HudCosmeticResult.Conflict);
                    foreach(var change in input) await WriteAsync(db,tx,change,token).ConfigureAwait(false);
                    if(!await HasPlus(db,tx,steam,token).ConfigureAwait(false)) return await Rollback(tx,HudCosmeticResult.PlusRequired);
                    token.ThrowIfCancellationRequested(); await tx.CommitAsync(CancellationToken.None).ConfigureAwait(false); return HudCosmeticResult.Saved;
                } catch { await tx.RollbackAsync(CancellationToken.None).ConfigureAwait(false); throw; }
            } finally { try { await db.ExecuteScalarAsync<int?>("SELECT RELEASE_LOCK(@name)",new{name}).ConfigureAwait(false); } catch { /* Connection disposal releases a session lock. */ } }
        } finally { gate.Release(); }
    }
    private static async Task<HudCosmeticResult> Rollback(MySqlTransaction tx,HudCosmeticResult result) { await tx.RollbackAsync(CancellationToken.None).ConfigureAwait(false);return result; }
    private static Task<bool?> HasPlusRaw(MySqlConnection db,MySqlTransaction tx,string steam,CancellationToken token)=>db.ExecuteScalarAsync<bool?>(new CommandDefinition("SELECT expires_at>CONVERT_TZ(UTC_TIMESTAMP(),'+00:00','+03:00') FROM plus_active WHERE steam_id=@steam FOR UPDATE",new{steam},tx,cancellationToken:token));
    private static async Task<bool> HasPlus(MySqlConnection db,MySqlTransaction tx,string steam,CancellationToken token)=>await HasPlusRaw(db,tx,steam,token).ConfigureAwait(false)==true;
    private static async Task<HudCosmeticSnapshot> ReadAsync(MySqlConnection db,MySqlTransaction tx,HudCosmeticKey key,bool locked,CancellationToken token)
    {
        string suffix=locked?" FOR UPDATE":""; object arg=new {steam=key.SteamId,team=key.Team,slot=key.Slot};
        string query=key.Kind switch {
            HudCosmeticKind.Agent=>"SELECT agent_t,agent_ct FROM wp_player_agents WHERE steamid=@steam",
            HudCosmeticKind.Patch=>"SELECT patch_id FROM wp_player_patches WHERE steamid=@steam AND weapon_team=@team AND patch_slot=@slot",
            HudCosmeticKind.Music=>"SELECT weapon_team,music_id FROM wp_player_music WHERE steamid=@steam AND weapon_team IN (0,@team) ORDER BY weapon_team DESC LIMIT 1",
            HudCosmeticKind.Pin=>"SELECT weapon_team,id FROM wp_player_pins WHERE steamid=@steam AND weapon_team IN (0,@team) ORDER BY weapon_team DESC LIMIT 1",
            HudCosmeticKind.Graffiti=>"SELECT graffiti_id,tint_id FROM wp_player_graffiti WHERE steamid=@steam",
            HudCosmeticKind.Pet=>"SELECT pet_enabled,pet_customized,pet_id,pet_stage,pet_variant,pet_seed,pet_name,pet_hat FROM wp_player_pets WHERE steamid=@steam",
            _=>throw new InvalidOperationException()
        };
        var row=await db.QueryFirstOrDefaultAsync(new CommandDefinition(query+suffix,arg,tx,cancellationToken:token)).ConfigureAwait(false);
        if(row is not IDictionary<string,object> v) return new(key,false,null,null);
        int I(string n)=>Convert.ToInt32(v[n],CultureInfo.InvariantCulture);
        string? S(string n)=>v[n] is null or DBNull?null:Convert.ToString(v[n],CultureInfo.InvariantCulture);
        HudCosmeticValue value=key.Kind switch {
            HudCosmeticKind.Agent=>new HudAgentValue(S(key.Team==2?"agent_t":"agent_ct")),
            HudCosmeticKind.Patch=>new HudNumberValue(I("patch_id")),
            HudCosmeticKind.Music=>new HudNumberValue(I("music_id")),
            HudCosmeticKind.Pin=>new HudNumberValue(I("id")),
            HudCosmeticKind.Graffiti=>new HudGraffitiValue(I("graffiti_id"),I("tint_id")),
            HudCosmeticKind.Pet=>new HudPetValue(Convert.ToBoolean(v["pet_enabled"]),Convert.ToBoolean(v["pet_customized"]),I("pet_id"),I("pet_stage"),I("pet_variant"),Convert.ToUInt32(v["pet_seed"],CultureInfo.InvariantCulture),S("pet_name"),S("pet_hat")),
            _=>throw new InvalidOperationException()
        };
        int? source=key.Kind is HudCosmeticKind.Music or HudCosmeticKind.Pin?I("weapon_team"):null;
        return new(key,true,source,value);
    }
    private static Task WriteAsync(MySqlConnection db,MySqlTransaction tx,HudCosmeticChange change,CancellationToken token)
    {
        var key=change.Expected.Key;
        string query; object arg;
        switch(change.Value) {
            case HudAgentValue a:
                query=key.Team==2?"INSERT INTO wp_player_agents (steamid,agent_t) VALUES (@steam,@model) ON DUPLICATE KEY UPDATE agent_t=VALUES(agent_t)":"INSERT INTO wp_player_agents (steamid,agent_ct) VALUES (@steam,@model) ON DUPLICATE KEY UPDATE agent_ct=VALUES(agent_ct)";
                arg=new{steam=key.SteamId,model=a.Model}; break;
            case HudNumberValue n:
                query=key.Kind switch {
                    HudCosmeticKind.Patch=>"INSERT INTO wp_player_patches (steamid,weapon_team,patch_slot,patch_id) VALUES (@steam,@team,@slot,@id) ON DUPLICATE KEY UPDATE patch_id=VALUES(patch_id)",
                    HudCosmeticKind.Music=>"INSERT INTO wp_player_music (steamid,weapon_team,music_id) VALUES (@steam,@team,@id) ON DUPLICATE KEY UPDATE music_id=VALUES(music_id)",
                    HudCosmeticKind.Pin=>"INSERT INTO wp_player_pins (steamid,weapon_team,id) VALUES (@steam,@team,@id) ON DUPLICATE KEY UPDATE id=VALUES(id)", _=>throw new InvalidOperationException()};
                arg=new{steam=key.SteamId,team=key.Team,slot=key.Slot,id=n.Id}; break;
            case HudGraffitiValue g:
                query="INSERT INTO wp_player_graffiti (steamid,graffiti_id,tint_id) VALUES (@steam,@id,@tint) ON DUPLICATE KEY UPDATE graffiti_id=VALUES(graffiti_id),tint_id=VALUES(tint_id)";arg=new{steam=key.SteamId,id=g.Id,tint=g.Tint};break;
            case HudPetValue p:
                query="INSERT INTO wp_player_pets (steamid,pet_enabled,pet_customized,pet_id,pet_stage,pet_variant,pet_seed,pet_name,pet_hat) VALUES (@steam,@enabled,@customized,@id,@stage,@variant,@seed,@petName,@hat) ON DUPLICATE KEY UPDATE pet_enabled=VALUES(pet_enabled),pet_customized=VALUES(pet_customized),pet_id=VALUES(pet_id),pet_stage=VALUES(pet_stage),pet_variant=VALUES(pet_variant),pet_seed=VALUES(pet_seed),pet_name=VALUES(pet_name),pet_hat=VALUES(pet_hat)";
                arg=new{steam=key.SteamId,enabled=p.Enabled,customized=p.Customized,id=p.Id,stage=p.Stage,variant=p.Variant,seed=p.Seed,petName=string.IsNullOrEmpty(p.Name)?null:p.Name,hat=string.IsNullOrEmpty(p.Hat)?null:p.Hat};break;
            default: throw new InvalidOperationException();
        }
        return db.ExecuteAsync(new CommandDefinition(query,arg,tx,cancellationToken:token));
    }
    private async Task<IDisposable> EnterAsync(string steam,CancellationToken token) {
        var waiting=operations.EnterAsync(steam); try{return await waiting.WaitAsync(token).ConfigureAwait(false);} catch(OperationCanceledException){_=ReleaseAbandoned(waiting);throw;}
    }
    private static async Task ReleaseAbandoned(Task<IDisposable> waiting) { try { using var lease=await waiting.ConfigureAwait(false); } catch { } }
    private async Task<MySqlConnection> ConnectAsync(CancellationToken token) {
        var waiting=connect();try{return await waiting.WaitAsync(token).ConfigureAwait(false);}catch(OperationCanceledException){_=DisposeAbandoned(waiting);throw;}
    }
    private static async Task DisposeAbandoned(Task<MySqlConnection> waiting) { try { await using var db=await waiting.ConfigureAwait(false); } catch { } }
}
