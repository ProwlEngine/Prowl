// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

namespace Prowl.Runtime.Networking;

/// <summary>
///     The prefab or factory a networked object was built from, with the bytes its factory got and the identifiers its
///     tree took in build order, so peers build the same tree, and its networked components and transforms as built.
///     A scene one is an object its scene asset stores, which every peer that loaded the scene holds.
/// </summary>
internal sealed record SpawnTemplate(
    Guid Prefab,
    string? Factory,
    byte[] Data,
    Guid[] Identifiers,
    Component[] Components,
    bool Scene = false,
    NetworkTransform[]? Transforms = null);
