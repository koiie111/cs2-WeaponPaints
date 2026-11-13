using Dapper;
using MySqlConnector;
using WeaponPaints.Hud;

internal static class CustomProductTests
{
    internal static async Task<int> Run(MySqlConnection sql,Func<Task<MySqlConnection>> connect,HudDatabaseIdentity identity,string steam)
    {
        int checks=0;void Check(bool ok,string message){if(!ok)throw new InvalidOperationException(message);checks++;}
        await sql.ExecuteAsync("DELETE FROM cw_skins;DELETE FROM cw_access; INSERT INTO cw_skins (model,name,active,price,event_only) VALUES ('owned','Owned',1,150,0),('event','Event',1,250,1),('expired','Expired',1,100,0),('sid','Other server',1,99,0),('disabled','Disabled',0,110,0),('ambiguous','First',1,100,0),('ambiguous','Second',1,200,0); INSERT INTO cw_access (steamid64,model,sid,expires) VALUES (@steam,'owned',33,0),(@steam,'event',0,UNIX_TIMESTAMP()+3600),(@steam,'expired',33,UNIX_TIMESTAMP()-1),(@steam,'sid',34,0)",new{steam});
        var specs=new[]{new HudCustomProductSpec("owned","config",true),new("event","config",true),new("expired","config",true),new("sid","config",true),new("disabled","config",true),new("ambiguous","config",true),new("missingpublic","config",false),new("OWNED","case",true)};
        var result=await HudCustomProducts.LoadAsync(connect,identity,33,steam,specs);
        var by=result.ById;
        Check(by.Count==specs.Length,"all configured models remain browsable");
        Check(by["owned"] is {Status:HudCustomProductStatus.Available,BasePrice:150,ExpiresAtUnix:null} && by["owned"].AvailableNow,"permanent grant/base price");
        Check(by["event"] is {Status:HudCustomProductStatus.Available,BasePrice:null,ExpiresAtUnix:not null} && by["event"].AvailableNow,"event-only grant with no sale price");
        Check(by["expired"].Status==HudCustomProductStatus.Unowned && !by["expired"].AvailableNow,"expired grant denied");
        Check(by["sid"].Status==HudCustomProductStatus.Unowned,"different server grant denied");
        Check(by["disabled"] is {Status:HudCustomProductStatus.Disabled,BasePrice:null},"inactive model no offer");
        Check(by["ambiguous"] is {Status:HudCustomProductStatus.Ambiguous,BasePrice:null},"ambiguous model no offer");
        Check(by["missingpublic"] is {Status:HudCustomProductStatus.Available,BasePrice:null},"public configured model available without false price");
        Check(by["OWNED"] is {Status:HudCustomProductStatus.Unowned,BasePrice:null},"case-exact model identity");
        Check(!new HudCustomProduct("x","x",true,HudCustomProductStatus.Available,null,DateTimeOffset.UtcNow.ToUnixTimeSeconds()-1).AvailableNow,"cached deadline expires locally");
        foreach(var bad in new[]{new HudDatabaseIdentity("11111111-1111-1111-1111-111111111111",identity.Database),new HudDatabaseIdentity(identity.ServerUuid,identity.Database+"_other")}) {
            bool rejected=false;try {await HudCustomProducts.LoadAsync(connect,bad,33,steam,specs);}catch(InvalidOperationException){rejected=true;}
            Check(rejected,"database identity mismatch");
        }
        return checks;
    }
}
