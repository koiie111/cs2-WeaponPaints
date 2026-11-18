using Steamworks;
using System.Security.Cryptography;
using System.Text.Json;

// Authoring tool only. Never referenced by the server plugin.
if (args.Length == 0 || args[0] == "help") {
    Console.WriteLine("who | publish <repo-root> <state-dir> <vpk> <vpk-sha256> <preview> <preview-sha256> <owner-steamid64> [unlisted|public] | hide <repo-root> <state-dir> <owner-steamid64> | unsubscribe <repo-root> <state-dir> <owner-steamid64> confirmed-new-subscription");
    return 1;
}
if (args[0] != "who" && args[0] != "publish" && args[0] != "hide" && args[0] != "unsubscribe") throw new ArgumentException("Unknown command");
if ((args[0] == "who" && args.Length != 1) || (args[0] == "hide" && args.Length != 4) || (args[0] == "unsubscribe" && (args.Length != 5 || args[4] != "confirmed-new-subscription")) || (args[0] == "publish" && args.Length is not (8 or 9))) throw new ArgumentException("Wrong arguments; use help.");
string? state = null;
if (args[0] != "who") {
    var repo = Path.GetFullPath(args[1]).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    state = Path.GetFullPath(args[2]).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    if ((!File.Exists(Path.Combine(repo, ".git")) && !Directory.Exists(Path.Combine(repo, ".git"))) || !File.Exists(Path.Combine(repo, "hud-mockup", "tools", "WorkshopCli", "Program.cs"))) throw new ArgumentException("repo-root must be the WeaponPaints authoring worktree.");
    if (state.Equals(repo, StringComparison.OrdinalIgnoreCase) || state.StartsWith(repo + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("State must be outside the repository.");
    // Disallow reparse points, including ancestor junctions, to prevent an apparent external path resolving inside the worktree.
    for (var p = new DirectoryInfo(state); p != null; p = p.Parent) { if (p.Exists && (p.Attributes & FileAttributes.ReparsePoint) != 0) throw new ArgumentException("State path must not traverse reparse points."); if (File.Exists(Path.Combine(p.FullName, ".git")) || Directory.Exists(Path.Combine(p.FullName, ".git"))) throw new ArgumentException("State path must not be inside any Git worktree."); }
}
string? archive = null, preview = null;
if (args[0] == "publish") {
    archive = Path.GetFullPath(args[3]); preview = Path.GetFullPath(args[5]);
    RequireHash(archive, args[4]); RequireHash(preview, args[6]);
    if (!archive.EndsWith(".vpk", StringComparison.OrdinalIgnoreCase) || !preview.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Expected reviewed VPK and PNG preview.");
    if (args.Length == 9 && args[8] is not ("unlisted" or "public")) throw new ArgumentException("Visibility must be unlisted or public.");
}
Environment.SetEnvironmentVariable("SteamAppId", "730"); Environment.SetEnvironmentVariable("SteamGameId", "730");
if (!SteamAPI.Init()) throw new Exception("Steam API init failed; start and sign into the intended Steam client.");
try {
    var owner = SteamUser.GetSteamID().m_SteamID;
    Console.WriteLine($"Steam account: {SteamFriends.GetPersonaName()} ({owner}), app={SteamUtils.GetAppID().m_AppId}");
    if (args[0] == "who") return 0;
    var expectedOwner = ulong.Parse(args[args[0] is "hide" or "unsubscribe" ? 3 : 7]);
    if (owner != expectedOwner || !SteamUser.BLoggedOn() || SteamUtils.GetAppID().m_AppId != 730) throw new Exception("Wrong Steam account or app; refused.");
    Directory.CreateDirectory(state!);
    var stateFile = Path.Combine(state!, "weaponpaints-test-item.json");
    var intent = Path.Combine(state!, "weaponpaints-create-pending.txt");
    ItemState? saved = File.Exists(stateFile) ? JsonSerializer.Deserialize<ItemState>(File.ReadAllText(stateFile)) : null;
    if (saved != null && (saved.Owner != owner || saved.Id == 0 || saved.Purpose != "weaponpaints-hud-temporary-v1")) throw new Exception("Invalid or wrong-owner state; refused.");
    if (args[0] is "hide" or "unsubscribe" && saved == null) throw new Exception("No saved test item for this operation.");
    PublishedFileId_t id;
    if (saved != null) { id = new(saved.Id); VerifyOwner(id, owner); }
    else {
        if (File.Exists(intent)) throw new Exception("Previous creation result unknown. Inspect Workshop before any retry.");
        // Fail closed if interrupted between creation and durable ID persistence.
        using (var f = new FileStream(intent, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { using var w = new StreamWriter(f); w.Write(DateTimeOffset.UtcNow.ToString("O")); w.Flush(); f.Flush(true); }
        bool done = false, failed = false; var result = default(CreateItemResult_t);
        using var cb = CallResult<CreateItemResult_t>.Create((r, io) => { result = r; failed = io; done = true; });
        cb.Set(SteamUGC.CreateItem(new(730), EWorkshopFileType.k_EWorkshopFileTypeCommunity)); Pump(() => done);
        if (failed || result.m_eResult != EResult.k_EResultOK) throw new Exception("CreateItem result uncertain: " + result.m_eResult);
        id = result.m_nPublishedFileId;
        var temp = stateFile + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(new ItemState(owner, id.m_PublishedFileId, "weaponpaints-hud-temporary-v1")));
        File.Move(temp, stateFile); File.Delete(intent);
        if (result.m_bUserNeedsToAcceptWorkshopLegalAgreement) Console.WriteLine("Workshop agreement acceptance required.");
    }
    Console.WriteLine("Temporary WeaponPaints item: " + id.m_PublishedFileId);
    if (args[0] == "unsubscribe") {
        // The explicit confirmation is an assertion that this task created the
        // subscription; never unsubscribe an arbitrary or pre-existing item.
        var flags = (EItemState)SteamUGC.GetItemState(id);
        if ((flags & EItemState.k_EItemStateSubscribed) == 0) { Console.WriteLine("Already unsubscribed."); return 0; }
        bool done = false, failed = false; var result = default(RemoteStorageUnsubscribePublishedFileResult_t);
        using var cb = CallResult<RemoteStorageUnsubscribePublishedFileResult_t>.Create((r, io) => { result = r; failed = io; done = true; });
        cb.Set(SteamUGC.UnsubscribeItem(id)); Pump(() => done);
        if (failed || result.m_eResult != EResult.k_EResultOK) throw new Exception("Unsubscribe result uncertain: " + result.m_eResult);
        Console.WriteLine("Temporary item unsubscribed; verify Steam state after callback."); return 0;
    }
    var update = SteamUGC.StartItemUpdate(new(730), id);
    if (args[0] == "hide") Need(SteamUGC.SetItemVisibility(update, ERemoteStoragePublishedFileVisibility.k_ERemoteStoragePublishedFileVisibilityPrivate));
    else {
        var visibility = args.Length == 9 ? args[8] : "unlisted";
        Need(SteamUGC.SetItemTitle(update, "4ill WeaponPaints HUD — temporary test"));
        Need(SteamUGC.SetItemDescription(update, "Temporary test addon containing WeaponPaints Panorama HUD resources only. Requires the matching server plugin."));
        Need(SteamUGC.SetItemVisibility(update, visibility == "public" ? ERemoteStoragePublishedFileVisibility.k_ERemoteStoragePublishedFileVisibilityPublic : ERemoteStoragePublishedFileVisibility.k_ERemoteStoragePublishedFileVisibilityUnlisted));
        Need(SteamUGC.SetAllowLegacyUpload(update, false)); Need(SteamUGC.SetRequiredGameVersions(update, "", ""));
        // Stage a copy in a fresh private directory. Never rename or mutate the reviewed input.
        var stage = Path.Combine(state!, "upload-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(stage);
        try {
            var staged = Path.Combine(stage, id.m_PublishedFileId + ".vpk"); File.Copy(archive!, staged);
            RequireHash(staged, args[4]); RequireHash(preview!, args[6]);
            Need(SteamUGC.SetItemContent(update, stage)); Need(SteamUGC.SetItemPreview(update, preview!));
            Submit(update, "Temporary WeaponPaints HUD test");
        } finally { Directory.Delete(stage, true); }
        return 0;
    }
    Submit(update, "Hide temporary WeaponPaints HUD test"); return 0;
} finally { SteamAPI.Shutdown(); }

static void RequireHash(string path, string expected) {
    if (expected.Length != 64 || !expected.All(Uri.IsHexDigit)) throw new ArgumentException("SHA-256 must be 64 hexadecimal characters.");
    using var f = File.OpenRead(path); var actual = Convert.ToHexString(SHA256.HashData(f));
    if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase)) throw new Exception("SHA-256 mismatch: " + path);
}
static void Need(bool ok) { if (!ok) throw new Exception("Steam rejected item update."); }
static void Pump(Func<bool> done) { var until = DateTime.UtcNow.AddMinutes(3); while (!done()) { if (DateTime.UtcNow > until) throw new TimeoutException("Steam result unknown; inspect the saved item before retry."); SteamAPI.RunCallbacks(); Thread.Sleep(50); } }
static void Submit(UGCUpdateHandle_t update, string note) {
    bool done = false, failed = false; var result = default(SubmitItemUpdateResult_t);
    using var cb = CallResult<SubmitItemUpdateResult_t>.Create((r, io) => { result = r; failed = io; done = true; });
    cb.Set(SteamUGC.SubmitItemUpdate(update, note)); Pump(() => done);
    if (failed || result.m_eResult != EResult.k_EResultOK) throw new Exception("Update result uncertain: " + result.m_eResult);
    if (result.m_bUserNeedsToAcceptWorkshopLegalAgreement) Console.WriteLine("Workshop agreement acceptance required.");
    Console.WriteLine("Update accepted by Steam.");
}
static void VerifyOwner(PublishedFileId_t id, ulong owner) {
    var query = SteamUGC.CreateQueryUGCDetailsRequest([id], 1); bool done = false, failed = false; var result = default(SteamUGCQueryCompleted_t);
    using var cb = CallResult<SteamUGCQueryCompleted_t>.Create((r, io) => { result = r; failed = io; done = true; });
    try { cb.Set(SteamUGC.SendQueryUGCRequest(query)); Pump(() => done);
        if (failed || result.m_eResult != EResult.k_EResultOK || !SteamUGC.GetQueryUGCResult(query, 0, out var item) || item.m_eResult != EResult.k_EResultOK || item.m_ulSteamIDOwner != owner || item.m_nConsumerAppID.m_AppId != 730) throw new Exception("Cannot verify saved item owner/app; refused.");
    } finally { SteamUGC.ReleaseQueryUGCRequest(query); }
}
record ItemState(ulong Owner, ulong Id, string Purpose);
