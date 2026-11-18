using System.Reflection;
using System.Runtime.Loader;

if (args.Length != 1) { Console.Error.WriteLine("Usage: OptionalApiProbe <absolute path to WeaponPaints.dll>"); return 2; }
var path=Path.GetFullPath(args[0]);
var context=new PluginProbe(Path.GetDirectoryName(path)!);
try
{
    var assembly=context.LoadFromAssemblyPath(path);
    var types=assembly.GetTypes();
    var pluginType=types.Single(t=>t.FullName=="WeaponPaints.WeaponPaints");
    var configType=assembly.GetType("WeaponPaints.WeaponPaintsConfig",true)!;
    var config=Activator.CreateInstance(configType) ?? throw new InvalidOperationException("Configuration constructor failed");
    var hud=config.GetType().GetProperty("PanoramaHud")!.GetValue(config)!;
    if ((bool)hud.GetType().GetProperty("Enabled")!.GetValue(hud)!) throw new InvalidOperationException("Expected default disabled HUD");
    bool blocked=false;try { context.LoadFromAssemblyName(new AssemblyName("CustomWeapons.Hud.Api")); } catch(FileNotFoundException) {blocked=true;}
    if(!blocked) throw new InvalidOperationException("API blocker ineffective");
    Console.WriteLine("PASS: real WP assembly/GetTypes and disabled configuration default with shared API resolution blocked; plugin constructor, CSS Load and native initialization not exercised");
    return 0;
}
catch(Exception e) { Console.Error.WriteLine("FAIL: "+e.GetBaseException().ToString()); return 1; }
finally { context.Unload(); }

sealed class PluginProbe(string directory) : AssemblyLoadContext(isCollectible:true)
{
    protected override Assembly? Load(AssemblyName name)
    {
        if (name.Name=="CustomWeapons.Hud.Api") throw new FileNotFoundException("Blocked optional API in smoke");
        var candidate=Path.Combine(directory,name.Name+".dll");
        return File.Exists(candidate)?LoadFromAssemblyPath(candidate):null;
    }
}
