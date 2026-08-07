// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Runtime.Resources;

namespace Prowl.Runtime.UI;

/// <summary>
/// Per-state sprites used by <see cref="SelectableTransition.SpriteSwap"/>. An unset entry falls back
/// to the sprite the target graphic was authored with.
/// </summary>
public struct SpriteState
{
    public AssetRef<Sprite> HighlightedSprite;
    public AssetRef<Sprite> PressedSprite;
    public AssetRef<Sprite> SelectedSprite;
    public AssetRef<Sprite> DisabledSprite;
}
