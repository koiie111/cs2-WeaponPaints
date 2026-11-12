using System.Reflection;
using System.Text.Json;
using Dapper;
using MySqlConnector;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Modules.Utils;
using WeaponPaints;
using WP=WeaponPaints.WeaponPaints;
var builder=new MySqlConnectionStringBuilder(Environment.GetEnvironmentVariable("WP_TEST_CONNECTION") ?? throw new Exception("Set WP_TEST_CONNECTION"));
if(builder.Server is not ("127.0.0.1" or "localhost")) throw new Exception("Local disposable MySQL only");
var schema="wp_test_"+Guid.NewGuid().ToString("N");builder.Database="";
await using var admin=new MySqlConnection(builder.ConnectionString);await admin.OpenAsync();
await admin.ExecuteAsync($"CREATE DATABASE `{schema}` CHARACTER SET utf8mb4");builder.Database=schema;builder.DefaultCommandTimeout=5;
var passed=0;
void Check(bool condition,string name){if(!condition)throw new Exception("FAIL: "+name);passed++;Console.WriteLine("PASS: "+name);}
try{
 await using var sql=new MySqlConnection(builder.ConnectionString);await sql.OpenAsync();
 await sql.ExecuteAsync(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory,"schema.sql")));
 Console.WriteLine("MySQL "+await sql.ExecuteScalarAsync<string>("SELECT VERSION()"));
 var config=new WeaponPaintsConfig();var sync=new WeaponSynchronization(new Database(builder.ConnectionString),config);WP.WeaponSync=sync;
 var player=new PlayerInfo{Slot=1,SteamId="76561198000000001"};var id=player.SteamId;var t=CsTeam.Terrorist;var ct=CsTeam.CounterTerrorist;
 Utilities.Players[1]=new Controller{SteamID=ulong.Parse(id)};long request=0;
 Task<bool> Load(){WP.PlayerDataLoading[1]=++request;return sync.GetPlayerData(player,request,WP.DataGeneration);}
 await sql.ExecuteAsync("INSERT INTO wp_player_knife VALUES (@id,0,'weapon_knife'),(@id,3,'weapon_knife_karambit'); INSERT INTO wp_player_gloves VALUES (@id,0,5030); INSERT INTO wp_player_music VALUES (@id,2,5),(@id,3,7); INSERT INTO wp_player_pins VALUES (@id,0,8); INSERT INTO wp_player_agents VALUES (@id,'ct','t')",new{id});
 var unicode="\u0422\u0435\u0441\u0442 \U0001F414";
 await sql.ExecuteAsync("INSERT INTO wp_player_skins (steamid,weapon_team,weapon_defindex,weapon_paint_id,weapon_seed,weapon_stattrak,weapon_stattrak_count,weapon_nametag,weapon_sticker_0,weapon_keychain) VALUES (@id,0,7,44,123,1,10,@name,'4294967295;0;0.1;0.2;0.3;1.5;45','4294967295;0.1;0.2;0.3;4294967295'),(@id,3,7,55,124,1,11,'CT','malformed','malformed')",new{id,name=unicode});
 Check(await Load(),"six-table load and publication");
 Check(WP.GPlayersKnife[1][t]=="weapon_knife"&&WP.GPlayersKnife[1][ct]=="weapon_knife_karambit","fallback team and override");
 Check(WP.GPlayersMusic[1][t]==5&&WP.GPlayersMusic[1][ct]==7&&WP.GPlayersPin[1][t]==8&&WP.GPlayersGlove[1][ct]==5030,"music pin glove mapping");
 Check(WP.GPlayersAgent[1]==("ct","t"),"agent tuple mapping");
 var skins=WP.GPlayerWeaponsInfo[1];Check(skins[t][7].Paint==44&&skins[ct][7].Paint==55&&skins[t][7].Nametag==unicode,"skin overrides and UTF8");
 Check(skins[t][7].KeyChain!.Seed==uint.MaxValue&&skins[t][7].Stickers[0].Id==uint.MaxValue,"uint32 cosmetics parser");
 Check(skins[ct][7].KeyChain!.Id==0&&skins[ct][7].Stickers.Count==5&&skins[ct][7].Stickers.All(x=>x.Id==0),"malformed cosmetics fallback");
 for(var mask=0;mask<64;mask++) {
  config.Additional.KnifeEnabled=(mask&1)!=0;config.Additional.GloveEnabled=(mask&2)!=0;config.Additional.AgentEnabled=(mask&4)!=0;config.Additional.MusicEnabled=(mask&8)!=0;config.Additional.SkinEnabled=(mask&16)!=0;config.Additional.PinsEnabled=(mask&32)!=0;
  Check(await Load() && WP.GPlayersKnife.ContainsKey(1)==config.Additional.KnifeEnabled && WP.GPlayersGlove.ContainsKey(1)==config.Additional.GloveEnabled && WP.GPlayersAgent.ContainsKey(1)==config.Additional.AgentEnabled && WP.GPlayersMusic.ContainsKey(1)==config.Additional.MusicEnabled && WP.GPlayerWeaponsInfo.ContainsKey(1)==config.Additional.SkinEnabled && WP.GPlayersPin.ContainsKey(1)==config.Additional.PinsEnabled,"feature flags mask "+mask);
 }
 config.Additional.KnifeEnabled=config.Additional.GloveEnabled=config.Additional.AgentEnabled=config.Additional.MusicEnabled=config.Additional.SkinEnabled=config.Additional.PinsEnabled=true;
 Check(await Load(),"restore full feature snapshot"); skins=WP.GPlayerWeaponsInfo[1];
 await sync.SyncKnifeToDatabase(player,"weapon_knife_butterfly",[t]);await sync.SyncGloveToDatabase(player,5031,[ct]);await sync.SyncMusicToDatabase(player,22,[t,ct]);await sync.SyncPinToDatabase(player,9,[ct]);WP.GPlayersAgent[1]=("ct","t");await sync.SyncAgentToDatabase(player);
 Check(await sql.ExecuteScalarAsync<string>("SELECT knife FROM wp_player_knife WHERE steamid=@id AND weapon_team=2",new{id})=="weapon_knife_butterfly","knife upsert");
 Check(await sql.ExecuteScalarAsync<int>("SELECT weapon_defindex FROM wp_player_gloves WHERE steamid=@id AND weapon_team=3",new{id})==5031,"glove upsert");
 Check(await sql.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM wp_player_music WHERE steamid=@id AND music_id=22",new{id})==2,"music upsert");
 Check(await sql.ExecuteScalarAsync<int>("SELECT id FROM wp_player_pins WHERE steamid=@id AND weapon_team=3",new{id})==9,"pin upsert");
 Check(await sql.ExecuteScalarAsync<string>("SELECT agent_ct FROM wp_player_agents WHERE steamid=@id",new{id})=="ct","agent upsert");
 skins[t][7].Paint=100;skins[t][7].StatTrakCount=200;await sync.SyncWeaponPaintsToDatabase(player);
 Check(await sql.ExecuteScalarAsync<int>("SELECT weapon_paint_id FROM wp_player_skins WHERE steamid=@id AND weapon_team=2 AND weapon_defindex=7",new{id})==100,"batch insert");
 skins[ct][7].Paint=101;await sync.SyncWeaponPaintsToDatabase(player);
 Check(await sql.ExecuteScalarAsync<string>("SELECT weapon_nametag FROM wp_player_skins WHERE steamid=@id AND weapon_team=3 AND weapon_defindex=7",new{id})=="CT","batch update preserves cosmetics");
 await sync.SyncStatTrakToDatabase(player,skins);
 Check(await sql.ExecuteScalarAsync<int>("SELECT weapon_stattrak_count FROM wp_player_skins WHERE steamid=@id AND weapon_team=2 AND weapon_defindex=7",new{id})==200,"disconnect StatTrak snapshot");
 // Simulate kills earned while SQL is in flight, then a website paint change.
 var beforePublish=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
 var allowPublish=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
 Server.BeforeDispatch=async()=>{beforePublish.TrySetResult();await allowPublish.Task;};
 var counterReload=Load();await beforePublish.Task.WaitAsync(TimeSpan.FromSeconds(10));
 skins[t][7].StatTrakCount=207;skins[ct][7].StatTrakCount=311;allowPublish.TrySetResult();
 Check(await counterReload&&WP.GPlayerWeaponsInfo[1][t][7].StatTrakCount==207&&WP.GPlayerWeaponsInfo[1][ct][7].StatTrakCount==311,"kills during refresh survive per-team snapshot publication");
 Server.BeforeDispatch=null;
 await sql.ExecuteAsync("UPDATE wp_player_skins SET weapon_paint_id=777,weapon_stattrak=0 WHERE steamid=@id AND weapon_team=2 AND weapon_defindex=7",new{id});
 Check(await Load()&&WP.GPlayerWeaponsInfo[1][t][7].Paint==777&&!WP.GPlayerWeaponsInfo[1][t][7].StatTrak&&WP.GPlayerWeaponsInfo[1][t][7].StatTrakCount==207,"new paint and disabled StatTrak retain unsaved kill count");
 await sync.SyncStatTrakToDatabase(player,WP.GPlayerWeaponsInfo[1]);
 skins[t][7].StatTrakCount=200;
 await sync.SyncStatTrakToDatabase(player,skins);
 Check(await sql.ExecuteScalarAsync<int>("SELECT weapon_stattrak_count FROM wp_player_skins WHERE steamid=@id AND weapon_team=2 AND weapon_defindex=7",new{id})==207,"late lower counter snapshot cannot reset saved kills");
 Check(!await sql.ExecuteScalarAsync<bool>("SELECT weapon_stattrak FROM wp_player_skins WHERE steamid=@id AND weapon_team=2 AND weapon_defindex=7",new{id}),"late true counter snapshot cannot re-enable website-disabled StatTrak");
 await sql.ExecuteAsync("UPDATE wp_player_skins SET weapon_stattrak_count=400,weapon_stattrak=1 WHERE steamid=@id AND weapon_team=2 AND weapon_defindex=7",new{id});
 Check(await Load()&&WP.GPlayerWeaponsInfo[1][t][7].StatTrakCount==400&&WP.GPlayerWeaponsInfo[1][t][7].StatTrak,"larger persisted count and re-enabled StatTrak survive refresh");
 var operations=(OrderedOperations)typeof(WeaponSynchronization).GetField("Operations",BindingFlags.NonPublic|BindingFlags.Static)!.GetValue(null)!;
 await sync.SyncStatTrakChoiceToDatabase(player,t,7,false);
 Check(!await sql.ExecuteScalarAsync<bool>("SELECT weapon_stattrak FROM wp_player_skins WHERE steamid=@id AND weapon_team=2 AND weapon_defindex=7",new{id}),"explicit command disable persists immediately");
 skins[t][7].StatTrak=true;skins[t][7].StatTrakCount=900;
 await sync.SyncStatTrakToDatabase(player,skins);
 Check(!await sql.ExecuteScalarAsync<bool>("SELECT weapon_stattrak FROM wp_player_skins WHERE steamid=@id AND weapon_team=2 AND weapon_defindex=7",new{id})&&await sql.ExecuteScalarAsync<int>("SELECT weapon_stattrak_count FROM wp_player_skins WHERE steamid=@id AND weapon_team=2 AND weapon_defindex=7",new{id})==900,"stale disconnect can increase count without undoing explicit disable");
 await sync.SyncStatTrakChoiceToDatabase(player,t,7,true);
 skins[t][7].StatTrak=false;await sync.SyncStatTrakToDatabase(player,skins);
 Check(await sql.ExecuteScalarAsync<bool>("SELECT weapon_stattrak FROM wp_player_skins WHERE steamid=@id AND weapon_team=2 AND weapon_defindex=7",new{id}),"stale false counter snapshot cannot undo explicit enable");
 using(var hold=await operations.EnterAsync(id)){
  var off=sync.SyncStatTrakChoiceToDatabase(player,t,7,false);var on=sync.SyncStatTrakChoiceToDatabase(player,t,7,true);var reload=Load();
  hold.Dispose();await off;await on;Check(await reload&&WP.GPlayerWeaponsInfo[1][t][7].StatTrak,"rapid explicit toggles and immediate refresh keep FIFO order");
 }
 Check(await sql.ExecuteScalarAsync<int>("SELECT weapon_stattrak_count FROM wp_player_skins WHERE steamid=@id AND weapon_team=2 AND weapon_defindex=7",new{id})==900,"explicit toggle cannot decrease a larger persisted kill count");
 await sql.ExecuteAsync("ALTER TABLE wp_player_skins ADD COLUMN weapon_keychain_sticker INT UNSIGNED NOT NULL DEFAULT 0,ADD COLUMN weapon_composition_version TINYINT UNSIGNED NOT NULL DEFAULT 0");
 await sql.ExecuteAsync("INSERT INTO wp_player_skins (steamid,weapon_team,weapon_defindex,weapon_paint_id,weapon_nametag,weapon_sticker_0,weapon_keychain,weapon_keychain_sticker,weapon_composition_version,weapon_stattrak_count) VALUES (@id,0,4,44,'fallback name','4550;0;0;0;0;1;0','37;0.1;0.2;0.3;1',4551,1,321)",new{id});
 Check(await Load(),"load decorated team-zero fallback before toggle");
 await sync.SyncStatTrakChoiceToDatabase(player,t,4,true);
 var cloned=await sql.QuerySingleAsync<dynamic>("SELECT * FROM wp_player_skins WHERE steamid=@id AND weapon_team=2 AND weapon_defindex=4",new{id});
 Check((bool)cloned.weapon_stattrak&&(int)cloned.weapon_stattrak_count==321&&(string)cloned.weapon_nametag=="fallback name"&&(string)cloned.weapon_sticker_0=="4550;0;0;0;0;1;0"&&(string)cloned.weapon_keychain=="37;0.1;0.2;0.3;1"&&(uint)cloned.weapon_keychain_sticker==4551&&(int)cloned.weapon_composition_version==1,"explicit team toggle copies full fallback appearance including slab/version");
 Check(!await sql.ExecuteScalarAsync<bool>("SELECT weapon_stattrak FROM wp_player_skins WHERE steamid=@id AND weapon_team=0 AND weapon_defindex=4",new{id})&&!WP.GPlayerWeaponsInfo[1][ct][4].StatTrak,"explicit team toggle leaves opposite team and shared fallback unchanged");
 await sql.ExecuteAsync("ALTER TABLE wp_player_skins DROP COLUMN weapon_keychain_sticker,DROP COLUMN weapon_composition_version");
 await sync.SyncStatTrakChoiceToDatabase(player,t,4,false);
 Check(!await sql.ExecuteScalarAsync<bool>("SELECT weapon_stattrak FROM wp_player_skins WHERE steamid=@id AND weapon_team=2 AND weapon_defindex=4",new{id}),"explicit toggle supports pre-composition schema");
 await sql.ExecuteAsync("DELETE FROM wp_player_skins WHERE steamid=@id AND weapon_defindex=4",new{id});
 skins=WP.GPlayerWeaponsInfo[1];
 using(var hold=await operations.EnterAsync(id)){skins[t][7].Paint=300;var save=sync.SyncWeaponPaintsToDatabase(player);skins[t][7].Paint=400;hold.Dispose();await save;}
 Check(await sql.ExecuteScalarAsync<int>("SELECT weapon_paint_id FROM wp_player_skins WHERE steamid=@id AND weapon_team=2 AND weapon_defindex=7",new{id})==300,"immutable save before wait");
 skins[t][7].Paint=500;var saving=sync.SyncWeaponPaintsToDatabase(player);var reconnecting=Load();await saving;Check(await reconnecting&&WP.GPlayerWeaponsInfo[1][t][7].Paint==500,"real read-after-write");
 foreach(var scenario in new[]{"slot reuse","newer request","map change","unload"}){
  var reached=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
  Server.BeforeDispatch=async()=>{reached.TrySetResult();await release.Task;};var before=WP.GPlayerWeaponsInfo[1];var load=Load();await reached.Task.WaitAsync(TimeSpan.FromSeconds(10));
  if(scenario=="slot reuse")Utilities.Players[1].SteamID++;else if(scenario=="newer request")WP.PlayerDataLoading[1]=++request;else WP.DataGeneration++;
  release.TrySetResult();Check(!await load&&ReferenceEquals(before,WP.GPlayerWeaponsInfo[1]),"late SQL rejected: "+scenario);Utilities.Players[1].SteamID=ulong.Parse(id);Server.BeforeDispatch=null;
 }
 var unreachable=new MySqlConnectionStringBuilder(builder.ConnectionString){Port=9,ConnectionTimeout=1,Pooling=false};
 WP.PlayerDataLoading[1]=++request;
 Check(!await new WeaponSynchronization(new Database(unreachable.ConnectionString),config).GetPlayerData(player,request,WP.DataGeneration)&&!WP.PlayerDataLoading.ContainsKey(1),"connection refusal clears current marker");
 var old=WP.GPlayerWeaponsInfo[1];await sql.ExecuteAsync("RENAME TABLE wp_player_pins TO hidden_pins");
 Check(!await Load()&&ReferenceEquals(old,WP.GPlayerWeaponsInfo[1])&&!WP.PlayerDataLoading.ContainsKey(1),"SQL failure preserves snapshot and clears marker");
 await sql.ExecuteAsync("RENAME TABLE hidden_pins TO wp_player_pins");Check(await Load(),"SQL recovery");
 var lockName="wp_player_skins:"+id;await sql.ExecuteScalarAsync<int>("SELECT GET_LOCK(@lockName,0)",new{lockName});var watch=System.Diagnostics.Stopwatch.StartNew();var retry=sync.SyncStatTrakToDatabase(player,WP.GPlayerWeaponsInfo[1]);await Task.Delay(2300);await sql.ExecuteScalarAsync<int>("SELECT RELEASE_LOCK(@lockName)",new{lockName});await retry;Check(watch.ElapsedMilliseconds>=2000,"real lock timeout and retry recovery");
 await sql.ExecuteScalarAsync<int>("SELECT GET_LOCK(@lockName,0)",new{lockName});watch.Restart();await sync.SyncStatTrakToDatabase(player,WP.GPlayerWeaponsInfo[1]);Check(watch.Elapsed.TotalSeconds>=8&&watch.Elapsed.TotalSeconds<15,"four-attempt retry bound");await sql.ExecuteScalarAsync<int>("SELECT RELEASE_LOCK(@lockName)",new{lockName});await sync.SyncStatTrakToDatabase(player,WP.GPlayerWeaponsInfo[1]);Check(operations.PendingKeys==0,"locks and queue released after failure");
 await sql.ExecuteAsync("SET GLOBAL innodb_lock_wait_timeout=1"); MySqlConnection.ClearAllPools();
 await using(var blocker=new MySqlConnection(builder.ConnectionString)) {
  await blocker.OpenAsync();await using var transaction=await blocker.BeginTransactionAsync();
  await blocker.ExecuteAsync("SELECT * FROM wp_player_skins WHERE steamid=@id AND weapon_team=2 AND weapon_defindex=7 FOR UPDATE",new{id},transaction);
  WP.GPlayerWeaponsInfo[1][t][7].StatTrakCount=909;
  var blockedSave=sync.SyncStatTrakToDatabase(player,WP.GPlayerWeaponsInfo[1]);await Task.Delay(1500);await transaction.RollbackAsync();await blockedSave;
 }
 Check(Utility.Messages.Any(message=>message.StartsWith("Retrying player skin batch") && message.Contains("Lock wait timeout exceeded")) && await sql.ExecuteScalarAsync<int>("SELECT weapon_stattrak_count FROM wp_player_skins WHERE steamid=@id AND weapon_team=2 AND weapon_defindex=7",new{id})==909,"real InnoDB error 1205 retries and saves successfully");
 await Task.WhenAll(Enumerable.Range(2,16).Select(n=>sync.SyncMusicToDatabase(new PlayerInfo{Slot=n,SteamId=(76561198000000000UL+(ulong)n).ToString()},99,[t])));
 Check(await sql.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM wp_player_music WHERE music_id=99")==16,"parallel independent accounts persist without mixing identities");
 await sql.ExecuteAsync("UPDATE wp_player_skins SET weapon_sticker_0='bad',weapon_sticker_4='77;0;0;0;0;1;0' WHERE steamid=@id",new{id});
 Check(await Load() && WP.GPlayerWeaponsInfo[1][ct][7].Stickers.Count==5 && WP.GPlayerWeaponsInfo[1][ct][7].Stickers[4].Id==77 && WP.GPlayerWeaponsInfo[1][ct][7].Stickers[0].Id==0,"invalid early sticker does not shift slot four");
 await sql.ExecuteAsync("DELETE FROM wp_player_skins;DELETE FROM wp_player_knife;DELETE FROM wp_player_gloves;DELETE FROM wp_player_agents;DELETE FROM wp_player_music;DELETE FROM wp_player_pins");Check(await Load()&&!WP.GPlayersKnife.ContainsKey(1)&&WP.GPlayerWeaponsInfo[1].Count==0,"empty account clears old values");
 await sql.ExecuteAsync("CREATE TABLE plus_active (steam_id varchar(18) PRIMARY KEY,expires_at datetime NOT NULL); INSERT INTO plus_active VALUES (@id,'2030-01-01 03:00:00'); INSERT INTO wp_player_pets (steamid,pet_seed,pet_hat) VALUES (@id,4294967295,'wizard_hat')",new{id});
 await sql.ExecuteAsync("INSERT INTO wp_player_patches VALUES (@id,3,2,4550),(@id,2,0,4551),(@id,1,0,4552),(@id,3,7,4553)",new{id});
 Check(await Load() && WP.GPlayersPatches[1][3].SequenceEqual(new uint[]{0,0,4550}) && WP.GPlayersPatches[1][2].SequenceEqual(new uint[]{4551,0,0}) && WP.GPlayersPatches[1].Count==2,"native patch kits retain team and slot; invalid slots are ignored");
 Check(WP.GPlayersPatchExpiry[1]==new DateTime(2030,1,1,0,0,0,DateTimeKind.Utc),"patch entitlement converts subscription MSK to UTC");
 var petSource=await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory,"Pets.source.txt"));
 var petQuery=System.Text.RegularExpressions.Regex.Match(petSource,"new MySqlCommand\\(\"(SELECT CONVERT_TZ[^\"]+)\"").Groups[1].Value;
 Check(petQuery.Length>0,"pet query located in production source");
 var pet=(IDictionary<string,object>)(await sql.QuerySingleAsync(petQuery,new{id}));
 Check(Convert.ToUInt32(pet["pet_seed"])==uint.MaxValue && Convert.ToString(pet["pet_hat"])=="wizard_hat" && Convert.ToDateTime(pet.Values.First())==new DateTime(2030,1,1),"real pet SQL uint seed hat and UTC conversion");
 var plan=(await sql.QueryAsync("EXPLAIN "+petQuery,new{id})).Select(row=>(IDictionary<string,object>)row).ToArray();
 Check(plan.Any(row=>Convert.ToString(row["table"])=="p" && Convert.ToString(row["key"])=="PRIMARY"),"pet collation-safe join retains primary-key lookup");
 var batchQuery=System.Text.RegularExpressions.Regex.Match(petSource,"new MySqlCommand\\(\\$\"(SELECT a.steam_id,CONVERT_TZ[^\"]+)\"").Groups[1].Value.Replace("{string.Join(',', parameters)}","@p0");
 Check(batchQuery.Length>0 && (await sql.QueryAsync(batchQuery,new{p0=id})).Count()==1,"production round batch pet query");
 var defaultQuery=System.Text.RegularExpressions.Regex.Match(petSource,"new MySqlCommand\\(\"(INSERT IGNORE INTO wp_player_pets[^\"]+)\"").Groups[1].Value;
 await sql.ExecuteAsync("UPDATE wp_player_pets SET pet_enabled=0 WHERE steamid=@id",new{id});await sql.ExecuteAsync(defaultQuery,new{id});
 Check(await sql.ExecuteScalarAsync<int>("SELECT pet_enabled FROM wp_player_pets WHERE steamid=@id",new{id})==0,"default persistence does not overwrite disabled preference");
 await sql.ExecuteAsync("DELETE FROM wp_player_pets");
 pet=(IDictionary<string,object>)(await sql.QuerySingleAsync(petQuery,new{id}));
 Check(pet["pet_enabled"]==null,"active subscription with no pet preference LEFT JOIN");
 await sql.ExecuteAsync("DELETE FROM plus_active");Check(!(await sql.QueryAsync(petQuery,new{id})).Any(),"inactive subscription has no pet row");
 Check(await Load() && WP.GPlayersPatchExpiry[1]==DateTime.MinValue && WP.GPlayersPatches[1][3][2]==4550,"inactive patch entitlement fails closed without deleting the saved choice");
 // Earlier assertions intentionally exercise the pre-migration skin table.
 await sql.ExecuteAsync("ALTER TABLE wp_player_skins ADD weapon_keychain_sticker INT UNSIGNED NOT NULL DEFAULT 0, ADD weapon_composition_version TINYINT UNSIGNED NOT NULL DEFAULT 0");
 using(var fixtures = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory,"composition-roundtrip.json")))) {
  foreach(var fixture in fixtures.RootElement.GetProperty("cases").EnumerateArray()) {
   var row=fixture.GetProperty("storage");var fields=row.EnumerateObject().ToArray();var parameters=new DynamicParameters();parameters.Add("id",id);
   foreach(var field in fields) {
    if(!System.Text.RegularExpressions.Regex.IsMatch(field.Name,"^weapon_(team|defindex|paint_id|seed|wear|nametag|stattrak|composition_version|keychain|keychain_sticker|sticker_[0-4])$"))throw new Exception("Unexpected fixture column");
    parameters.Add(field.Name,field.Value.ValueKind==JsonValueKind.String?field.Value.GetString():(object)field.Value.GetDouble());
   }
   await sql.ExecuteAsync("DELETE FROM wp_player_skins WHERE steamid=@id",new{id});
   await sql.ExecuteAsync("INSERT INTO wp_player_skins (steamid,"+string.Join(',',fields.Select(field=>field.Name))+") VALUES (@id,"+string.Join(',',fields.Select(field=>"@"+field.Name))+")",parameters);
   Check(await Load(),"shared fixture load "+fixture.GetProperty("name").GetString());
   var loaded=WP.GPlayerWeaponsInfo[1][(CsTeam)row.GetProperty("weapon_team").GetInt32()][row.GetProperty("weapon_defindex").GetInt32()];
   Check(loaded.CompositionVersion==row.GetProperty("weapon_composition_version").GetInt32() && loaded.HasCustomization,"shared fixture default paint accepts decoration and explicit format");
   Check(loaded.Stickers.Select(sticker=>sticker.Schema).SequenceEqual(fixture.GetProperty("expectedSchemas").EnumerateArray().Select(value=>value.GetUInt32())),"shared fixture physical anchors and five draw slots");
   Check(Math.Abs(loaded.Wear-row.GetProperty("weapon_wear").GetSingle())<.000001f && loaded.Seed==row.GetProperty("weapon_seed").GetInt32(),"shared fixture paint wear/seed precision");
   if(fixture.TryGetProperty("input",out var input)) {
    var expected=input.GetProperty("stickers").EnumerateArray().ToArray();
    Check(loaded.Stickers.Select((sticker,index)=>sticker.Id==expected[index].GetProperty("id").GetUInt32() && (sticker.Id==0 ||
     Math.Abs(sticker.OffsetX-expected[index].GetProperty("x").GetSingle())<.000001f && Math.Abs(sticker.OffsetY-expected[index].GetProperty("y").GetSingle())<.000001f &&
     Math.Abs(sticker.Wear-expected[index].GetProperty("wear").GetSingle())<.000001f && Math.Abs(sticker.Scale-expected[index].GetProperty("scale").GetSingle())<.000001f &&
     Math.Abs(sticker.Rotation-expected[index].GetProperty("rotation").GetSingle())<.000001f)).All(value=>value),"shared fixture sticker units, positions, rotation and wear");
    var keychain=input.GetProperty("keychain");
    Check(loaded.KeyChain is {} charm && charm.Id==keychain.GetProperty("id").GetUInt32() && charm.Seed==keychain.GetProperty("seed").GetUInt32() && charm.Sticker==keychain.GetProperty("sticker").GetUInt32() &&
     Math.Abs(charm.OffsetX-keychain.GetProperty("x").GetSingle())<.000001f && Math.Abs(charm.OffsetY-keychain.GetProperty("y").GetSingle())<.000001f && Math.Abs(charm.OffsetZ-keychain.GetProperty("z").GetSingle())<.000001f,"shared fixture slab coordinates, embedded kit and seed");
   }
  }
 }
 config.Additional.GraffitiEnabled=true;
 await sql.ExecuteAsync("CREATE TABLE wp_player_graffiti (steamid VARCHAR(64) PRIMARY KEY,graffiti_id INT,tint_id INT); INSERT INTO wp_player_graffiti VALUES (@id,1671,0)",new{id});
 Check(await Load(),"graffiti selection loads with the regular identity-guarded snapshot");
 Check(WP.PlayerGraffiti.TryGetValue(player.Slot,out var graffiti)&&graffiti==(1671,0),"native graffiti kit and tint round trip");
 Check(!WP.HasGraffitiSubscription(player.Slot),"saved graffiti gives no permission without active 4ill+");
 await sql.ExecuteAsync("INSERT INTO plus_active VALUES (@id,'2030-01-01 03:00:00')",new{id});
 Check(await Load()&&WP.HasGraffitiSubscription(player.Slot)&&WP.PlayerGraffitiExpiry[player.Slot]==new DateTime(2030,1,1,0,0,0,DateTimeKind.Utc),"graffiti permission shares joined read and converts MSK expiry to UTC");
 WP.PlayerGraffitiExpiry[player.Slot]=DateTime.UtcNow.AddSeconds(-1);
 Check(!WP.HasGraffitiSubscription(player.Slot),"expiry denies the actual spray gate immediately without another SQL load");
 Check(await Load()&&WP.HasGraffitiSubscription(player.Slot),"renewed entitlement reload enables saved graffiti");
 await sql.ExecuteAsync("UPDATE plus_active SET expires_at='2000-01-01' WHERE steam_id=@id",new{id});
 Check(await Load()&&!WP.HasGraffitiSubscription(player.Slot)&&WP.PlayerGraffiti[player.Slot]==(1671,0),"expired subscription retains saved selection but cannot spray");
 await sql.ExecuteAsync("DELETE FROM plus_active WHERE steam_id=@id",new{id});
 Check(await Load()&&!WP.PlayerGraffitiExpiry.ContainsKey(player.Slot),"removed subscription clears cached graffiti permission");
 await sql.ExecuteAsync("INSERT INTO wp_player_pins VALUES (@id,0,8),(@id,3,9)",new{id});
 Check(await Load()&&WP.GPlayersPin[player.Slot][t]==8&&WP.GPlayersPin[player.Slot][ct]==9&&WP.PlayerGraffiti[player.Slot]==(1671,0),"batched pin fallback and team override preserve graffiti result");
 var reachedGraffiti=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var releaseGraffiti=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
 Server.BeforeDispatch=async()=>{reachedGraffiti.TrySetResult();await releaseGraffiti.Task;};
 var beforeGraffitiPins=WP.GPlayersPin[player.Slot];var delayedGraffiti=Load();await reachedGraffiti.Task.WaitAsync(TimeSpan.FromSeconds(10));
 Utilities.Players[player.Slot].SteamID++;releaseGraffiti.TrySetResult();
 Check(!await delayedGraffiti&&ReferenceEquals(beforeGraffitiPins,WP.GPlayersPin[player.Slot])&&WP.PlayerGraffiti[player.Slot]==(1671,0),"late batched graffiti/pin result cannot publish into a reused slot");
 Utilities.Players[player.Slot].SteamID=ulong.Parse(id);Server.BeforeDispatch=null;
 config.Additional.PinsEnabled=false;
 Check(await Load()&&!WP.GPlayersPin.ContainsKey(player.Slot)&&WP.PlayerGraffiti[player.Slot]==(1671,0),"graffiti-only grid does not read or publish disabled pins");
 config.Additional.PinsEnabled=true;
 await sql.ExecuteAsync("UPDATE wp_player_graffiti SET graffiti_id=999999 WHERE steamid=@id",new{id});
 Check(await Load(),"invalid graffiti does not block other cosmetics");
 Check(!WP.PlayerGraffiti.ContainsKey(player.Slot),"invalid catalogue pair removes prior override");
 await sql.ExecuteAsync("DELETE FROM wp_player_graffiti WHERE steamid=@id",new{id});
 Check(await Load()&&!WP.PlayerGraffiti.ContainsKey(player.Slot)&&WP.GPlayersPin[player.Slot][ct]==9,"empty graffiti result retains selected pin");
 config.Additional.GraffitiEnabled=false;
 await sql.ExecuteAsync("DROP TABLE wp_player_graffiti;DROP TABLE plus_active");
 Check(await Load()&&WP.GPlayersPin[player.Slot][ct]==9,"disabled graffiti supports the old schema without its table");
 Console.WriteLine($"PASS: {passed} MySQL assertions; engine dispatch adapter; linked production SQL source");
}finally{Server.BeforeDispatch=null;MySqlConnection.ClearAllPools();await admin.ExecuteAsync($"DROP DATABASE `{schema}`");Console.WriteLine("CLEANUP: isolated schema removed");}
