using System.Runtime.InteropServices;

using System.Numerics;
using Prowl.PaperUI;

namespace Prowl.PaperUI.Graphics
{
    /// <summary>
    /// Represents a scissor used for clipping inside the Canvas.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
	internal struct ScissorUniform
	{
		public Transform Transform;
		public Vector2 Extent;
	}
}
