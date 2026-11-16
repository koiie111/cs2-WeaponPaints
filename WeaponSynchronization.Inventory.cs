using Dapper;
namespace WeaponPaints;
internal partial class WeaponSynchronization
{
    internal async Task ResetInventoryAsync(string steam, int[] teams, int definition, string kind)
    {
        using var operation = await Operations.EnterAsync(steam);
        var gate = GetPlayerSkinWriteLock(steam); await gate.WaitAsync();
        try
        {
            await using var db = await _database.GetConnectionAsync();
            var name = "wp_player_skins:" + steam;
            if(await db.ExecuteScalarAsync<int?>("SELECT GET_LOCK(@name,2)",new {name}) != 1) throw new TimeoutException("Inventory reset lock unavailable");
            try
            {
                await using var tx = await db.BeginTransactionAsync();
                foreach(var team in teams) await NativeInventoryStore.ResetAsync(db,tx,steam,team,definition,kind);
                await tx.CommitAsync();
            }
            finally { await db.ExecuteScalarAsync<int?>("SELECT RELEASE_LOCK(@name)",new {name}); }
        }
        finally { gate.Release(); }
    }
}
