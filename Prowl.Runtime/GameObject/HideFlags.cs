using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Prowl.Runtime
{
    [Flags]
    public enum HideFlags
    {
        None = 0,
        Hide = 1 << 0,
        NotEditable = 1 << 1,
        DontSave = 1 << 2,
        HideAndDontSave = 1 << 3
    }
}
