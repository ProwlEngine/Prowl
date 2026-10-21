// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Xunit;

namespace Prowl.Warp.Tests;

// Other stress tests deliberately exhaust shared buffer pools. Keep allocation measurements
// separate so an unrelated thread cannot consume the buffers warmed by these tests.
[CollectionDefinition("Allocations", DisableParallelization = true)]
public sealed class AllocationTestCollection;
