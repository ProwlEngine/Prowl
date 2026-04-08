// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.PaperUI.LayoutEngine;
using Prowl.Vector;
using Prowl.Vector.Geometry;

namespace Prowl.PaperUI.Events
{
    public enum ClickPhase
    {
        Click,
        Press,
        Release,
        DoubleClick,
        RightClick,
        Held
    }

    public class ClickEvent : ElementEvent
    {
        public PaperMouseBtn Button { get; }

        /// <summary>
        /// Identifies which click handler this event targets during bubbling.
        /// </summary>
        public ClickPhase Phase { get; }

        public ClickEvent(ElementHandle source, Rect elementRect, Float2 pointerPos, PaperMouseBtn button, ClickPhase phase = ClickPhase.Click)
            : base(source, elementRect, pointerPos)
        {
            Button = button;
            Phase = phase;
        }
    }
}
