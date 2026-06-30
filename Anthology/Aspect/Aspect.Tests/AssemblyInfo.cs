// Many aspects in these tests keep static state (event trackers, circuit-breaker/retry/
// rate-limiter counters, memoization caches). xUnit parallelizes test collections by default,
// which makes that shared state collide. Run them sequentially.
[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]
