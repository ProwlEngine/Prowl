using Xunit;

namespace Prowl.Graphite.Tests;

// CPU-side tests (Core/*, format helpers, profiling value types) run in parallel.
// GPU tests share a single graphics device per backend and must not run concurrently,
// so every GPU test class joins this collection, which disables parallelization for
// its members while leaving the rest of the suite parallel.
[CollectionDefinition("GPU Tests", DisableParallelization = true)]
public sealed class GpuTestCollection { }
