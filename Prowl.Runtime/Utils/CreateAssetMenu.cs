// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

namespace Prowl.Runtime.Utils
{
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
    public class CreateAssetMenu : Attribute
    {
        public string Name { get; }
        public CreateAssetMenu(string path)
        {
            Name = path;
        }
    }
}
