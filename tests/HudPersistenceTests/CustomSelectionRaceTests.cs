using System.Data;
using System.Reflection;
using Dapper;
using MySqlConnector;
using WeaponPaints;
using WeaponPaints.Hud;

internal static class CustomSelectionRaceTests
{
    internal static async Task<int> Run(MySqlConnection control,Func<Task<MySqlConnection>> connect,OrderedOperations ops,SemaphoreSlim gate,HudDatabaseIdentity identity,string steam)
    {
        int checks=0;void Check(bool condition,string message){if(!condition)throw new InvalidOperationException(message);checks++;}
        await control.ExecuteAsync("DELETE FROM wp_player_skins; DELETE FROM cw_site_selections; DELETE FROM plus_active; INSERT INTO plus_active VALUES (@steam,DATE_ADD(CONVERT_TZ(UTC_TIMESTAMP(),'+00:00','+03:00'),INTERVAL 1 HOUR)); INSERT INTO wp_player_skins (steamid,weapon_team,weapon_defindex,weapon_paint_id,weapon_seed) VALUES (@steam,2,7,44,0),(@steam,3,7,44,0); INSERT INTO cw_site_selections (steamid64,weapon_team,weapon,model,revision) VALUES (@steam,0,'weapon_ak47','legacy',1)",new{steam});
        var store=new HudSkinStore(connect,ops,_=>gate,33,identity);
        var k2=new HudSkinKey(steam,2,7,"weapon_ak47",true);var k3=k2 with {Team=3};
        var t=await store.LoadAsync(k2);var ct=await store.LoadAsync(k3);
        Check(t.Custom?.SourceTeam==0 && ct.Custom?.SourceTeam==0,"race fixture reads team0 fallback");
        var write=typeof(HudSkinStore).GetMethod("WriteCustomSelectionAsync",BindingFlags.Static|BindingFlags.NonPublic) ?? throw new InvalidOperationException("production selection helper missing");
        await using var writer=await connect();await using var transaction=await writer.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        try {
            // Match the production locking read, but explicitly use READ COMMITTED
            // so another writer can insert the missing team-specific row.
            await writer.QueryAsync("SELECT model,revision,weapon_team FROM cw_site_selections WHERE steamid64=@steam AND weapon_team IN (0,2,3) AND weapon='weapon_ak47' ORDER BY weapon_team DESC FOR UPDATE",new{steam},transaction);
            await writer.ExecuteAsync("UPDATE wp_player_skins SET weapon_seed=weapon_team*10 WHERE steamid=@steam AND weapon_team IN(2,3)",new{steam},transaction);
            var first=(Task<bool>)write.Invoke(null,new object[]{writer,transaction,t,"",CancellationToken.None})!;
            Check(await first,"first-side production insert succeeds");
            // Independent session inserts after the locked fallback was read.
            await control.ExecuteAsync("INSERT INTO cw_site_selections (steamid64,weapon_team,weapon,model,revision) VALUES (@steam,3,'weapon_ak47','external',1)",new{steam});
            var second=(Task<bool>)write.Invoke(null,new object[]{writer,transaction,ct,"",CancellationToken.None})!;
            Check(!await second,"rival exact insert produces conflict");
            await transaction.RollbackAsync();
            Check(await control.ExecuteScalarAsync<string>("SELECT model FROM cw_site_selections WHERE steamid64=@steam AND weapon_team=3 AND weapon='weapon_ak47'",new{steam})=="external","rival choice survives");
            Check(await control.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM cw_site_selections WHERE steamid64=@steam AND weapon_team=2",new{steam})==0,"first-side selection rolls back");
            Check(await control.ExecuteScalarAsync<int>("SELECT SUM(weapon_seed) FROM wp_player_skins WHERE steamid=@steam AND weapon_team IN (2,3)",new{steam})==0,"both earlier stock writes roll back");
        } catch { await transaction.RollbackAsync();throw; }
        return checks;
    }
}
