// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Runtime
{
    public static class CommandBufferPool
    {
        private static readonly Utils.ObjectPool<CommandBuffer> bufferPool = new();

        /// <summary>Get a clean Command Buffer.</summary>
        public static CommandBuffer Get()
        {
            return Get("New Command Buffer");
        }

        /// <summary>Get a clean, named Command Buffer.</summary>
        public static CommandBuffer Get(string name)
        {
            CommandBuffer cmd = bufferPool.Get();
            cmd.Name = name;

            cmd.BeginRecording();

            return cmd;
        }

        /// <summary>Release a Command Buffer.</summary>
        public static void Release(CommandBuffer buffer)
        {
            buffer.Clear();
            bufferPool.Release(buffer);
        }
    }
}
