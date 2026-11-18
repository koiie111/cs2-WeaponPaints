namespace WeaponPaints.Hud;

internal enum HudRetryDecision { Ignore, Complete, Timeout, WaitDeadline, Retry }
internal readonly record struct HudRetryStep(HudRetryDecision Decision, TimeSpan Delay);

/// <summary>Pure bounded retry decision. The caller owns one existing deadline timer and cancellation.</summary>
internal static class HudRetryPolicy
{
    internal const int MaxAttempts = 20;
    internal static HudRetryStep Decide(bool current, bool transient, int attempts, TimeSpan elapsed, TimeSpan timeout)
    {
        if (!current) return new(HudRetryDecision.Ignore, TimeSpan.Zero);
        if (attempts < 1 || elapsed < TimeSpan.Zero || timeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(attempts));
        if (elapsed >= timeout) return new(HudRetryDecision.Timeout, TimeSpan.Zero);
        if (!transient) return new(HudRetryDecision.Complete, TimeSpan.Zero);
        if (attempts >= MaxAttempts) return new(HudRetryDecision.WaitDeadline, TimeSpan.Zero);
        var delay = TimeSpan.FromSeconds(attempts switch { 1 => 1, 2 => 2, 3 => 4, _ => 5 });
        return delay >= timeout - elapsed ? new(HudRetryDecision.WaitDeadline, TimeSpan.Zero) : new(HudRetryDecision.Retry, delay);
    }
}
