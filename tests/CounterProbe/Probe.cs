using System.Collections;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Timers;
using ProbeTimer = CounterStrikeSharp.API.Modules.Timers.Timer;

// Isolated QA only. No native item writes, identity changes or production SQL.
public sealed class CounterProbe : BasePlugin
{
    public override string ModuleName => "Skins3d isolated counter probe";
    public override string ModuleVersion => "1.0.0";
    private const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    private readonly Dictionary<string,List<double>> samples = new();
    private BasePlugin? plugin;
    private object? sync;
    private Type? infoType;
    private PropertyInfo? counterProperty;
    private MethodInfo? save;
    private IDictionary? allWeapons;
    private ProbeTimer? timer;
    private ActivityListener? sqlListener;
    private bool active, recording;
    private int pending, failures, started, completed, maximumCounter, epoch;
    private int sqlFailures;
    private string error = "";
    private long recordStart, recordEnd;
    private static bool Owns(Activity? activity)
    {
        while(activity!=null){if(activity.GetCustomProperty("Skins3d.CounterOwned") is true)return true;activity=activity.Parent;}
        return false;
    }
    private void Sample(string name, long start)
    {
        var now=Stopwatch.GetTimestamp();
        if(!recording||start<recordStart||now>recordEnd)return;
        if(!samples.TryGetValue(name,out var values))samples[name]=values=new();
        if(values.Count<100000)values.Add(Stopwatch.GetElapsedTime(start,now).TotalMilliseconds);
    }
    private CCSPlayerController[] Bots()
    {
        var players=Utilities.GetPlayers().Where(p=>p.IsValid&&!p.IsHLTV).ToArray();
        if(players.Length!=8||players.Any(p=>!p.IsBot||p.SteamID!=0||!p.PawnIsAlive))throw new InvalidOperationException("Eight natural live bots and no humans required");
        return players;
    }
    private void Stop()
    {
        active=recording=false;
        if(timer==null)return;
        timer.Kill();Timers.Remove(timer);timer=null;
    }
    private void Cycle()
    {
        if(!active||pending!=0)return;
        try
        {
            var bots=Bots();var expected=epoch;
            foreach(var bot in bots)
            {
                if(allWeapons![bot.Slot] is not IDictionary teams)throw new InvalidOperationException("Owned fixture data missing");
                foreach(IDictionary weapons in teams.Values)
                    foreach(var weapon in weapons.Values)
                    {
                        var count=checked((int)counterProperty!.GetValue(weapon)!+1);
                        counterProperty!.SetValue(weapon,count);maximumCounter=Math.Max(maximumCounter,count);
                    }
                var info=Activator.CreateInstance(infoType!)!;
                infoType!.GetProperty("Slot")!.SetValue(info,bot.Slot);
                infoType.GetProperty("SteamId")!.SetValue(info,"0");
                var start=Stopwatch.GetTimestamp();
                var previous=Activity.Current;
                var activity=new Activity("Skins3d.Counter").SetIdFormat(ActivityIdFormat.Hierarchical);
                activity.SetCustomProperty("Skins3d.CounterOwned",true);activity.Start();
                Task task;
                try{task=(Task)save!.Invoke(sync,new object?[]{info,null})!;}
                catch{activity.Stop();throw;}
                finally{Activity.Current=previous;}
                pending++;started++;Sample("counter_dispatch_ms",start);
                _=task.ContinueWith(result=>{activity.Stop();Server.NextFrame(()=>
                {
                    if(expected!=epoch)return;
                    pending--;completed++;
                    if(!result.IsCompletedSuccessfully){failures++;error="Counter task failed";Stop();return;}
                    Sample("counter_save_to_game_thread_ms",start);
                });},TaskScheduler.Default);
            }
        }
        catch(Exception ex){failures++;error=ex.GetBaseException().Message;Stop();}
    }
    private string Status()=>JsonSerializer.Serialize(new{active,recording,pending,failures,sqlFailures,error,started,completed,maximumCounter,version=plugin?.ModuleVersion});
    public override void Load(bool hotReload)
    {
        sqlListener=new ActivityListener
        {
            ShouldListenTo=source=>source.Name=="MySqlConnector",
            Sample=(ref ActivityCreationOptions<ActivityContext> options)=>Owns(Activity.Current)?ActivitySamplingResult.AllData:ActivitySamplingResult.None,
            SampleUsingParentId=(ref ActivityCreationOptions<string> options)=>Owns(Activity.Current)?ActivitySamplingResult.AllData:ActivitySamplingResult.None,
            ActivityStopped=activity=>{if(Owns(activity.Parent)&&activity.Status==ActivityStatusCode.Error)Interlocked.Increment(ref sqlFailures);}
        };
        ActivitySource.AddActivityListener(sqlListener);
        AddCommand("css_s3_counter_start","Console-only isolated counter workload",(caller,command)=>
        {
            if(caller!=null)return;
            try
            {
                if(pending!=0)throw new InvalidOperationException("Prior writes must drain");
                Stop();Bots();
                plugin=AppDomain.CurrentDomain.GetAssemblies().Where(a=>a.GetName().Name=="WeaponPaints")
                    .Select(a=>a.GetType("WeaponPaints.WeaponPaints")?.GetProperty("Instance",Flags)?.GetValue(null))
                    .OfType<BasePlugin>().Last(p=>p.ModuleVersion==command.GetArg(1));
                var type=plugin.GetType();var config=type.GetProperty("Config")!.GetValue(plugin)!;
                if((string?)config.GetType().GetProperty("DatabaseName")!.GetValue(config)!="skins3d_native"
                    ||(string?)config.GetType().GetProperty("DatabaseHost")!.GetValue(config)!="127.0.0.1"
                    ||(int)config.GetType().GetProperty("DatabasePort")!.GetValue(config)! !=43379)
                    throw new InvalidOperationException("Disposable QA database required");
                sync=type.GetField("WeaponSync",Flags)!.GetValue(null)!;
                save=sync.GetType().GetMethod("SyncStatTrakToDatabase",Flags)!;
                infoType=type.Assembly.GetType("WeaponPaints.PlayerInfo")!;
                counterProperty=type.Assembly.GetType("WeaponPaints.WeaponInfo")!.GetProperty("StatTrakCount")!;
                allWeapons=(IDictionary)type.GetField("GPlayerWeaponsInfo",Flags)!.GetValue(null)!;
                epoch++;sqlFailures=failures=started=completed=maximumCounter=0;error="";active=true;
                timer=AddTimer(1,Cycle,TimerFlags.REPEAT|TimerFlags.STOP_ON_MAPCHANGE);
            }
            catch(Exception ex){failures++;error=ex.GetBaseException().Message;Stop();}
            command.ReplyToCommand("COUNTER "+Status());
        });
        AddCommand("css_s3_counter_record","Bound samples to thirty seconds",(caller,command)=>
        {
            if(caller!=null)return;
            samples.Clear();recordStart=Stopwatch.GetTimestamp();recordEnd=recordStart+30*Stopwatch.Frequency;recording=true;
            command.ReplyToCommand("COUNTER "+Status());
        });
        AddCommand("css_s3_counter_status","Owned workload state",(caller,command)=>{if(caller==null)command.ReplyToCommand("COUNTER "+Status());});
        AddCommand("css_s3_counter_stop","Stop dispatch; wait for pending=0 before unloading",(caller,command)=>{if(caller==null){Stop();command.ReplyToCommand("COUNTER "+Status());}});
        AddCommand("css_s3_counter_export","Export bounded durations, no SQL or credentials",(caller,command)=>
        {
            if(caller!=null)return;recording=false;
            var directory=Path.GetFullPath(Path.Combine(ModuleDirectory,"../../logs/Skins3dCounterProbe"));Directory.CreateDirectory(directory);
            var file=Path.Combine(directory,DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff")+".json");
            File.WriteAllText(file,JsonSerializer.Serialize(new{version=plugin?.ModuleVersion,probe=ModuleVersion,recordingSeconds=Math.Min(30,Stopwatch.GetElapsedTime(recordStart).TotalSeconds),failures,sqlFailures,error,started,completed,maximumCounter,samples}));
            command.ReplyToCommand("COUNTERFILE "+file);
        });
        RegisterListener<Listeners.OnMapEnd>(()=>{epoch++;Stop();});
    }
    public override void Unload(bool hotReload){epoch++;Stop();sqlListener?.Dispose();}
}
