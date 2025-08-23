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
    }
}