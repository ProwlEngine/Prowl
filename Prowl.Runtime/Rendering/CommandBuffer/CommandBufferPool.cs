using System;
using System.Collections.Generic;
using System.Linq;
using Veldrid;

namespace Prowl.Runtime
{
    public static class CommandBufferPool
    {
        private static Utils.ObjectPool<CommandBuffer> bufferPool = new();

        /// <summary>Get a clean Command Buffer.</summary>
        public static CommandBuffer Get()
        {
            var cmd = bufferPool.Get();
            cmd.Name = "";

            return cmd;
        }

        /// <summary>Get a clean, named Command Buffer.</summary>
        public static CommandBuffer Get(string name)
        {
            var cmd = bufferPool.Get();
            cmd.Name = name;

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