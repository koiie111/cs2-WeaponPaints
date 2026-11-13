using System.Collections.Frozen;
using Dapper;
using MySqlConnector;

namespace WeaponPaints.Hud;

internal enum HudCustomProductStatus { Available, Unowned, Disabled, Ambiguous }
internal sealed record HudCustomProductSpec(string Id,string Name,bool Private);
internal sealed record HudCustomProduct(string Id,string Name,bool Private,HudCustomProductStatus Status,decimal? BasePrice,long? ExpiresAtUnix)
{
    internal bool AvailableNow => Status==HudCustomProductStatus.Available && (ExpiresAtUnix is null || ExpiresAtUnix > DateTimeOffset.UtcNow.ToUnixTimeSeconds());
}

/// <summary>Read-only, per-session product/entitlement snapshot. Prices are unpersonalized base catalogue values.</summary>
internal sealed class HudCustomProducts
{
    internal IReadOnlyDictionary<string,HudCustomProduct> ById { get; }
    private HudCustomProducts(Dictionary<string,HudCustomProduct> products) => ById=products.ToFrozenDictionary(StringComparer.Ordinal);

    internal static async Task<HudCustomProducts> LoadAsync(Func<Task<MySqlConnection>> connect,HudDatabaseIdentity expected,int serverId,string steamId,IReadOnlyList<HudCustomProductSpec> models,CancellationToken token=default)
    {
        var input=models.ToArray();
        if(expected==null || !Guid.TryParseExact(expected.ServerUuid,"D",out var expectedUuid) || expectedUuid==Guid.Empty || string.IsNullOrEmpty(expected.Database) || serverId<1 || steamId.Length!=17 || !steamId.All(char.IsAsciiDigit) || input.Any(x=>string.IsNullOrWhiteSpace(x.Id) || x.Id.Length>64 || x.Name==null) || input.Select(x=>x.Id).Distinct(StringComparer.Ordinal).Count()!=input.Length) throw new ArgumentException("Invalid custom product source");
        var pending=connect();
        MySqlConnection opened;
        try { opened=await pending.WaitAsync(token).ConfigureAwait(false); }
        catch(OperationCanceledException) { _=DisposeAbandonedAsync(pending); throw; }
        await using var db=opened;
        token.ThrowIfCancellationRequested();
        await using var tx=await db.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead,token).ConfigureAwait(false);
        try {
            var identity=await db.QuerySingleAsync<(string? Uuid,string? Db)>(new CommandDefinition("SELECT @@server_uuid AS Uuid, DATABASE() AS Db",transaction:tx,cancellationToken:token)).ConfigureAwait(false);
            if(!Guid.TryParseExact(identity.Uuid,"D",out var actual) || actual!=expectedUuid || !string.Equals(identity.Db,expected.Database,StringComparison.Ordinal)) throw new InvalidOperationException("Custom product database identity mismatch");
            var rows=(await db.QueryAsync<ProductRow>(new CommandDefinition("SELECT model AS Model,name AS Name,active AS Active,price AS Price,event_only AS EventOnly FROM cw_skins WHERE model IS NOT NULL",transaction:tx,cancellationToken:token)).ConfigureAwait(false)).ToArray();
            var grants=(await db.QueryAsync<GrantRow>(new CommandDefinition("SELECT model AS Model,expires AS Expires FROM cw_access WHERE steamid64=@steam AND sid IN (0,@serverId) AND (expires=0 OR expires>UNIX_TIMESTAMP())",new{steam=steamId,serverId},tx,cancellationToken:token)).ConfigureAwait(false)).ToArray();
            var byModel=rows.GroupBy(r=>r.Model,StringComparer.Ordinal).ToDictionary(g=>g.Key,g=>g.Distinct().ToArray(),StringComparer.Ordinal);
            var access=grants.GroupBy(r=>r.Model,StringComparer.Ordinal).ToDictionary(g=>g.Key,g=>g.Any(x=>x.Expires==0)?(long?)null:g.Max(x=>x.Expires),StringComparer.Ordinal);
            var result=new Dictionary<string,HudCustomProduct>(StringComparer.Ordinal);
            foreach(var model in input) {
                byModel.TryGetValue(model.Id,out var records); var row=records is {Length:1}?records[0]:null;
                var ambiguous=records is {Length:>1}; var disabled=row!=null && row.Active!=1;
                bool hasGrant=access.TryGetValue(model.Id,out var expiry);
                var status=ambiguous?HudCustomProductStatus.Ambiguous:disabled?HudCustomProductStatus.Disabled:(!model.Private || hasGrant)?HudCustomProductStatus.Available:HudCustomProductStatus.Unowned;
                decimal? price=row is {Active:1,EventOnly:0,Price:>=0}?row.Price:null;
                if(ambiguous) price=null;
                result.Add(model.Id,new(model.Id,string.IsNullOrWhiteSpace(row?.Name)?model.Name:row.Name!,model.Private,status,price,model.Private && hasGrant?expiry:null));
            }
            await tx.CommitAsync(token).ConfigureAwait(false); return new(result);
        } catch { await tx.RollbackAsync(CancellationToken.None).ConfigureAwait(false);throw; }
    }
    private static async Task DisposeAbandonedAsync(Task<MySqlConnection> pending)
    {
        try { await using var connection=await pending.ConfigureAwait(false); } catch { /* Observe abandoned connection failure. */ }
    }
    private sealed record ProductRow(string Model,string? Name,int? Active,decimal? Price,int? EventOnly);
    private sealed record GrantRow(string Model,long Expires);
}
