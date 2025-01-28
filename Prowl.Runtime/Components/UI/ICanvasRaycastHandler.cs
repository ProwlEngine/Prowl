// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Runtime.Components.UI;

public interface ICanvasRaycastHandler
{
    bool ProcessRaycast(Vector2 screenPosition);
}
