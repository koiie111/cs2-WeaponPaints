using System.Globalization;
using System.Text;

namespace WeaponPaints.Hud;

internal readonly record struct HudWriteKey(bool Variable, string Panel, string Name);
internal readonly record struct HudWrite(HudWriteKey Key, string Value);

/// <summary>Game-thread-owned applied state. Prepare compares immutable frames on a worker.</summary>
internal sealed class HudDeltaQueue
{
    private const int MaximumWrites = 2048;
    private readonly Func<HudWriteKey, string> _authored;
    private readonly Dictionary<HudWriteKey, string> _applied = new();
    private readonly Queue<HudWrite> _pending = new();
    private readonly List<HudWrite> _changes = new();
    private IReadOnlyDictionary<HudWriteKey, string>? _target;

    internal sealed record PreparedFrame(IReadOnlyDictionary<HudWriteKey, string> Target,
        IReadOnlyDictionary<HudWriteKey, string>? Previous, List<HudWrite>? Changes);

    internal HudDeltaQueue(Func<HudWriteKey, string> authored) => _authored = authored;
    internal int PendingCount => _pending.Count;
    // Only completed, immutable renderer output can serve as a worker's baseline.
    internal IReadOnlyDictionary<HudWriteKey, string>? SettledTarget => _pending.Count == 0 ? _target : null;

    internal PreparedFrame Prepare(IReadOnlyDictionary<HudWriteKey, string>? previous, IReadOnlyDictionary<HudWriteKey, string> desired)
    {
        if (desired.Count > MaximumWrites) throw new InvalidDataException("HUD frame exceeds write budget");
        if (previous == null) return new(desired, null, null);
        var changes = new List<HudWrite>();
        CollectChanges(previous, desired, false, changes);
        return new(desired, previous, changes);
    }

    private void CollectChanges(IReadOnlyDictionary<HudWriteKey, string> previous,
        IReadOnlyDictionary<HudWriteKey, string> desired, bool force, List<HudWrite> changes)
    {
        foreach (var (key, old) in previous)
        {
            var next = desired.TryGetValue(key, out var value) ? value : _authored(key);
            if (force || old != next) changes.Add(new(key, next));
        }
        foreach (var (key, next) in desired)
            if (!previous.ContainsKey(key) && (force || next != _authored(key))) changes.Add(new(key, next));
    }

    internal void Replace(IReadOnlyDictionary<HudWriteKey, string> desired, bool force = false)
    {
        if (desired.Count > MaximumWrites) throw new InvalidDataException("HUD frame exceeds write budget");
        _changes.Clear();
        CollectChanges(_applied, desired, force, _changes);
        Enqueue(_changes);
        _changes.Clear();
        // Regular HUD screens also hand off finished renderer output. A first visit
        // to Collections can then compare against the screen already on display.
        _target = desired;
    }

    internal void Replace(PreparedFrame frame, bool force)
    {
        if (!force && frame.Changes is { } changes && ReferenceEquals(frame.Previous, SettledTarget))
            Enqueue(changes);
        else
            Replace(frame.Target, force);
        _target = frame.Target;
    }

    private void Enqueue(List<HudWrite> changes)
    {
        if (changes.Count > MaximumWrites) throw new InvalidDataException("HUD delta exceeds write budget");
        // Publish text before visual changes and clear obsolete classes before adding new ones.
        _pending.Clear();
        foreach (var write in changes) if (write.Key.Variable) _pending.Enqueue(write);
        foreach (var write in changes) if (!write.Key.Variable && write.Value == "0") _pending.Enqueue(write);
        foreach (var write in changes) if (!write.Key.Variable && write.Value != "0") _pending.Enqueue(write);
    }

    internal bool TryPeek(out HudWrite write) => _pending.TryPeek(out write);
    internal void Commit(HudWrite written)
    {
        if (!_pending.TryPeek(out var expected) || expected != written) throw new InvalidOperationException("HUD write order changed");
        _pending.Dequeue();
        if (written.Value == _authored(written.Key)) _applied.Remove(written.Key);
        else _applied[written.Key] = written.Value;
    }
    internal void Cancel()
    {
        _pending.Clear();
        _target = null;
    }
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
