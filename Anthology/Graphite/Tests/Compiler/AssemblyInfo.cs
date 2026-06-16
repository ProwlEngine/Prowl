using Xunit;

// Each test spins up a Slang session, and Slang's sessions are not thread-safe across
// concurrent CreateSession calls. Run the compiler tests serially to avoid the emitter racing.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
