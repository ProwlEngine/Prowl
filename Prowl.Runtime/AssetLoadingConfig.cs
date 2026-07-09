// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Runtime;

/// <summary>
/// Global toggle for asynchronous (background-threaded) asset loading.
/// <para>
/// When <see cref="AsyncEnabled"/> is true, <see cref="AssetRef{T}.Res"/> resolves
/// non-blocking: it returns the cached instance if present, otherwise queues a load on
/// the <see cref="AssetLoader"/> background thread and returns null until the asset
/// streams in. When false, <c>.Res</c> blocks and loads synchronously (legacy behavior).
/// </para>
/// <para>
/// Driven by the "Async Asset Loading" project setting (default ON). The editor's
/// settings apply path and the standalone <c>PlayerSettingsLoader</c> both write here.
/// </para>
/// </summary>
public static class AssetLoadingConfig
{
    /// <summary>Whether asset resolution streams on a background thread (true) or blocks (false).</summary>
    public static volatile bool AsyncEnabled = true;
}
