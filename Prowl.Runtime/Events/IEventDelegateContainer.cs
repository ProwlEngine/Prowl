// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Prowl.Runtime.Events {

public interface IEventDelegateContainer : IDisposable
{
    public bool Enabled { get; }
    public void Enable();
    public void Disable();

    public bool Added { get; }
    public bool HasTag(string tag);
}

}
