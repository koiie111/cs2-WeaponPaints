using Dapper;
using MySqlConnector;
using WeaponPaints;
using WeaponPaints.Hud;

var value = Environment.GetEnvironmentVariable("WP_TEST_CONNECTION");
if (string.IsNullOrWhiteSpace(value)) { Console.Error.WriteLine("Set WP_TEST_CONNECTION to a disposable local MySQL server."); return 2; }
var builder = new MySqlConnectionStringBuilder(value);
if (builder.Server is not ("localhost" or "127.0.0.1" or "::1")) { Console.Error.WriteLine("Only localhost is accepted."); return 2; }
builder.Database = ""; builder.DefaultCommandTimeout = 5;
var schema = "wp_hud_test_" + Guid.NewGuid().ToString("N");
await using var admin = new MySqlConnection(builder.ConnectionString); await admin.OpenAsync();
await admin.ExecuteAsync($"CREATE DATABASE `{schema}` CHARACTER SET utf8mb4"); builder.Database = schema;
try
{
    await using var sql = new MySqlConnection(builder.ConnectionString); await sql.OpenAsync();
    await sql.ExecuteAsync(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory,"schema.sql")));
    await sql.ExecuteAsync("ALTER TABLE wp_player_skins ADD weapon_keychain_sticker INT UNSIGNED NOT NULL DEFAULT 0, ADD weapon_composition_version TINYINT UNSIGNED NOT NULL DEFAULT 0");
    await sql.ExecuteAsync(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory,"hud-fixture.sql")));
    var ops = new OrderedOperations(); var semaphore = new SemaphoreSlim(1,1);
    async Task<MySqlConnection> Connect() { var c = new MySqlConnection(builder.ConnectionString); await c.OpenAsync(); return c; }
    var identityRow=await sql.QuerySingleAsync<(string ServerUuid,string Database)>("SELECT @@server_uuid, DATABASE()");
    var identity=new HudDatabaseIdentity(Guid.Parse(identityRow.ServerUuid).ToString("D"),identityRow.Database);
    var store = new HudSkinStore(Connect,ops,_=>semaphore,33,identity);
    const string steam="76561198000000001";
    var key2=new HudSkinKey(steam,2,7,"weapon_ak47",true); var key3=key2 with {Team=3};
    var axis=new HudAxis(-10,10); var limits=new HudCompositionLimits(new(0,1),5,axis,axis,axis,axis,axis);
    HashSet<uint> stickers=[101,102,103,104,105,106]; HashSet<uint> chains=[7];
    var models=new Dictionary<string,HudCustomModel> { ["rare"] = new("rare",7,true) };
    var checks=0;
    void Check(bool pass,string message) { if(!pass) throw new InvalidOperationException(message); checks++; }
    async Task<HudSkinTarget> Target(HudSkinKey key,Action<HudSkinDraft> edit) {
        var snapshot=await store.LoadAsync(key); var draft=HudSkinCodec.Decode(snapshot).Draft.Copy(); edit(draft);
        return new(snapshot,draft,limits,stickers,chains,true,null);
    }
    async Task Reset() {
        await sql.ExecuteAsync("DELETE FROM wp_player_skins; DELETE FROM cw_site_selections; DELETE FROM cw_skins; DELETE FROM cw_access; DELETE FROM plus_active");
        await sql.ExecuteAsync("INSERT INTO plus_active VALUES (@steam,DATE_ADD(CONVERT_TZ(UTC_TIMESTAMP(),'+00:00','+03:00'),INTERVAL 1 HOUR)); INSERT INTO wp_player_skins (steamid,weapon_team,weapon_defindex,weapon_paint_id,weapon_stattrak_count,weapon_composition_version) VALUES (@steam,2,7,44,10,1),(@steam,3,7,55,20,1)",new {steam});
    }
    async Task<HudApplyResult> Apply(params HudSkinTarget[] targets)=>await store.ApplyAsync(targets,models);
    await Reset();
    var pending=await Target(key2,d=>d.Seed=17);
    var wrongUuid=new HudSkinStore(Connect,ops,_=>semaphore,33,new HudDatabaseIdentity("11111111-1111-1111-1111-111111111111",identity.Database));
    var wrongDb=new HudSkinStore(Connect,ops,_=>semaphore,33,new HudDatabaseIdentity(identity.ServerUuid,identity.Database+"_different"));
    var noIdentity=new HudSkinStore(Connect,ops,_=>semaphore,33);
    foreach(var denied in new[]{wrongUuid,wrongDb,noIdentity}) {
        bool deniedLoad=false;try {await denied.LoadAsync(key2);}catch(InvalidOperationException){deniedLoad=true;}
        Check(deniedLoad,"mismatched/missing custom database identity must deny load");
        Check(await denied.ApplyAsync(new[]{pending},models)==HudApplyResult.CustomUnavailable,"mismatched/missing custom database identity must deny apply");
    }
    Check(await sql.ExecuteScalarAsync<int>("SELECT weapon_seed FROM wp_player_skins WHERE steamid=@steam AND weapon_team=2",new {steam})==0,"identity mismatch mutated skin");
    await sql.ExecuteAsync("UPDATE plus_active SET expires_at='2000-01-01' WHERE steam_id=@steam",new {steam});
    Check(await Apply(pending)==HudApplyResult.PlusRequired,"expired Plus must reject");
    Check(await sql.ExecuteScalarAsync<int>("SELECT weapon_seed FROM wp_player_skins WHERE steamid=@steam AND weapon_team=2",new {steam})==0,"expired Plus mutated skin");
    await sql.ExecuteAsync("UPDATE plus_active SET expires_at=DATE_ADD(CONVERT_TZ(UTC_TIMESTAMP(),'+00:00','+03:00'),INTERVAL 1 HOUR) WHERE steam_id=@steam",new {steam});
    Check(await Apply(pending)==HudApplyResult.Saved,"renewed Plus should permit unchanged snapshot");
    await Reset(); pending=await Target(key2,d=>d.Seed=18);
    await sql.ExecuteAsync("UPDATE wp_player_skins SET weapon_nametag='website update' WHERE steamid=@steam AND weapon_team=2",new {steam});
    Check(await Apply(pending)==HudApplyResult.Conflict,"website conflict must reject");
    Check(await sql.ExecuteScalarAsync<int>("SELECT weapon_seed FROM wp_player_skins WHERE steamid=@steam AND weapon_team=2",new {steam})==0,"conflict mutated skin");
    await Reset(); var a=await Target(key2,d=>d.Seed=21); var b=await Target(key3,d=>d.Seed=22);
    await sql.ExecuteAsync("CREATE TRIGGER hud_fail_second BEFORE UPDATE ON wp_player_skins FOR EACH ROW BEGIN IF NEW.weapon_team=3 THEN SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='injected'; END IF; END");
    bool fault=false; try { await Apply(a,b); } catch(MySqlException) { fault=true; }
    await sql.ExecuteAsync("DROP TRIGGER hud_fail_second");
    Check(fault,"second team failure must propagate");
    Check(await sql.ExecuteScalarAsync<int>("SELECT SUM(weapon_seed) FROM wp_player_skins WHERE steamid=@steam",new {steam})==0,"both-side transaction was not rolled back");
    await Reset(); pending=await Target(key2,d=> { for(uint i=0;i<5;i++) d.Stickers[i]=new(101+i,i,0,0,0,1,0); d.Keychain=new(37,0,0,0,1,106); });
    Check(await Apply(pending)==HudApplyResult.Saved,"five stickers and slab save failed");
    var stored=await store.LoadAsync(key2); var decoded=HudSkinCodec.Decode(stored).Draft;
    Check(decoded.Stickers.Select(s=>s.Id).SequenceEqual(new uint[]{101,102,103,104,105}),"five sticker round trip");
    Check(decoded.Keychain?.Id==37 && decoded.Keychain.SlabSticker==106,"slab round trip");
    await Reset(); await sql.ExecuteAsync("UPDATE wp_player_skins SET weapon_sticker_0='malformed legacy',weapon_composition_version=0 WHERE steamid=@steam AND weapon_team=2",new {steam});
    pending=await Target(key2,d=>d.Seed=99); Check(await Apply(pending)==HudApplyResult.Saved,"independent legacy edit");
    Check(await sql.ExecuteScalarAsync<string>("SELECT weapon_sticker_0 FROM wp_player_skins WHERE steamid=@steam AND weapon_team=2",new {steam})=="malformed legacy","legacy raw data changed");
    pending=await Target(key2,d=>d.Stickers[1]=new(101,1,0,0,0,1,0)); Check(await Apply(pending)==HudApplyResult.Invalid,"undecodable legacy upgrade must reject");
    await Reset(); await sql.ExecuteAsync("UPDATE wp_player_skins SET weapon_stattrak=1 WHERE steamid=@steam AND weapon_team=2",new {steam});
    pending=await Target(key2,d=>d.Seed=31);
    Check(HudSkinCodec.Decode(pending.Expected).Draft.StatTrak,"true StatTrak must decode correctly");
    await sql.ExecuteAsync("UPDATE wp_player_skins SET weapon_stattrak_count=50 WHERE steamid=@steam AND weapon_team=2",new {steam});
    Check(await Apply(pending)==HudApplyResult.Saved,"kill counter must not cause conflict");
    Check(await sql.ExecuteScalarAsync<int>("SELECT weapon_stattrak_count FROM wp_player_skins WHERE steamid=@steam AND weapon_team=2",new {steam})==50,"stattrak regressed");
    Check(await sql.ExecuteScalarAsync<bool>("SELECT weapon_stattrak FROM wp_player_skins WHERE steamid=@steam AND weapon_team=2",new {steam}),"stattrak toggle regressed");
    await Reset();
    await sql.ExecuteAsync("INSERT INTO cw_skins (model,name,active) VALUES ('rare','Rare',1); INSERT INTO cw_access VALUES (@steam,'rare',33,0); INSERT INTO cw_site_selections (steamid64,weapon_team,weapon,model,revision) VALUES (@steam,0,'weapon_ak47','rare',1)",new{steam});
    var fallbackT=await Target(key2,d=>{});var fallbackCt=await Target(key3,d=>{});
    Check(fallbackT.Expected.Custom?.SourceTeam==0 && fallbackCt.Expected.Custom?.SourceTeam==0,"custom choice inherited from team0");
    Check(await store.ApplyAsync(new[]{fallbackT,fallbackCt},models)==HudApplyResult.Saved,"explicit same custom choice saves both sides");
    Check(await sql.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM cw_site_selections WHERE steamid64=@steam AND weapon_team IN(2,3) AND model='rare'",new{steam})==2,"same fallback custom materializes exact sides");
    await sql.ExecuteAsync("UPDATE cw_site_selections SET model='other',revision=revision+1 WHERE steamid64=@steam AND weapon_team=0",new{steam});
    Check((await store.LoadAsync(key2)).Custom?.Model=="rare" && (await store.LoadAsync(key3)).Custom?.Model=="rare","later team0 change cannot replace explicit side choices");
    await Reset(); await sql.ExecuteAsync("INSERT INTO cw_skins (model,name,active) VALUES ('rare','Rare',1)");
    pending=await Target(key2,d=>d.SelectCustom("rare"));
    Check(await Apply(pending)==HudApplyResult.CustomUnavailable,"private model without grant");
    await sql.ExecuteAsync("INSERT INTO cw_access VALUES (@steam,'rare',33,UNIX_TIMESTAMP()-1)",new {steam}); Check(await Apply(pending)==HudApplyResult.CustomUnavailable,"expired grant");
    await sql.ExecuteAsync("UPDATE cw_access SET expires=0,sid=34 WHERE steamid64=@steam",new {steam}); Check(await Apply(pending)==HudApplyResult.CustomUnavailable,"wrong server grant");
    await sql.ExecuteAsync("UPDATE cw_access SET sid=33 WHERE steamid64=@steam",new {steam});
    await sql.ExecuteAsync("UPDATE cw_skins SET active=0 WHERE model='rare'"); Check(await Apply(pending)==HudApplyResult.CustomUnavailable,"inactive model");
    await sql.ExecuteAsync("UPDATE cw_skins SET active=1 WHERE model='rare'; INSERT INTO cw_skins (model,name,active) VALUES ('rare','Other',1)"); Check(await Apply(pending)==HudApplyResult.CustomUnavailable,"ambiguous model");
    await sql.ExecuteAsync("DELETE FROM cw_skins WHERE name='Other'"); Check(await Apply(pending)==HudApplyResult.Saved,"valid custom entitlement");
    Check(await sql.ExecuteScalarAsync<string>("SELECT model FROM cw_site_selections WHERE steamid64=@steam AND weapon_team=2",new {steam})=="rare","custom selection missing");
    Check(await sql.ExecuteScalarAsync<int>("SELECT weapon_paint_id FROM wp_player_skins WHERE steamid=@steam AND weapon_team=2",new {steam})==44,"custom selection mutated stock paint");
    await Reset();
    pending=await Target(key2,d=> { d.Seed=40; d.Stickers[0]=new(101,0,0,0,0,1,0); });
    var editableSet = new HashSet<uint>(stickers);
    pending = pending with { Stickers=editableSet };
    using (var blocking=await ops.EnterAsync(steam)) {
        var queued=store.ApplyAsync(new[]{pending},models);
        pending.Draft.Seed=41; pending.Draft.Stickers[0]=new(102,0,0,0,0,1,0); editableSet.Clear();
        blocking.Dispose();
        Check(await queued.WaitAsync(TimeSpan.FromSeconds(5))==HudApplyResult.Saved,"queued draft/set mutation affected request");
    }
    var frozen=HudSkinCodec.Decode(await store.LoadAsync(key2)).Draft;
    Check(frozen.Seed==40 && frozen.Stickers[0].Id==101,"queued request did not freeze draft");
    await Reset(); await sql.ExecuteAsync("INSERT INTO cw_skins (model,name,active) VALUES ('rare','Rare',1); INSERT INTO cw_access VALUES (@steam,'rare',33,0)",new {steam});
    pending=await Target(key2,d=>d.SelectCustom("rare"));
    var mutableModels=new Dictionary<string,HudCustomModel>(models);
    using (var blocking=await ops.EnterAsync(steam)) {
        var queued=store.ApplyAsync(new[]{pending},mutableModels);
        mutableModels.Clear(); pending.Draft.SelectCustom(null);
        blocking.Dispose();
        Check(await queued.WaitAsync(TimeSpan.FromSeconds(5))==HudApplyResult.Saved,"queued model map mutation affected request");
    }
    Check(await sql.ExecuteScalarAsync<string>("SELECT model FROM cw_site_selections WHERE steamid64=@steam AND weapon_team=2",new {steam})=="rare","queued custom selection changed");
    await Reset(); pending=await Target(key2,d=>d.Seed=50);
    using (var blocking=await ops.EnterAsync(steam)) {
        using var cts=new CancellationTokenSource();
        var queued=store.ApplyAsync(new[]{pending},models,cts.Token); cts.Cancel();
        bool cancelled=false; try { await queued.WaitAsync(TimeSpan.FromSeconds(5)); } catch(OperationCanceledException) { cancelled=true; }
        Check(cancelled,"queued cancellation must complete promptly");
        blocking.Dispose();
    }
    using(var tail=await ops.EnterAsync(steam).WaitAsync(TimeSpan.FromSeconds(5))) { }
    Check(ops.PendingKeys==0,"cancelled FIFO lease leaked");
    Check(await sql.ExecuteScalarAsync<int>("SELECT weapon_seed FROM wp_player_skins WHERE steamid=@steam AND weapon_team=2",new {steam})==0,"cancelled operation wrote data");
    await Reset();pending=await Target(key2,d=>d.Seed=77);
    var guardCalls=0;
    Check(await store.ApplyAsync(new[]{pending},models,CancellationToken.None,()=>++guardCalls<3)==HudApplyResult.CustomUnavailable && guardCalls==3,"guard invalidation before commit must fail closed");
    Check(await sql.ExecuteScalarAsync<int>("SELECT weapon_seed FROM wp_player_skins WHERE steamid=@steam AND weapon_team=2",new{steam})==0,"precommit guard invalidation must rollback prior write");
    Check(await store.ApplyAsync(new[]{pending},models,CancellationToken.None,()=>false)==HudApplyResult.CustomUnavailable,"guard invalidation before queue must reject");
    checks += await CustomSelectionRaceTests.Run(sql,Connect,ops,semaphore,identity,steam);
    checks += await EquipmentTests.Run(sql,Connect,ops,semaphore,identity,steam);
    checks += await CosmeticTests.Run(sql,Connect,ops,semaphore,steam);
    checks += await CustomProductTests.Run(sql,Connect,identity,steam);
    Console.WriteLine($"PASS: {checks} real MySQL HUD persistence assertions"); return 0;
}
catch (MySqlException ex) { Console.Error.WriteLine("FAIL: MySQL error number " + ex.Number + " at " + ex.StackTrace); return 1; }
catch (InvalidOperationException ex) { Console.Error.WriteLine("FAIL: " + ex.Message); return 1; }
finally { MySqlConnection.ClearAllPools(); await admin.ExecuteAsync($"DROP DATABASE `{schema}`"); Console.WriteLine("CLEANUP: isolated schema removed"); }
