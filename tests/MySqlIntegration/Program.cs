using System.Reflection;
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
 var operations=(OrderedOperations)typeof(WeaponSynchronization).GetField("Operations",BindingFlags.NonPublic|BindingFlags.Static)!.GetValue(null)!;
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
 Console.WriteLine($"PASS: {passed} MySQL assertions; engine dispatch adapter; linked production SQL source");
}finally{Server.BeforeDispatch=null;MySqlConnection.ClearAllPools();await admin.ExecuteAsync($"DROP DATABASE `{schema}`");Console.WriteLine("CLEANUP: isolated schema removed");}
