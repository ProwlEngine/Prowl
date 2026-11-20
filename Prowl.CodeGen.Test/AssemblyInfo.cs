// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Xunit;

// Compiling scripts loads assemblies, which changes the network schema a hosted test's client must match.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
