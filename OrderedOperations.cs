namespace WeaponPaints;
// FIFO reservation happens at invocation, not after connection acquisition. Idle keys are removed.
internal sealed class OrderedOperations
{
    private readonly object _gate = new();
    private readonly Dictionary<string, Task> _tails = new(StringComparer.Ordinal);
    internal int PendingKeys { get { lock (_gate) return _tails.Count; } }
    internal Task<IDisposable> EnterAsync(string key)
    {
        Task previous;
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_gate) { previous = _tails.GetValueOrDefault(key, Task.CompletedTask); _tails[key] = finished.Task; }
        return WaitAsync();
        async Task<IDisposable> WaitAsync()
        {
            await previous.ConfigureAwait(false);
            return new Lease(() => {
                lock (_gate) { if (_tails.TryGetValue(key, out var tail) && tail == finished.Task) _tails.Remove(key); }
                finished.TrySetResult();
            });
        }
    }
    private sealed class Lease(Action release) : IDisposable
    {
        private Action? _release = release;
        public void Dispose() => Interlocked.Exchange(ref _release, null)?.Invoke();
    }
}
