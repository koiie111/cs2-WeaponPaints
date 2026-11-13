using Dapper;
using MySqlConnector;
using WeaponPaints;
using WeaponPaints.Hud;

internal static class EquipmentTests
{
    internal static async Task<int> Run(MySqlConnection sql,Func<Task<MySqlConnection>> connect,OrderedOperations ops,SemaphoreSlim gate,HudDatabaseIdentity identity,string steam)
    {
        var store=new HudSkinStore(connect,ops,_=>gate,33,identity);var models=new Dictionary<string,HudCustomModel>();int checks=0;
        void Check(bool ok,string message){if(!ok)throw new InvalidOperationException(message);checks++;}
        var limits=new HudCompositionLimits(new(0,1),0,new(0,0),new(0,0),null,null,null);
        async Task<HudSkinTarget> Target(HudSkinKey key,string equipment,int seed) {
            var old=await store.LoadAsync(key);var draft=HudSkinCodec.Decode(old).Draft.Copy();draft.Seed=seed;
            return new(old,draft,limits,new HashSet<uint>(),new HashSet<uint>(),true,equipment);
        }
        async Task Reset() {
            await sql.ExecuteAsync("DELETE FROM wp_player_skins; DELETE FROM wp_player_knife; DELETE FROM wp_player_gloves; DELETE FROM plus_active; INSERT INTO plus_active VALUES (@steam,DATE_ADD(CONVERT_TZ(UTC_TIMESTAMP(),'+00:00','+03:00'),INTERVAL 1 HOUR))",new{steam});
        }
        HudSkinKey Glove(int team,int definition)=>new(steam,team,definition,"",false,HudEquipmentKind.Glove);
        HudSkinKey Knife(int team,int definition)=>new(steam,team,definition,"weapon_knife",false,HudEquipmentKind.Knife);
        await Reset();
        var bf2=await Target(Glove(2,4725),"4725",11);var bf3=await Target(Glove(3,4725),"4725",12);
        Check(!bf2.Expected.SourceTeam.HasValue && bf2.Expected.Equipment==null,"Broken Fang initial load");
        Check(await store.ApplyAsync(new[]{bf2,bf3},models)==HudApplyResult.Saved,"Broken Fang both sides save");
        Check(await sql.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM wp_player_gloves WHERE steamid=@steam AND weapon_defindex=4725 AND weapon_team IN (2,3)",new{steam})==2,"Broken Fang equipment both sides");
        Check((await store.LoadAsync(Glove(2,4725))).Equipment=="4725" && (await store.LoadAsync(Glove(3,4725))).Equipment=="4725","Broken Fang both sides load");
        Check(await sql.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM wp_player_skins WHERE steamid=@steam AND weapon_defindex=4725 AND weapon_team IN (2,3)",new{steam})==2,"Broken Fang stock rows");
        await Reset();await sql.ExecuteAsync("INSERT INTO wp_player_gloves VALUES (@steam,2,5028),(@steam,3,5029)",new{steam});
        bf2=await Target(Glove(2,4725),"4725",11);bf3=await Target(Glove(3,4725),"4725",12);
        await sql.ExecuteAsync("CREATE TRIGGER hud_bf_fail BEFORE UPDATE ON wp_player_gloves FOR EACH ROW BEGIN IF NEW.weapon_team=3 THEN SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='injected'; END IF; END");
        bool bfFailed=false;try{await store.ApplyAsync(new[]{bf2,bf3},models);}catch(MySqlException){bfFailed=true;}await sql.ExecuteAsync("DROP TRIGGER hud_bf_fail");
        Check(bfFailed,"Broken Fang second side injected failure");
        Check(await sql.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM wp_player_gloves WHERE steamid=@steam AND ((weapon_team=2 AND weapon_defindex=5028) OR (weapon_team=3 AND weapon_defindex=5029))",new{steam})==2 && await sql.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM wp_player_skins WHERE steamid=@steam",new{steam})==0,"Broken Fang both sides rollback");
        await Reset();await sql.ExecuteAsync("INSERT INTO wp_player_knife VALUES (@steam,2,'weapon_bayonet'),(@steam,3,'weapon_bayonet')",new{steam});
        var kt=await Target(Knife(2,59),"weapon_knife",21);var kct=await Target(Knife(3,42),"weapon_knife",22);
        Check(await store.ApplyAsync(new[]{kt,kct},models)==HudApplyResult.Saved,"default knife 59/42 pair save");
        Check(await sql.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM wp_player_knife WHERE steamid=@steam AND knife='weapon_knife' AND weapon_team IN (2,3)",new{steam})==2,"default knife equipment both sides");
        Check(await sql.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM wp_player_skins WHERE steamid=@steam AND ((weapon_team=2 AND weapon_defindex=59) OR (weapon_team=3 AND weapon_defindex=42))",new{steam})==2,"default knife correct side definitions");
        await Reset();await sql.ExecuteAsync("INSERT INTO wp_player_knife VALUES (@steam,2,'weapon_bayonet'),(@steam,3,'weapon_bayonet')",new{steam});
        kt=await Target(Knife(2,59),"weapon_knife",21);kct=await Target(Knife(3,42),"weapon_knife",22);
        await sql.ExecuteAsync("CREATE TRIGGER hud_knife_fail BEFORE UPDATE ON wp_player_knife FOR EACH ROW BEGIN IF NEW.weapon_team=3 THEN SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='injected'; END IF; END");
        bool failed=false;try{await store.ApplyAsync(new[]{kt,kct},models);}catch(MySqlException){failed=true;}await sql.ExecuteAsync("DROP TRIGGER hud_knife_fail");
        Check(failed,"default knife second side injected failure");
        Check(await sql.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM wp_player_knife WHERE steamid=@steam AND knife='weapon_bayonet'",new{steam})==2 && await sql.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM wp_player_skins WHERE steamid=@steam",new{steam})==0,"default knife both sides rollback");
        await Reset();await sql.ExecuteAsync("INSERT INTO wp_player_gloves VALUES (@steam,2,4725),(@steam,3,4725)",new{steam});
        var gt=await Target(Glove(2,5028),"5028",31);var gct=await Target(Glove(3,5029),"5029",32);
        Check(await store.ApplyAsync(new[]{gt,gct},models)==HudApplyResult.Saved,"default gloves 5028/5029 pair save");
        Check(await sql.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM wp_player_gloves WHERE steamid=@steam AND ((weapon_team=2 AND weapon_defindex=5028) OR (weapon_team=3 AND weapon_defindex=5029))",new{steam})==2,"default gloves equipment correct sides");
        Check(await sql.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM wp_player_skins WHERE steamid=@steam AND ((weapon_team=2 AND weapon_defindex=5028) OR (weapon_team=3 AND weapon_defindex=5029))",new{steam})==2,"default gloves stock rows correct sides");
        await Reset();await sql.ExecuteAsync("INSERT INTO wp_player_gloves VALUES (@steam,2,4725),(@steam,3,4725)",new{steam});
        gt=await Target(Glove(2,5028),"5028",31);gct=await Target(Glove(3,5029),"5029",32);
        await sql.ExecuteAsync("CREATE TRIGGER hud_gloves_fail BEFORE UPDATE ON wp_player_gloves FOR EACH ROW BEGIN IF NEW.weapon_team=3 THEN SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='injected'; END IF; END");
        failed=false;try{await store.ApplyAsync(new[]{gt,gct},models);}catch(MySqlException){failed=true;}await sql.ExecuteAsync("DROP TRIGGER hud_gloves_fail");
        Check(failed,"default gloves second side injected failure");
        Check(await sql.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM wp_player_gloves WHERE steamid=@steam AND weapon_defindex=4725",new{steam})==2 && await sql.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM wp_player_skins WHERE steamid=@steam",new{steam})==0,"default gloves both sides rollback");
        var wrong=await Target(Glove(3,5030),"5030",32);Check(await store.ApplyAsync(new[]{gt,wrong},models)==HudApplyResult.Invalid,"mixed non-default glove definitions rejected");
        return checks;
    }
}
