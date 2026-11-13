using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Entities.Constants;
using WeaponPaints;
using WP = WeaponPaints.WeaponPaints;

int checks = 0;
void Check(bool condition, string message) { checks++; if (!condition) throw new Exception(message); }
var p = new CCSPlayerController { Slot = 3, Team = 2, InventoryServices = new() };
WP.Music[3] = new() { [2] = 12, [3] = 90 }; WP.Pins[3] = new() { [2] = 31, [3] = 82 };
void Apply() { WP.ApplyMusic(p); WP.ApplyPin(p); }
void Selected(ushort music, ushort pin, string name) {
    Check(p.MusicKitID == music && p.InventoryServices!.MusicID == music && (ushort)p.InventoryServices.Rank[5] == pin, name);
}
Apply(); Selected(12, 31, "team T selection");
Check(Utilities.Changes.SequenceEqual(new[] { "m_iMusicKitID", "m_pInventoryServices", "m_pInventoryServices" }), "music and pin notify network");
WP.Music[3][2] = 13; WP.Pins[3][2] = 32; Apply(); Selected(13, 32, "selection A to B");
WP.Music[3][2] = 0; WP.Pins[3][2] = 0; Apply(); Selected(0, 0, "explicit zero resets controller and inventory");
p.Team = 3; Apply(); Selected(90, 82, "other team retains separate selection");
WP.Music.Remove(3); WP.Pins.Remove(3); Apply(); Selected(0, 0, "deleted rows clear prior values");
WP.Music[3] = new() { [2] = 7 }; WP.Pins[3] = new() { [2] = 8 }; Apply(); Selected(0, 0, "missing current team clears without borrowing other team");
p.MusicKitID = 41; p.InventoryServices!.MusicID = 41; p.InventoryServices.Rank[5] = (MedalRank_t)42;
WP.Config.Additional.MusicEnabled = false; WP.Config.Additional.PinsEnabled = false; Utilities.Changes.Clear(); Apply(); Selected(41, 42, "disabled features leave existing values alone");
Check(Utilities.Changes.Count == 0, "disabled features do not notify");
WP.Config.Additional.MusicEnabled = true; WP.Config.Additional.PinsEnabled = true; p.IsBot = true; Utilities.Changes.Clear(); WP.ApplyMusic(p);
Check(p.MusicKitID == 41 && Utilities.Changes.Count == 0, "bots do not receive music updates");
p.IsBot = false; p.InventoryServices = null; Utilities.Changes.Clear(); Apply();
Check(p.MusicKitID == 41 && Utilities.Changes.Count == 0, "missing inventory does not mutate or notify");
Console.WriteLine($"PASS: {checks} linked production controller cosmetic checks");

namespace CounterStrikeSharp.API.Core {
 public class CCSPlayerController { public bool IsBot; public int Slot, Team; public ushort MusicKitID; public Inventory? InventoryServices; }
 public class Inventory { public ushort MusicID; public MedalRank_t[] Rank = new MedalRank_t[10]; }
}
namespace CounterStrikeSharp.API.Modules.Entities.Constants { public enum MedalRank_t : ushort { MEDAL_RANK_NONE = 0 } }
namespace CounterStrikeSharp.API { public static class Utilities { public static List<string> Changes = new(); public static void SetStateChanged(CCSPlayerController p, string cls, string field) => Changes.Add(field); } }
namespace WeaponPaints { public partial class WeaponPaints {
 public class Extra { public bool MusicEnabled = true, PinsEnabled = true; }
 public class Cfg { public Extra Additional = new(); }
 public static Cfg Config = new(); private static readonly Cfg _config = Config;
 public static Dictionary<int,Dictionary<int,ushort>> Music = new(), Pins = new();
 private static Dictionary<int,Dictionary<int,ushort>> GPlayersMusic => Music;
 private static Dictionary<int,Dictionary<int,ushort>> GPlayersPin => Pins;
 public static void ApplyMusic(CCSPlayerController p) => GivePlayerMusicKit(p);
 public static void ApplyPin(CCSPlayerController p) => GivePlayerPin(p);
} }
