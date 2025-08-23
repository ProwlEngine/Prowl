using Prowl.Scribe;

namespace Tests
{
    public class UtilityTests
    {
        [Fact]
        public void LRU_EvictsLeastRecentlyUsedItem()
        {
            var cache = new LruCache<string, int>(2);
            cache.Add("a", 1);
            cache.Add("b", 2);

            // Access "a" so that "b" becomes least recently used
            Assert.True(cache.TryGetValue("a", out _));

            cache.Add("c", 3); // should evict "b"

            Assert.False(cache.TryGetValue("b", out _));
            Assert.True(cache.TryGetValue("a", out var aVal) && aVal == 1);
            Assert.True(cache.TryGetValue("c", out var cVal) && cVal == 3);
        }

        [Fact]
        public void UpdatingExistingKeyMovesItToFront()
        {
            var cache = new LruCache<string, int>(2);
            cache.Add("a", 1);
            cache.Add("b", 2);

            // Update "a" which should also mark it as most recently used
            cache.Add("a", 10);

            cache.Add("c", 3); // should evict "b"

            Assert.False(cache.TryGetValue("b", out _));
            Assert.True(cache.TryGetValue("a", out var aVal) && aVal == 10);
            Assert.True(cache.TryGetValue("c", out var cVal) && cVal == 3);
        }

        [Fact]
        public void ReducingCapacityTrimsOldEntries()
        {
            var cache = new LruCache<string, int>(3);
            cache.Add("a", 1);
            cache.Add("b", 2);
            cache.Add("c", 3);

            // Access "a" so "b" is least recently used
            cache.TryGetValue("a", out _);

            // Reduce capacity to 2 which should evict "b"
            cache.Capacity = 2;

            Assert.False(cache.TryGetValue("b", out _));
            Assert.True(cache.TryGetValue("a", out var aVal) && aVal == 1);
            Assert.True(cache.TryGetValue("c", out var cVal) && cVal == 3);
        }
    }
}