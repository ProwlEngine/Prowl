// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Prowl.Runtime.NodeSystem;

[Node("Event/On Awake")] public class OnAwakeEventNode : BasicEventNode { public override string Title => "On Awake"; }
[Node("Event/On Start")] public class OnStartEventNode : BasicEventNode { public override string Title => "On Start"; }
[Node("Event/On Enable")] public class OnEnableEventNode : BasicEventNode { public override string Title => "On Enable"; }
[Node("Event/On Disable")] public class OnDisableEventNode : BasicEventNode { public override string Title => "On Disable"; }
[Node("Event/On Destroy")] public class OnDestroyEventNode : BasicEventNode { public override string Title => "On Destroy"; }

[Node("Event/On Update")] public class OnUpdateEventNode : BasicEventNode { public override string Title => "On Update"; }
[Node("Event/On Late Update")] public class OnLateUpdateEventNode : BasicEventNode { public override string Title => "On Late Update"; }
[Node("Event/On Fixed Update")] public class OnFixedUpdateEventNode : BasicEventNode { public override string Title => "On Fixed Update"; }
