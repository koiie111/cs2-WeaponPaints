using System.Globalization;
using System.Text;

namespace WeaponPaints.Hud;

internal readonly record struct HudWriteKey(bool Variable, string Panel, string Name);
internal readonly record struct HudWrite(HudWriteKey Key, string Value);

/// <summary>Game-thread-only, replaceable visual target. No scheduled work while empty.</summary>
internal sealed class HudDeltaQueue
{
    private const int MaximumWrites = 2048;
    private readonly Func<HudWriteKey, string> _authored;
    private readonly Dictionary<HudWriteKey, string> _applied = new();
    private readonly Queue<HudWrite> _pending = new();
    private readonly List<HudWrite> _changes = new();

    internal HudDeltaQueue(Func<HudWriteKey, string> authored) => _authored = authored;
    internal int PendingCount => _pending.Count;

    internal void Replace(IReadOnlyDictionary<HudWriteKey, string> desired, bool force = false)
    {
        if (desired.Count > MaximumWrites) throw new InvalidDataException("HUD frame exceeds write budget");
        _changes.Clear();
        foreach (var (key, old) in _applied)
        {
            var next = desired.TryGetValue(key, out var value) ? value : _authored(key);
            if (force || old != next) _changes.Add(new(key, next));
        }
        foreach (var (key, next) in desired)
            if (!_applied.ContainsKey(key) && (force || next != _authored(key))) _changes.Add(new(key, next));

        if (_changes.Count > MaximumWrites) throw new InvalidDataException("HUD delta exceeds write budget");
        // Publish text before visual changes and clear obsolete classes before adding new ones.
        _pending.Clear();
        foreach (var write in _changes) if (write.Key.Variable) _pending.Enqueue(write);
        foreach (var write in _changes) if (!write.Key.Variable && write.Value == "0") _pending.Enqueue(write);
        foreach (var write in _changes) if (!write.Key.Variable && write.Value != "0") _pending.Enqueue(write);
        _changes.Clear();
    }

    internal bool TryPeek(out HudWrite write) => _pending.TryPeek(out write);
    internal void Commit(HudWrite written)
    {
        if (!_pending.TryPeek(out var expected) || expected != written) throw new InvalidOperationException("HUD write order changed");
        _pending.Dequeue();
        if (written.Value == _authored(written.Key)) _applied.Remove(written.Key);
        else _applied[written.Key] = written.Value;
    }
    internal void Cancel() => _pending.Clear();
}

internal readonly record struct HudViewerStamp(int Slot, ulong SteamId, uint Controller, uint Pawn, long MapGeneration, long SessionGeneration)
{
    internal bool Matches(HudViewerStamp other) => this == other && SteamId != 0 && Controller != 0;
    internal bool OwnsController(ulong steam, uint controller) => SteamId != 0 && Controller != 0 && SteamId == steam && Controller == controller;
}

internal sealed class HudPromptLease
{
    internal HudPromptLease(HudViewerStamp owner, string field, DateTimeOffset expires)
        => (Owner, Field, Expires) = (owner, field, expires);
    internal HudViewerStamp Owner { get; }
    internal string Field { get; }
    internal DateTimeOffset Expires { get; }
    internal bool Accepts(HudViewerStamp current, DateTimeOffset now) => Owner.Matches(current) && now < Expires;
}

internal static class HudInput
{
    internal static bool TryFloat(string? text, float minimum, float maximum, out float value)
    {
        value = 0;
        return float.TryParse(text?.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value)
            && float.IsFinite(value) && value >= minimum && value <= maximum;
    }
    internal static bool TryInteger(string? text, int minimum, int maximum, out int value)
    {
        value = 0;
        return int.TryParse(text?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value)
            && value >= minimum && value <= maximum;
    }
    internal static string CleanText(string? text, int maxUtf8Bytes)
    {
        if (maxUtf8Bytes < 0) throw new ArgumentOutOfRangeException(nameof(maxUtf8Bytes));
        var result = new StringBuilder();
        var bytes = 0;
        foreach (var rune in (text ?? "").Trim().EnumerateRunes())
        {
            if (Rune.IsControl(rune)) continue;
            if (bytes + rune.Utf8SequenceLength > maxUtf8Bytes) break;
            result.Append(rune.ToString());
            bytes += rune.Utf8SequenceLength;
        }
        return result.ToString();
    }
}
