using HvViewer.Core.Search;
using Xunit;

namespace HvViewer.Tests.Core;

public class MessageCacheTests
{
    [Fact] public void Factory_CalledOnceForRepeatedKeys()
    {
        var cache = new MessageCache();
        var calls = 0;
        string? Make() { calls++; return "text"; }

        Assert.Equal("text", cache.GetOrAdd("System", 1, Make));
        Assert.Equal("text", cache.GetOrAdd("System", 1, Make));

        Assert.Equal(1, calls);
        Assert.Equal(1, cache.Count);
    }

    [Fact] public void LeastRecentlyUsed_IsEvictedAtCapacity()
    {
        var cache = new MessageCache(capacity: 2);
        var calls = new Dictionary<long, int>();
        string? Make(long id) { calls[id] = calls.GetValueOrDefault(id) + 1; return "m" + id; }

        cache.GetOrAdd("System", 1, () => Make(1)); // A
        cache.GetOrAdd("System", 2, () => Make(2)); // B
        cache.GetOrAdd("System", 1, () => Make(1)); // touch A
        cache.GetOrAdd("System", 3, () => Make(3)); // C evicts B

        Assert.Equal(2, cache.Count);
        cache.GetOrAdd("System", 1, () => Make(1));
        cache.GetOrAdd("System", 2, () => Make(2));
        Assert.Equal(1, calls[1]);  // A survived
        Assert.Equal(2, calls[2]);  // B was evicted and rebuilt
    }

    [Fact] public void Null_IsCachedToo()
    {
        var cache = new MessageCache();
        var calls = 0;
        string? Make() { calls++; return null; }

        Assert.Null(cache.GetOrAdd("System", 1, Make));
        Assert.Null(cache.GetOrAdd("System", 1, Make));

        Assert.Equal(1, calls);
    }

    [Fact] public void LogName_IgnoresCase()
    {
        var cache = new MessageCache();
        cache.GetOrAdd("System", 1, () => "a");
        Assert.Equal("a", cache.GetOrAdd("SYSTEM", 1, () => "b"));
    }

    [Fact] public void Clear_EmptiesCache()
    {
        var cache = new MessageCache();
        cache.GetOrAdd("System", 1, () => "a");
        cache.Clear();
        Assert.Equal(0, cache.Count);
        Assert.Equal("b", cache.GetOrAdd("System", 1, () => "b"));
    }
}
