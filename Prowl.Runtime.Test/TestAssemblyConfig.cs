// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Xunit;

// Runtime tests drive global static state (Application.IsPlaying, Time.TimeStack, Scene.Current),
// so they must not run in parallel with each other.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
