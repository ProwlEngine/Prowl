using System;

namespace Veldrid.SPIRV
{
    /// <summary>
    /// Represents errors that occur in the Veldrid.SPIRV library.
    /// </summary>
    public class SpirvCompilationException : Exception
    {
        /// <summary>
        /// Constructs a new <see cref="SpirvCompilationException"/>.
        /// </summary>
        public SpirvCompilationException() { }

        /// <summary>
        /// Constructs a new <see cref="SpirvCompilationException"/> with the given message.
        /// </summary>
        public SpirvCompilationException(string message) : base(message) { }

        /// <summary>
        /// Constructs a new <see cref="SpirvCompilationException"/> with the given message and inner exception.
        /// </summary>
        public SpirvCompilationException(string message, Exception innerException) : base(message, innerException) { }
    }
}
