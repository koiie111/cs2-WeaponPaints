using Dapper;
using MySqlConnector;
namespace WeaponPaints;

internal static class NativeInventoryStore
{
    internal static readonly int[] Knives = [42,59,500,503,505,506,507,508,509,512,514,515,516,517,518,519,520,521,522,523,525,526];
    internal static async Task ResetAsync(MySqlConnection db, MySqlTransaction tx, string steam, int team, int definition, string kind)
    {
        var definitions = kind == "knife" ? Knives : kind == "glove" ? Enumerable.Range(5027,9).ToArray() : [definition];
        await db.ExecuteAsync("DELETE FROM wp_player_skins WHERE steamid=@steam AND weapon_team=@team AND weapon_defindex IN @definitions", new {steam,team,definitions},tx);
        // Explicit neutral rows mask any team-0 preference and block stale counter snapshots.
        foreach (var index in definitions)
            await db.ExecuteAsync("INSERT INTO wp_player_skins (steamid,weapon_team,weapon_defindex,weapon_paint_id) VALUES (@steam,@team,@index,0)",new {steam,team,index},tx);
        if(kind == "knife") await db.ExecuteAsync("INSERT INTO wp_player_knife (steamid,weapon_team,knife) VALUES (@steam,@team,'weapon_knife') ON DUPLICATE KEY UPDATE knife=VALUES(knife)",new {steam,team},tx);
        if(kind == "glove") await db.ExecuteAsync("INSERT INTO wp_player_gloves (steamid,weapon_team,weapon_defindex) VALUES (@steam,@team,0) ON DUPLICATE KEY UPDATE weapon_defindex=0",new {steam,team},tx);
    }
}
