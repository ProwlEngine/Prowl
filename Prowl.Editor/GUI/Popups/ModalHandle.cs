// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.OrigamiUI;
using Prowl.PaperUI;

namespace Prowl.Editor.GUI.Popups;

internal sealed class ModalHandle
{
    private IModal? _modal;

    public bool IsOpen => _modal != null;

    public void Open(Action<Paper, int> draw, bool closeOnBackdrop = false)
    {
        _modal = Modal.PushCustomDraw((p, layer, _) => draw(p, layer), closeOnBackdrop: closeOnBackdrop);
    }

    public void Close()
    {
        if (_modal == null) return;
        Modal.Remove(_modal);
        _modal = null;
    }
}
