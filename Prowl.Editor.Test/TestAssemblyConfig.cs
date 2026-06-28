// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Xunit;

// Editor tests drive global static state (Project.Current, AssetDatabase.Current, Scene.Current,
// EditorAssetDatabase.Instance) and touch the filesystem, so they must not run in parallel.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
