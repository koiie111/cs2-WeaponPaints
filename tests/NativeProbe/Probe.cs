using System.Collections;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Memory;
using System.Runtime.InteropServices;
using CounterStrikeSharp.API.Modules.Memory.DynamicFunctions;
using CounterStrikeSharp.API.Modules.Utils;

public sealed class Probe : BasePlugin
{
    public override string ModuleName => "Skins3d isolated acceptance probe";
    public override string ModuleVersion => "2.7.0";
    private static object[] Inventory(CCSPlayerPawn pawn)=>pawn.WeaponServices?.MyWeapons.Select(h=>{
        var weapon=h.Value;return (object)new{handle=h.Raw,valid=weapon?.IsValid,owner=weapon?.IsValid==true?weapon.OwnerEntity.Raw:0,
            classname=weapon?.IsValid==true?weapon.DesignerName:null,definition=weapon?.IsValid==true?weapon.AttributeManager.Item.ItemDefinitionIndex:0};}).ToArray()??[];
    private bool giveTrace;
    private readonly List<object> giveResults=new();
    private HookResult TraceGive(DynamicHook hook) {
        if(giveResults.Count>=96)return HookResult.Continue;
        try {
            var services=hook.GetParam<CCSPlayer_ItemServices>(0);var weapon=hook.GetReturn<CBasePlayerWeapon>();
            if(services?.Pawn.Value is not {IsValid:true} pawn||pawn.Controller.Value?.As<CCSPlayerController>() is not {IsBot:true} bot)return HookResult.Continue;
            giveResults.Add(new{slot=bot.Slot,pawn=pawn.EntityHandle.Raw,valid=weapon?.IsValid,entity=weapon?.EntityHandle.Raw,
                owner=weapon?.IsValid==true?weapon.OwnerEntity.Raw:0,classname=weapon?.IsValid==true?weapon.DesignerName:null,
                definition=weapon?.IsValid==true?weapon.AttributeManager.Item.ItemDefinitionIndex:0,
                acquisition=weapon?.IsValid==true?services.CanAcquire(weapon.AttributeManager.Item,AcquireMethod.PickUp).ToString():null,inventory=Inventory(pawn.As<CCSPlayerPawn>())});
        } catch { giveResults.Add(new{readError=true}); }
        return HookResult.Continue;
    }
    private void StopGiveTrace(){if(giveTrace){VirtualFunctions.GiveNamedItemFunc.Unhook(TraceGive,HookMode.Post);giveTrace=false;}}
    private const BindingFlags Flags = BindingFlags.Static|BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
    private BasePlugin? weaponPaints;
    private WeaponPaints.WeaponPaints? candidateRefresh;
    private Type? type;
    private bool active, recording, loading, stopped, refreshing;
    private long thinkStarted, previousTick;
    private int cycles, failures, loads, applied, nextCycle;
    private string error = "", mode = "apply";
    private JsonElement fixture;
    private Dictionary<string,int> attributeIds = new();
    private readonly Dictionary<string,List<double>> samples = new();
    private readonly object sampleLock = new();
    private long recordStarted, recordCutoff;
    private ActivityListener? sqlListener;
    [StructLayout(LayoutKind.Sequential)] private struct Timespec { public long Seconds,Nanoseconds; }
    [DllImport("libc.so.6",SetLastError=true)] private static extern int clock_gettime(int id,out Timespec value);
    private static double ThreadCpuMilliseconds(){if(clock_gettime(3,out var value)!=0)throw new Exception("Thread CPU clock unavailable");return value.Seconds*1000.0+value.Nanoseconds/1000000.0;}
    private double previousCpu;
    private int recordingThread;
    private sealed class LoadTrace(long started) { internal readonly long Started=started; internal long FirstOpen; }
    private static LoadTrace? Trace(Activity? activity)
    {
        while(activity!=null){if(activity.GetCustomProperty("Skins3d.Load") is LoadTrace trace)return trace;activity=activity.Parent;}
        return null;
    }
    private readonly HashSet<int> ownedSlots = new();
    private void Sample(string name, double value, long started=0)
    {
        lock(sampleLock) {
            if (!recording||Stopwatch.GetTimestamp()>recordCutoff||started!=0&&started<recordStarted) return;
            if (!samples.TryGetValue(name,out var values)) samples[name]=values=new();
            if(values.Count<100000) values.Add(value);
        }
    }
    private void Elapsed(string name,long start)=>Sample(name,Stopwatch.GetElapsedTime(start).TotalMilliseconds,start);
    private void Safe(Action action)
    {
        try { if (Utilities.GetPlayers().Any(p=>p.IsValid&&!p.IsBot&&!p.IsHLTV)) throw new Exception("Human joined; fixture stopped"); action(); }
        catch(Exception ex) { failures++;var cause=ex.GetBaseException();error=cause.Message+" @ "+cause.StackTrace;active=false; }
    }
    public override void Load(bool hotReload)
    {
        _=ThreadCpuMilliseconds(); // Resolve/JIT the public Linux clock outside recording.
        sqlListener=new ActivityListener {
            ShouldListenTo=source=>source.Name=="MySqlConnector",
            Sample=(ref ActivityCreationOptions<ActivityContext> options)=>Trace(Activity.Current)!=null?ActivitySamplingResult.AllData:ActivitySamplingResult.None,
            SampleUsingParentId=(ref ActivityCreationOptions<string> options)=>Trace(Activity.Current)!=null?ActivitySamplingResult.AllData:ActivitySamplingResult.None,
            ActivityStarted=activity=>{
                var trace=Trace(activity.Parent);if(trace==null)return;
                var now=Stopwatch.GetTimestamp();activity.SetCustomProperty("Skins3d.SqlStart",now);
                if(activity.OperationName=="Open"&&Interlocked.CompareExchange(ref trace.FirstOpen,now,0)==0)Elapsed("queue_before_connection_ms",trace.Started);
            },
            ActivityStopped=activity=>{
                if(Trace(activity.Parent)==null||activity.GetCustomProperty("Skins3d.SqlStart") is not long start||start<recordStarted)return;
                // Export only durations. Never serialize SQL, connection strings or tags.
                var schema=activity.GetTagItem("db.namespace")??activity.GetTagItem("db.name");
                if(schema is string name&&name!="skins3d_native")return;
                Sample("sql_"+activity.OperationName.ToLowerInvariant()+"_ms",activity.Duration.TotalMilliseconds,start);
            }
        };
        ActivitySource.AddActivityListener(sqlListener);
        fixture=JsonDocument.Parse(File.ReadAllText(Path.Combine(ModuleDirectory,"composition-roundtrip.json"))).RootElement.GetProperty("cases")[0].Clone();
        attributeIds=JsonSerializer.Deserialize<Dictionary<string,int>>(File.ReadAllText(Path.Combine(ModuleDirectory,"attribute-ids.json")))!;
        RegisterListener<Listeners.OnServerPreEntityThink>(()=>{if(recording)thinkStarted=Stopwatch.GetTimestamp();});
        RegisterListener<Listeners.OnServerPostEntityThink>(()=>{if(recording&&thinkStarted!=0)Sample("entity_think_span_ms",Stopwatch.GetElapsedTime(thinkStarted).TotalMilliseconds);thinkStarted=0;});
        RegisterListener<Listeners.OnTick>(()=>{
            if(!recording)return;
            var cpu=ThreadCpuMilliseconds();var thread=Environment.CurrentManagedThreadId;
            if(recordingThread!=0&&recordingThread!=thread){active=recording=false;failures++;error="Tick callbacks changed threads";return;}
            recordingThread=thread;if(previousCpu!=0)Sample("server_main_thread_cycle_cpu_ms",cpu-previousCpu);previousCpu=cpu;
            var now=Stopwatch.GetTimestamp();if(previousTick!=0)Sample("tick_interval_wall_ms",Stopwatch.GetElapsedTime(previousTick,now).TotalMilliseconds);previousTick=now;
        });
        AddCommand("css_s3_prepare","Console-only, owned bot fixture",(caller,command)=>{
            if(caller!=null)return;
            Safe(()=>{
                var version=command.GetArg(1);
                weaponPaints=AppDomain.CurrentDomain.GetAssemblies().Where(a=>a.GetName().Name=="WeaponPaints")
                    .Select(a=>a.GetType("WeaponPaints.WeaponPaints")?.GetProperty("Instance",Flags)?.GetValue(null)).OfType<BasePlugin>().Last(p=>p.ModuleVersion==version);
                type=weaponPaints.GetType();active=true;stopped=false;loading=true;
                foreach(var player in Bots()){
                    ownedSlots.Add(player.Slot);
                    // Keep owned fake clients away from map weapon pickups while a
                    // native refresh briefly leaves the pistol slot empty.
                    var pawn=player.PlayerPawn.Value!;var origin=pawn.AbsOrigin!;
                    pawn.MoveType=pawn.ActualMoveType=MoveType_t.MOVETYPE_NOCLIP;pawn.GravityScale=0;
                    pawn.Teleport(new Vector(origin.X,origin.Y,origin.Z+256),new QAngle(0,0,0),Vector.Zero);
                    player.RemoveWeapons();
                }
                if(ownedSlots.Count!=8)throw new Exception("Exactly eight live bots required");
                AddTimer(.2f,()=>{if(active&&!stopped)LoadFromDatabase();},TimerFlags.STOP_ON_MAPCHANGE);
            });
            command.ReplyToCommand("S3 "+Status());
        });
        AddCommand("css_s3_mode","apply, refresh or sql",(caller,command)=>{if(caller==null){mode=command.GetArg(1);command.ReplyToCommand("S3 mode="+mode);}});
        AddCommand("css_s3_record","Reset bounded 30-second diagnostic samples",(caller,command)=>{if(caller==null){lock(sampleLock){samples.Clear();previousTick=thinkStarted=0;previousCpu=0;recordingThread=0;recordStarted=Stopwatch.GetTimestamp();recordCutoff=recordStarted+30*Stopwatch.Frequency;recording=true;}command.ReplyToCommand("S3 recording");}});
        AddCommand("css_s3_status","State only",(caller,command)=>{if(caller==null)command.ReplyToCommand("S3 "+Status());});
        AddCommand("css_s3_trace_start","Read-only native acquisition trace outside measurements",(caller,command)=>{
            if(caller!=null)return;Safe(()=>{StopGiveTrace();giveResults.Clear();VirtualFunctions.GiveNamedItemFunc.Hook(TraceGive,HookMode.Post);giveTrace=true;});command.ReplyToCommand("S3 trace started");
        });
        AddCommand("css_s3_trace_export","Stop acquisition trace",(caller,command)=>{
            if(caller!=null)return;StopGiveTrace();var directory=Path.GetFullPath(Path.Combine(ModuleDirectory,"../../logs/Skins3dProbe"));Directory.CreateDirectory(directory);
            var file=Path.Combine(directory,DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff")+"-give.json");File.WriteAllText(file,JsonSerializer.Serialize(giveResults));command.ReplyToCommand("S3FILE "+file);
        });
        AddCommand("css_s3_direct_give","Observe a native direct give after failed refresh",(caller,command)=>{
            if(caller!=null)return;Safe(()=>{
                active=false;var bot=Bots().First();var raw=bot.GiveNamedItem("weapon_fiveseven");
                var handle=raw==0?0:new CBasePlayerWeapon(raw).EntityHandle.Raw;
                void Snapshot(string phase){var weapon=handle==0?null:new CHandle<CBasePlayerWeapon>(handle).Value;
                    giveResults.Add(new{direct=phase,slot=bot.Slot,pawn=bot.PlayerPawn.Raw,entity=handle,valid=weapon?.IsValid,owner=weapon?.IsValid==true?weapon.OwnerEntity.Raw:0,
                        inventory=bot.PlayerPawn.Value is {IsValid:true} pawn?Inventory(pawn):[]});}
                Snapshot("returned");Server.NextFrame(()=>Snapshot("next-frame"));AddTimer(.5f,()=>Snapshot("half-second"),TimerFlags.STOP_ON_MAPCHANGE);
            });command.ReplyToCommand("S3 direct give observed");
        });
        AddCommand("css_s3_check","Verify native decoration attributes",(caller,command)=>{
            if(caller!=null)return;Safe(()=>{foreach(var player in Bots())Verify(player,true);});command.ReplyToCommand("S3 "+Status());
        });
        AddCommand("css_s3_single","Single bot in-memory sticker transition",(caller,command)=>{
            if(caller!=null)return;
            Safe(()=>{
                var bot=Bots().Single();
                weaponPaints=AppDomain.CurrentDomain.GetAssemblies().Where(a=>a.GetName().Name=="WeaponPaints")
                    .Select(a=>a.GetType("WeaponPaints.WeaponPaints")?.GetProperty("Instance",Flags)?.GetValue(null)).OfType<BasePlugin>()
                    .Last(p=>p.GetType().GetField("_giveNamedItemHooked",Flags)?.GetValue(p) is true);
                type=weaponPaints.GetType(); var root=(IDictionary)type.GetField("GPlayerWeaponsInfo",Flags)!.GetValue(null)!;
                candidateRefresh=new WeaponPaints.WeaponPaints();candidateRefresh.InitializeForProbe(weaponPaints,this,bot);
                if(root.Contains(bot.Slot))throw new Exception("Bot slot already has player data");
                var teamsType=root.GetType().GetGenericArguments()[1]; var teams=(IDictionary)Activator.CreateInstance(teamsType)!;
                var items=(IDictionary)Activator.CreateInstance(teamsType.GetGenericArguments()[1])!;
                var infoType=type.Assembly.GetType("WeaponPaints.WeaponInfo")!;var info=Activator.CreateInstance(infoType)!;
                infoType.GetProperty("Paint")!.SetValue(info,44);infoType.GetProperty("Wear")!.SetValue(info,.01f);
                infoType.GetProperty("CompositionVersion")!.SetValue(info,1);
                infoType.GetProperty("StatTrak")!.SetValue(info,true);
                infoType.GetProperty("StatTrakCount")!.SetValue(info,17);
                infoType.GetProperty("Nametag")!.SetValue(info,"wp-probe");
                var charmType=type.Assembly.GetType("WeaponPaints.KeyChainInfo")!;
                var charm=Activator.CreateInstance(charmType)!;
                charmType.GetProperty("Id")!.SetValue(charm,37u);charmType.GetProperty("Sticker")!.SetValue(charm,4551u);
                charmType.GetProperty("Seed")!.SetValue(charm,100000u);charmType.GetProperty("OffsetX")!.SetValue(charm,1.25f);
                charmType.GetProperty("OffsetY")!.SetValue(charm,.25f);charmType.GetProperty("OffsetZ")!.SetValue(charm,5.5f);
                infoType.GetProperty("KeyChain")!.SetValue(info,charm);
                var stickerType=type.Assembly.GetType("WeaponPaints.StickerInfo")!;
                var stickers=(IList)infoType.GetProperty("Stickers")!.GetValue(info)!;
                for(var index=0;index<5;index++){var sticker=Activator.CreateInstance(stickerType)!;stickerType.GetProperty("Id")!.SetValue(sticker,index==0?23u:0u);stickerType.GetProperty("Scale")!.SetValue(sticker,1f);stickers.Add(sticker);}
                items[7]=info;teams[bot.Team]=items;root[bot.Slot]=teams;ownedSlots.Add(bot.Slot);
                bot.RemoveWeapons(); AddTimer(.15f,()=>Safe(()=>{
                    var raw=bot.GiveNamedItem("weapon_ak47");if(raw==0)throw new Exception("AK give failed");
                    var gun=new CBasePlayerWeapon(raw);if(!gun.IsValid || gun.OwnerEntity.Raw!=bot.PlayerPawn.Raw)throw new Exception("AK not owned");
                    type.GetMethod("GivePlayerWeaponSkin",Flags)!.Invoke(weaponPaints,new object[]{bot,gun});
                    var before=BitConverter.SingleToUInt32Bits(ReadAttributes(gun).GetValueOrDefault(attributeIds["sticker slot 0 id"]));
                    if(before!=23)throw new Exception("Initial sticker mismatch: "+before);
                    stickerType.GetProperty("Id")!.SetValue(stickers[1],16u);stickerType.GetProperty("Schema")!.SetValue(stickers[1],1u);
                    stickerType.GetProperty("OffsetX")!.SetValue(stickers[1],-.17183096f);
                    stickerType.GetProperty("OffsetY")!.SetValue(stickers[1],.002451175f);
                    charmType.GetProperty("Sticker")!.SetValue(charm,4552u);charmType.GetProperty("Seed")!.SetValue(charm,99999u);
                    infoType.GetProperty("StatTrakCount")!.SetValue(info,18);infoType.GetProperty("Nametag")!.SetValue(info,"wp-check");
                    var old=gun.EntityHandle.Raw;bool? firstCompletion=null,secondCompletion=null;
                    candidateRefresh.RefreshForProbe(bot,success=>firstCompletion=success);
                    // Arrive after the first pass has begun retiring the gun, so
                    // the second request must survive the in-flight operation.
                    Server.NextFrame(()=>Safe(()=>candidateRefresh!.RefreshForProbe(bot,success=>secondCompletion=success)));
                    AddTimer(.8f,()=>Safe(()=>{
                        if(firstCompletion!=true || secondCompletion!=true)throw new Exception("Candidate refresh did not report success for both requests");
                        var current=bot.PlayerPawn.Value!.WeaponServices!.MyWeapons.Select(h=>h.Value).Single(w=>w is {IsValid:true} && w.AttributeManager.Item.ItemDefinitionIndex==7)!;
                        foreach(var field in new[]{"m_AttributeManager","m_nFallbackPaintKit","m_nFallbackSeed","m_flFallbackWear"}) {
                            if(Schema.GetSchemaOffset(nameof(CEconEntity),field)<=0)throw new Exception("Missing network schema "+field);
                            if(!Schema.IsSchemaFieldNetworked(nameof(CEconEntity),field))throw new Exception("Non-networked weapon field "+field);
                        }
                        WeaponPaints.WeaponPaints.NotifyWeaponAppearanceChanged(current);
                        foreach(var field in new[]{"m_EconGloves","m_nEconGlovesChanged"})
                            if(Schema.GetSchemaOffset(nameof(CCSPlayerPawn),field)<=0 || !Schema.IsSchemaFieldNetworked(nameof(CCSPlayerPawn),field))
                                throw new Exception("Missing networked pawn field "+field);
                        var testPawn=bot.PlayerPawn.Value!;var gloves=testPawn.EconGloves;var changed=testPawn.EconGlovesChanged;
                        gloves.ItemDefinitionIndex=5030;gloves.Initialized=true;
                        WeaponPaints.GloveNetwork.Clear(testPawn);
                        if(gloves.ItemDefinitionIndex!=0 || gloves.Initialized || testPawn.EconGlovesChanged!=unchecked((byte)(changed+1)))
                            throw new Exception("Glove reset failed");
                        var values=ReadAttributes(current);var first=BitConverter.SingleToUInt32Bits(values.GetValueOrDefault(attributeIds["sticker slot 0 id"]));
                        var second=BitConverter.SingleToUInt32Bits(values.GetValueOrDefault(attributeIds["sticker slot 1 id"]));
                        error=$"SINGLE old={old} new={current.EntityHandle.Raw} first={first} second={second}";
                        if(current.EntityHandle.Raw==old || first!=23 || second!=16 ||
                           BitConverter.SingleToUInt32Bits(values.GetValueOrDefault(attributeIds["sticker slot 1 schema"]))!=1 ||
                           Math.Abs(values.GetValueOrDefault(attributeIds["sticker slot 1 offset x"])-(-.17183096f))>.000001f ||
                           Math.Abs(values.GetValueOrDefault(attributeIds["sticker slot 1 offset y"])-.002451175f)>.000001f)
                            throw new Exception(error+" incorrect sticker attributes");
                        uint UInt(string name)=>BitConverter.SingleToUInt32Bits(values.GetValueOrDefault(attributeIds[name]));
                        if(UInt("keychain slot 0 id")!=37 || UInt("keychain slot 0 sticker")!=4552 || UInt("keychain slot 0 seed")!=99999 ||
                           Math.Abs(values.GetValueOrDefault(attributeIds["keychain slot 0 offset x"])-1.25f)>.000001f ||
                           Math.Abs(values.GetValueOrDefault(attributeIds["keychain slot 0 offset y"])-.25f)>.000001f ||
                           Math.Abs(values.GetValueOrDefault(attributeIds["keychain slot 0 offset z"])-5.5f)>.000001f ||
                           UInt("kill eater")!=18 || current.AttributeManager.Item.EntityQuality!=9 || current.AttributeManager.Item.CustomName!="wp-check")
                            throw new Exception("Slab, StatTrak, name or coordinates did not update");
                        var previous=current.EntityHandle.Raw;
                        stickerType.GetProperty("Id")!.SetValue(stickers[0],0u);stickerType.GetProperty("Id")!.SetValue(stickers[1],0u);
                        infoType.GetProperty("KeyChain")!.SetValue(info,null);infoType.GetProperty("StatTrak")!.SetValue(info,false);
                        infoType.GetProperty("Nametag")!.SetValue(info,"");
                        bool? clearCompletion=null;
                        candidateRefresh.RefreshForProbe(bot,success=>clearCompletion=success);
                        AddTimer(.8f,()=>Safe(()=>{
                            if(clearCompletion!=true)throw new Exception("Candidate clear refresh did not report success");
                            var cleared=bot.PlayerPawn.Value!.WeaponServices!.MyWeapons.Select(h=>h.Value).Single(w=>w is {IsValid:true} && w.AttributeManager.Item.ItemDefinitionIndex==7)!;
                            var attrs=ReadAttributes(cleared);
                            if(cleared.EntityHandle.Raw==previous || BitConverter.SingleToUInt32Bits(attrs.GetValueOrDefault(attributeIds["sticker slot 0 id"]))!=0 ||
                               BitConverter.SingleToUInt32Bits(attrs.GetValueOrDefault(attributeIds["sticker slot 1 id"]))!=0 ||
                               attrs.ContainsKey(attributeIds["keychain slot 0 id"]) || attrs.ContainsKey(attributeIds["keychain slot 0 sticker"]) ||
                               attrs.ContainsKey(attributeIds["kill eater"]) || cleared.AttributeManager.Item.EntityQuality==9 ||
                               !string.IsNullOrEmpty(cleared.AttributeManager.Item.CustomName))
                                throw new Exception("Removed stickers, slab, StatTrak or name survived refresh");
                            var clearedHandle=cleared.EntityHandle.Raw;bool? cancelled=null;var callbacks=0;
                            candidateRefresh.RefreshForProbe(bot,success=>{cancelled=success;callbacks++;});
                            candidateRefresh.StopForProbe();
                            Server.NextFrame(()=>Safe(()=>{
                                var retained=bot.PlayerPawn.Value!.WeaponServices!.MyWeapons.Select(h=>h.Value).Single(w=>w is {IsValid:true} && w.AttributeManager.Item.ItemDefinitionIndex==7)!;
                                if(cancelled!=false || callbacks!=1 || retained.EntityHandle.Raw!=clearedHandle)
                                    throw new Exception("Cancelled candidate refresh ran or completed incorrectly");
                                var rawKnife=bot.GiveNamedItem("weapon_knife");
                                if(rawKnife==0)throw new Exception("Knife fixture give failed");
                                var knife=new CBasePlayerWeapon(rawKnife);var knifeHandle=knife.EntityHandle.Raw;
                                if(!knife.IsValid || knife.OwnerEntity.Raw!=bot.PlayerPawn.Raw)throw new Exception("Knife fixture not acquired");
                                candidateRefresh.SetKnifeEnabledForProbe(false);
                                bool? knifeCompletion=null;
                                candidateRefresh.RefreshForProbe(bot,success=>knifeCompletion=success);
                                AddTimer(.8f,()=>Safe(()=>{
                                    if(knifeCompletion!=true)throw new Exception("Refresh with disabled knives did not complete");
                                    var sameKnife=new CHandle<CBasePlayerWeapon>(knifeHandle).Value;
                                    if(sameKnife is not {IsValid:true} || sameKnife.OwnerEntity.Raw!=bot.PlayerPawn.Raw ||
                                       !bot.PlayerPawn.Value!.WeaponServices!.MyWeapons.Any(h=>h.Raw==knifeHandle))
                                        throw new Exception("Knife was replaced while knife customization was disabled");
                                    error+=" callbacks=PASS clear=PASS slab-stattrak=PASS cancel=PASS knife-disabled=PASS PASS";
                                    candidateRefresh=null;root.Remove(bot.Slot);ownedSlots.Remove(bot.Slot);
                                }),TimerFlags.STOP_ON_MAPCHANGE);
                            }));
                        }),TimerFlags.STOP_ON_MAPCHANGE);
                    }),TimerFlags.STOP_ON_MAPCHANGE);
                }),TimerFlags.STOP_ON_MAPCHANGE);
            });command.ReplyToCommand("S3 "+Status());
        });
        AddCommand("css_s3_sticker_refresh","Verify changed sticker after native replacement",(caller,command)=>{
            if(caller!=null)return;
            Safe(()=>{
                if(type==null || weaponPaints==null || loading || refreshing) throw new Exception("Fixture not ready");
                var players=Bots(); if(players.Length!=8) throw new Exception("Exactly eight live bots required");
                refreshing=true; var pending=players.Length;
                foreach(var player in players) {
                    var root=(IDictionary)type.GetField("GPlayerWeaponsInfo",Flags)!.GetValue(null)!;
                    var teams=(IDictionary)root[player.Slot]!;
                    var weapons=(IDictionary)teams[player.Team]!;
                    var info=weapons[3] ?? throw new Exception("Fixture weapon preference missing");
                    var stickers=(IList)info.GetType().GetProperty("Stickers")!.GetValue(info)!;
                    var sticker=stickers[0]!; var property=sticker.GetType().GetProperty("Id")!;
                    var previous=(uint)property.GetValue(sticker)!; var next=previous==1u?2u:1u;
                    var old=Gun(player).EntityHandle.Raw;
                    if(BitConverter.SingleToUInt32Bits(ReadAttributes(Gun(player)).GetValueOrDefault(attributeIds["sticker slot 0 id"]))!=previous)
                        throw new Exception("Initial sticker does not match snapshot");
                    property.SetValue(sticker,next);
                    type.GetMethod("RefreshWeapons",Flags)!.Invoke(weaponPaints,new object?[]{player,(int?)3});
                    var start=Stopwatch.GetTimestamp();
                    void Poll() { Safe(()=>{
                        var gun=player.PlayerPawn.Value?.WeaponServices?.MyWeapons.Select(h=>h.Value)
                            .FirstOrDefault(w=>w is {IsValid:true} && w.AttributeManager.Item.ItemDefinitionIndex==3);
                        if(gun==null || gun.EntityHandle.Raw==old) {
                            if(Stopwatch.GetElapsedTime(start).TotalSeconds>2) throw new Exception("Sticker refresh did not replace weapon");
                            Server.NextFrame(Poll); return;
                        }
                        Server.NextFrame(()=>Safe(()=>{
                            var current=Gun(player);
                            var actual=BitConverter.SingleToUInt32Bits(ReadAttributes(current).GetValueOrDefault(attributeIds["sticker slot 0 id"]));
                            if(actual!=next) throw new Exception($"Sticker refresh remained {actual}; expected {next}");
                            if(--pending==0)refreshing=false;
                        }));
                    }); }
                    Server.NextFrame(Poll);
                }
            });
            command.ReplyToCommand("S3 "+Status());
        });
        AddCommand("css_s3_check_default","Verify decoration on default paint",(caller,command)=>{
            if(caller!=null)return;Safe(()=>{foreach(var player in Bots()){Verify(player,true);if(Gun(player).FallbackPaintKit!=0)throw new Exception("Default paint not applied");}});command.ReplyToCommand("S3 "+Status());
        });
        AddCommand("css_s3_check_clear","Verify removal of all native decor",(caller,command)=>{
            if(caller!=null)return;Safe(()=>{foreach(var player in Bots()){
                var gun=Gun(player);var values=ReadAttributes(gun);
                foreach(var name in Enumerable.Range(0,5).Select(slot=>$"sticker slot {slot} id").Concat(new[]{"keychain slot 0 id","keychain slot 0 sticker"}))
                    if(BitConverter.SingleToUInt32Bits(values.GetValueOrDefault(attributeIds[name]))!=0)throw new Exception("Old decor survived removal: "+name);
                if(gun.FallbackPaintKit!=0)throw new Exception("Default paint not cleared");Verify(player,false);
                if(gun.AttributeManager.Item.CustomName!=""||gun.AttributeManager.Item.EntityQuality!=0)throw new Exception("Name or counter survived removal");
            }});command.ReplyToCommand("S3 "+Status());
        });
        AddCommand("css_s3_export","Stop recording and export samples outside measurement",(caller,command)=>{
            if(caller!=null)return;lock(sampleLock)recording=false;
            var directory=Path.GetFullPath(Path.Combine(ModuleDirectory,"../../logs/Skins3dProbe"));Directory.CreateDirectory(directory);
            var file=Path.Combine(directory,DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff")+".json");
            File.WriteAllText(file,JsonSerializer.Serialize(new{version=weaponPaints?.ModuleVersion,probe=ModuleVersion,recordingThread,recordingSeconds=Math.Min(30,Stopwatch.GetElapsedTime(recordStarted).TotalSeconds),mode,cycles,failures,error,loads,applied,samples}));command.ReplyToCommand("S3FILE "+file);
        });
        AddCommand("css_s3_stop","Stop owned fixture",(caller,command)=>{if(caller==null){Stop();command.ReplyToCommand("S3 stopped");}});
        RegisterListener<Listeners.OnTick>(()=>{if(!active||stopped||loading||refreshing||Server.TickCount<nextCycle)return;nextCycle=Server.TickCount+64;Safe(()=>{
            cycles++;
            if(mode=="sql"){if(!loading)LoadFromDatabase();return;}
            var players=Bots();var pendingRefresh=players.Length;
            if(mode=="refresh")refreshing=true;
            foreach(var player in players) {
                var start=Stopwatch.GetTimestamp();
                if(mode=="refresh") {
                    var old=Gun(player).EntityHandle.Raw;
                    type!.GetMethod("RefreshWeapons",Flags)!.Invoke(weaponPaints,new object?[]{player,(int?)3});
                    void PollRefresh() { Safe(()=>{
                        if(!active||stopped||!player.IsValid||!player.IsBot||!player.PawnIsAlive)return;
                        var gun=player.PlayerPawn.Value!.WeaponServices!.MyWeapons.Select(h=>h.Value).FirstOrDefault(w=>w is {IsValid:true}&&w.AttributeManager.Item.ItemDefinitionIndex==3);
                        if(gun==null||gun.EntityHandle.Raw==old){if(Stopwatch.GetElapsedTime(start).TotalSeconds>2)throw new Exception("Refresh did not replace serial");Server.NextFrame(PollRefresh);return;}
                        Verify(player,false);Elapsed("refresh_to_native_ms",start);if(--pendingRefresh==0)refreshing=false;
                    }); } Server.NextFrame(PollRefresh);
                } else {
                    type!.GetMethod("GivePlayerWeaponSkin",Flags)!.Invoke(weaponPaints,new object[]{player,Gun(player)});applied++;
                    Elapsed("apply_immediate_ms",start);Verify(player,false);
                }
            }
        });});
    }
    private CCSPlayerController[] Bots()=>Utilities.GetPlayers().Where(p=>p.IsValid&&p.IsBot&&p.PawnIsAlive).OrderBy(p=>p.Slot).ToArray();
    private CBasePlayerWeapon Gun(CCSPlayerController player)=>player.PlayerPawn.Value!.WeaponServices!.MyWeapons.Select(h=>h.Value)
        .Single(w=>w is {IsValid:true}&&w.AttributeManager.Item.ItemDefinitionIndex==3)!;
    private void LoadFromDatabase()
    {
        if(type==null||weaponPaints==null)return;loading=true;
        var players=Bots();var remaining=players.Length;
        foreach(var player in players) {
            var start=Stopwatch.GetTimestamp();var info=Activator.CreateInstance(type.Assembly.GetType("WeaponPaints.PlayerInfo")!)!;
            info.GetType().GetProperty("Slot")!.SetValue(info,player.Slot);info.GetType().GetProperty("SteamId")!.SetValue(info,player.SteamID.ToString());
            var loadId=(long)type.GetMethod("NextPlayerDataLoadId",Flags)!.Invoke(null,null)!;
            var pending=(IDictionary)type.GetField("PlayerDataLoading",Flags)!.GetValue(null)!;pending[player.Slot]=loadId;
            var generation=(long)type.GetField("DataGeneration",Flags)!.GetValue(null)!;var sync=type.GetField("WeaponSync",Flags)!.GetValue(null)!;
            var prior=Activity.Current;
            // Hierarchical IDs deliberately avoid adding W3C query attributes to the
            // wire on newer connector versions. Only this owned load is sampled.
            var activity=new Activity("Skins3d.Load").SetIdFormat(ActivityIdFormat.Hierarchical);
            activity.SetCustomProperty("Skins3d.Load",new LoadTrace(start));activity.Start();
            Task<bool> task;
            try {task=(Task<bool>)sync.GetType().GetMethod("GetPlayerData",Flags)!.Invoke(sync,new[]{info,(object)loadId,generation})!;}
            finally {Activity.Current=prior;}
            task.ContinueWith(completed=>{activity.Stop();Server.NextFrame(()=>Safe(()=>{
                try {
                    if(stopped||!active||!player.IsValid||!player.IsBot)return;
                    if(!completed.IsCompletedSuccessfully||!completed.Result)throw new Exception("Production SQL load failed");
                    if(pending.Contains(player.Slot)&&Equals(pending[player.Slot],loadId))pending.Remove(player.Slot);
                    if(!player.PawnIsAlive)throw new Exception("Bot died during load");
                    var weapons=player.PlayerPawn.Value!.WeaponServices!.MyWeapons.Select(h=>h.Value).Where(w=>w is {IsValid:true}&&w.AttributeManager.Item.ItemDefinitionIndex==3).ToArray();
                    if(weapons.Length==0){player.RemoveWeapons();player.GiveNamedItem("weapon_fiveseven");}
                    type.GetMethod("GivePlayerWeaponSkin",Flags)!.Invoke(weaponPaints,new object[]{player,Gun(player)});
                    loads++;Elapsed("sql_load_to_apply_ms",start);
                } finally { if(--remaining==0)loading=false; }
            }));});
        }
    }
    private void Verify(CCSPlayerController player,bool allAttributes)
    {
        var gun=Gun(player);var values=ReadAttributes(gun);
        var expected=fixture.GetProperty("input");var wear=expected.GetProperty("wear").GetSingle();
        Sample("observed_weapon_wear",gun.FallbackWear);
        if(weaponPaints!.ModuleVersion.Contains("skins3d")&&Math.Abs(gun.FallbackWear-wear)>.000001f)throw new Exception("Weapon wear differs from saved value: "+gun.FallbackWear);
        if(!allAttributes)return;
        for(var slot=0;slot<5;slot++) {
            var sticker=expected.GetProperty("stickers")[slot];
            foreach(var (field,key,integer) in new[]{("id","id",true),("schema","schema",true),("offset x","x",false),("offset y","y",false),("wear","wear",false),("scale","scale",false),("rotation","rotation",false)}) {
                var value=values.GetValueOrDefault(attributeIds[$"sticker slot {slot} {field}"]);
                var wanted=sticker.GetProperty("id").GetUInt32()==0?0:sticker.GetProperty(key).GetSingle();
                if(integer?BitConverter.SingleToUInt32Bits(value)!=(uint)wanted:Math.Abs(value-wanted)>.000001f)throw new Exception($"Native sticker {slot}/{field} differs");
            }
        }
        var charm=expected.GetProperty("keychain");
        foreach(var(field,key,integer) in new[]{("id","id",true),("offset x","x",false),("offset y","y",false),("offset z","z",false),("seed","seed",true),("sticker","sticker",true)}) {
            var value=values.GetValueOrDefault(attributeIds["keychain slot 0 "+field]);var wanted=charm.GetProperty(key).GetSingle();
            if(integer?BitConverter.SingleToUInt32Bits(value)!=(uint)wanted:Math.Abs(value-wanted)>.000001f)throw new Exception("Native keychain "+field+" differs");
        }
    }
    private static Dictionary<int,float> ReadAttributes(CBasePlayerWeapon weapon)
    {
        var vector=weapon.AttributeManager.Item.NetworkedDynamicAttributes.Attributes;
        var count=NativeAPI.GetNetworkVectorSize(vector.Handle);
        var stride=Schema.GetClassSize("CEconItemAttribute");
        if(count<0||count>128||stride<16||stride>256)throw new Exception("Unexpected native attribute layout");
        var result=new Dictionary<int,float>();if(count==0)return result;
        // The CSS helper exposes the first element address; its generic enumerator
        // intentionally accepts only CHandle<T>. Use the live schema's struct stride.
        var first=NativeAPI.GetNetworkVectorElementAt(vector.Handle,0);
        if(first==0)throw new Exception("Null native attribute storage");
        for(var index=0;index<count;index++) {
            var attribute=new CEconItemAttribute(first+index*stride);
            result[attribute.AttributeDefinitionIndex]=attribute.Value;
        }
        return result;
    }
    private string Status()=>JsonSerializer.Serialize(new{active,loading,refreshing,mode,cycles,failures,error,loads,applied,slots=ownedSlots.Count,version=weaponPaints?.ModuleVersion});
    private void Stop(){
        active=recording=false;stopped=true;
        candidateRefresh?.StopForProbe();candidateRefresh=null;
        if(type?.GetField("GPlayerWeaponsInfo",Flags)?.GetValue(null) is IDictionary root)
            foreach(var slot in ownedSlots)
                if(Utilities.GetPlayerFromSlot(slot) is {IsValid:true,IsBot:true})root.Remove(slot);
        ownedSlots.Clear();
    }
    public override void Unload(bool hotReload){Stop();StopGiveTrace();sqlListener?.Dispose();}
}
