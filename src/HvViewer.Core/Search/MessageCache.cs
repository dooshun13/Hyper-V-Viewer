namespace HvViewer.Core.Search;

/// <summary>In-memory least-recently-used cache of formatted event messages. Never written to disk.</summary>
public sealed class MessageCache(int capacity = 50_000)
{
    private readonly Dictionary<Key, LinkedListNode<(Key Key, string? Value)>> _map = new();
    private readonly LinkedList<(Key Key, string? Value)> _order = new(); // front = most recently used
    private readonly object _gate = new();

    public int Count
    {
        get { lock (_gate) return _map.Count; }
    }

    /// <summary>Returns the cached message or builds it. A null result is cached too.</summary>
    public string? GetOrAdd(string log, long recordId, Func<string?> factory)
    {
        var key = new Key(log, recordId);
        lock (_gate)
        {
            if (_map.TryGetValue(key, out var node))
            {
                _order.Remove(node);
                _order.AddFirst(node);
                return node.Value.Value;
            }
        }

        var value = factory(); // outside the lock: formatting can be slow

        lock (_gate)
        {
            if (_map.TryGetValue(key, out var existing)) return existing.Value.Value; // another thread won
            _map[key] = _order.AddFirst((key, value));
            while (_map.Count > capacity)
            {
                var last = _order.Last!;
                _order.RemoveLast();
                _map.Remove(last.Value.Key);
            }
        }
        return value;
    }

    public void Clear()
    {
        lock (_gate)
        {
            _map.Clear();
            _order.Clear();
        }
    }

    private readonly struct Key(string log, long recordId) : IEquatable<Key>
    {
        public bool Equals(Key other) => recordId == other.RecordId && string.Equals(log, other.Log, StringComparison.OrdinalIgnoreCase);

        public override bool Equals(object? obj) => obj is Key other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(StringComparer.OrdinalIgnoreCase.GetHashCode(log), recordId);

        private string Log => log;

        private long RecordId => recordId;
    }
}
