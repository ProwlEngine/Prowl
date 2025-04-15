namespace Prowl.PaperUI.Graphics
{
    /// <summary>
    /// Represents a drawing command in the Paper vector graphics system.
    /// Commands are used to build paths and control rendering behavior.
    /// </summary>
    internal struct Command
    {
        /// <summary>Type of command to execute</summary>
        public CommandType Type;

        /// <summary>Command parameters (usage depends on command type)</summary>
        public float P1, P2, P3, P4, P5, P6;

        /// <summary>
        /// Creates a new command of the specified type with default parameters.
        /// </summary>
        /// <param name="type">Type of command to create</param>
        public Command(CommandType type)
        {
            Type = type;
            P1 = P2 = P3 = P4 = P5 = P6 = 0;
        }

        /// <summary>
        /// Creates a winding command that sets path fill solidity.
        /// </summary>
        /// <param name="solidity">Fill rule for the path</param>
        public Command(Solidity solidity)
        {
            Type = CommandType.Winding;
            P1 = (int)solidity;
            P2 = P3 = P4 = P5 = P6 = 0;
        }

        /// <summary>
        /// Creates a command with two parameters (commonly used for MoveTo and LineTo).
        /// </summary>
        /// <param name="type">Type of command to create</param>
        /// <param name="p1">First parameter (typically X coordinate)</param>
        /// <param name="p2">Second parameter (typically Y coordinate)</param>
        public Command(CommandType type, float p1, float p2)
        {
            Type = type;
            P1 = p1;
            P2 = p2;
            P3 = P4 = P5 = P6 = 0;
        }

        /// <summary>
        /// Creates a bezier curve command with control points and end point.
        /// </summary>
        /// <param name="p1">First control point X</param>
        /// <param name="p2">First control point Y</param>
        /// <param name="p3">Second control point X</param>
        /// <param name="p4">Second control point Y</param>
        /// <param name="p5">End point X</param>
        /// <param name="p6">End point Y</param>
        public Command(float p1, float p2, float p3, float p4, float p5, float p6)
        {
            Type = CommandType.BezierTo;
            P1 = p1;
            P2 = p2;
            P3 = p3;
            P4 = p4;
            P5 = p5;
            P6 = p6;
        }
    }
}