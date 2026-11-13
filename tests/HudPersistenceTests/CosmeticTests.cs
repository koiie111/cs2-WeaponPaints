using Dapper;
using MySqlConnector;
using WeaponPaints;
using WeaponPaints.Hud;

internal static class CosmeticTests
{
    internal static async Task<int> Run(MySqlConnection sql,Func<Task<MySqlConnection>> connect,OrderedOperations ops,SemaphoreSlim gate,string steam)
    {
        var store=new HudCosmeticStore(connect,ops,_=>gate); int checks=0;
        void Check(bool ok,string message) { if(!ok) throw new InvalidOperationException(message); checks++; }
        var agent=new HudAgentValue("models/player/agent.vmdl"); var patch=new HudNumberValue(99); var music=new HudNumberValue(17); var pin=new HudNumberValue(18);
        var graffiti=new HudGraffitiValue(1671,3); var pet=new HudPetValue(true,true,3,1,0,123,"Chicken","");
        var allowed=new Dictionary<HudCosmeticScope,IReadOnlySet<HudCosmeticValue>> {
            [new(HudCosmeticKind.Agent,2)]=new HashSet<HudCosmeticValue>{agent},
            [new(HudCosmeticKind.Patch,2)]=new HashSet<HudCosmeticValue>{patch,new HudNumberValue(0)}, [new(HudCosmeticKind.Patch,3)]=new HashSet<HudCosmeticValue>{patch},
            [new(HudCosmeticKind.Music,2)]=new HashSet<HudCosmeticValue>{music}, [new(HudCosmeticKind.Music,3)]=new HashSet<HudCosmeticValue>{music},
            [new(HudCosmeticKind.Pin,3)]=new HashSet<HudCosmeticValue>{pin},[new(HudCosmeticKind.Graffiti,0)]=new HashSet<HudCosmeticValue>{graffiti}};
        var policy=new HudCosmeticPolicy(allowed,new HashSet<HudPetIdentity>{new(3,1,0)},new HashSet<string>{""});
        HudCosmeticKey K(HudCosmeticKind kind,int team=0,int slot=0)=>new(steam,kind,team,slot);
        async Task<HudCosmeticChange> C(HudCosmeticKey key,HudCosmeticValue val)=>new(await store.LoadAsync(key),val);
        await sql.ExecuteAsync("DELETE FROM wp_player_agents;DELETE FROM wp_player_patches;DELETE FROM wp_player_music;DELETE FROM wp_player_pins;DELETE FROM wp_player_graffiti;DELETE FROM wp_player_pets;DELETE FROM plus_active; INSERT INTO plus_active VALUES (@steam,DATE_ADD(CONVERT_TZ(UTC_TIMESTAMP(),'+00:00','+03:00'),INTERVAL 1 HOUR)); INSERT INTO wp_player_agents VALUES (@steam,'oldct',NULL); INSERT INTO wp_player_music VALUES (@steam,0,5);INSERT INTO wp_player_pins VALUES (@steam,0,6)",new{steam});
        var a=await C(K(HudCosmeticKind.Agent,2),agent);var p0=await C(K(HudCosmeticKind.Patch,2,0),patch);var p2=await C(K(HudCosmeticKind.Patch,3,2),patch);
        var m=await C(K(HudCosmeticKind.Music,2),music);var pc=await C(K(HudCosmeticKind.Pin,3),pin);var g=await C(K(HudCosmeticKind.Graffiti),graffiti);var petChange=await C(K(HudCosmeticKind.Pet),pet);
        Check(m.Expected.SourceTeam==0 && pc.Expected.SourceTeam==0,"fallback source team");
        Check(await store.ApplyAsync(new[]{a,p0,p2,m,pc,g,petChange},policy)==HudCosmeticResult.Saved,"combined cosmetics save");
        Check(await sql.ExecuteScalarAsync<string>("SELECT agent_ct FROM wp_player_agents WHERE steamid=@steam",new{steam})=="oldct","other agent side overwritten");
        Check((await store.LoadAsync(K(HudCosmeticKind.Patch,2,0))).Value==patch && (await store.LoadAsync(K(HudCosmeticKind.Patch,3,2))).Value==patch,"patch sides/slots");
        Check((await store.LoadAsync(K(HudCosmeticKind.Music,2))).Value==music && (await store.LoadAsync(K(HudCosmeticKind.Music,3))).Value==new HudNumberValue(5),"music fallback preserved");
        Check((await store.LoadAsync(K(HudCosmeticKind.Pin,3))).Value==pin && (await store.LoadAsync(K(HudCosmeticKind.Pin,2))).Value==new HudNumberValue(6),"pin fallback preserved");
        Check((await store.LoadAsync(K(HudCosmeticKind.Graffiti))).Value==graffiti && (await store.LoadAsync(K(HudCosmeticKind.Pet))).Value==pet with { Hat=null },"global cosmetics round trip");
        var wrongTeam=await C(K(HudCosmeticKind.Agent,3),agent); Check(await store.ApplyAsync(new[]{wrongTeam},policy)==HudCosmeticResult.Invalid,"T-only agent accepted for CT");
        Check(await store.ApplyAsync(new[]{new HudCosmeticChange(new HudCosmeticSnapshot(K(HudCosmeticKind.Patch,2,3),false,null,null),patch)},policy)==HudCosmeticResult.Invalid,"invalid slot accepted");
        var conflict=await C(K(HudCosmeticKind.Music,2),music);await sql.ExecuteAsync("UPDATE wp_player_music SET music_id=42 WHERE steamid=@steam AND weapon_team=2",new{steam});
        Check(await store.ApplyAsync(new[]{conflict},policy)==HudCosmeticResult.Conflict,"external edit must conflict");
        var expired=await C(K(HudCosmeticKind.Patch,2,1),patch);await sql.ExecuteAsync("UPDATE plus_active SET expires_at='2000-01-01' WHERE steam_id=@steam",new{steam});
        Check(await store.ApplyAsync(new[]{expired},policy)==HudCosmeticResult.PlusRequired,"expired Plus accepted");
        await sql.ExecuteAsync("UPDATE plus_active SET expires_at=DATE_ADD(CONVERT_TZ(UTC_TIMESTAMP(),'+00:00','+03:00'),INTERVAL 1 HOUR) WHERE steam_id=@steam",new{steam});
        Check(await store.ApplyAsync(new[]{expired},policy)==HudCosmeticResult.Saved,"renewed Plus rejected");
        var invalid=await C(K(HudCosmeticKind.Music,3),new HudNumberValue(999));Check(await store.ApplyAsync(new[]{invalid},policy)==HudCosmeticResult.Invalid,"untrusted id accepted");
        var bad=await C(K(HudCosmeticKind.Pet),pet with { Hat="unknown" });Check(await store.ApplyAsync(new[]{bad},policy)==HudCosmeticResult.Invalid,"untrusted hat accepted");
        bad=await C(K(HudCosmeticKind.Pet),pet with { Name="<bad>" });Check(await store.ApplyAsync(new[]{bad},policy)==HudCosmeticResult.Invalid,"unsafe name accepted");
        bad=await C(K(HudCosmeticKind.Pet),pet with { Name="\nname" });Check(await store.ApplyAsync(new[]{bad},policy)==HudCosmeticResult.Invalid,"control in name accepted");
        bad=await C(K(HudCosmeticKind.Pet),pet with { Name=string.Concat(Enumerable.Repeat("\U0001F413",33)) });Check(await store.ApplyAsync(new[]{bad},policy)==HudCosmeticResult.Invalid,"too many Unicode codepoints accepted");
        var first=await C(K(HudCosmeticKind.Patch,2,0),new HudNumberValue(0));var second=await C(K(HudCosmeticKind.Patch,3,2),patch);
        await sql.ExecuteAsync("CREATE TRIGGER cosmetic_fail_second BEFORE UPDATE ON wp_player_patches FOR EACH ROW BEGIN IF NEW.weapon_team=3 THEN SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='injected'; END IF; END");
        bool failed=false;try {await store.ApplyAsync(new[]{first,second},policy);}catch(MySqlException){failed=true;} await sql.ExecuteAsync("DROP TRIGGER cosmetic_fail_second");
        Check(failed,"second-side exception absent");Check(await sql.ExecuteScalarAsync<int>("SELECT patch_id FROM wp_player_patches WHERE steamid=@steam AND weapon_team=2 AND patch_slot=0",new{steam})==99,"patch rollback");
        var allowedPatch=(HashSet<HudCosmeticValue>)allowed[new(HudCosmeticKind.Patch,3)];var queuedChange=await C(K(HudCosmeticKind.Patch,3,1),patch);
        using (var blocker=await ops.EnterAsync(steam)) {var waiting=store.ApplyAsync(new[]{queuedChange},policy);allowedPatch.Clear();blocker.Dispose();Check(await waiting.WaitAsync(TimeSpan.FromSeconds(5))==HudCosmeticResult.Saved,"policy changed after enqueue");}
        var cancelChange=await C(K(HudCosmeticKind.Patch,2,2),patch);
        using (var blocker=await ops.EnterAsync(steam)) {using var cts=new CancellationTokenSource();var waiting=store.ApplyAsync(new[]{cancelChange},policy,cts.Token);cts.Cancel();bool cancelled=false;try {await waiting.WaitAsync(TimeSpan.FromSeconds(5));}catch(OperationCanceledException){cancelled=true;}Check(cancelled,"cosmetic cancellation did not finish");blocker.Dispose();}
        using(var tail=await ops.EnterAsync(steam).WaitAsync(TimeSpan.FromSeconds(5))) { }
        Check(ops.PendingKeys==0 && !(await store.LoadAsync(K(HudCosmeticKind.Patch,2,2))).Exists,"cancelled operation leaked or wrote");
        return checks;
    }
}
