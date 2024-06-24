using Prowl.Editor.Assets;
using Prowl.Runtime;
using Prowl.Runtime.Utils;
using System.Reflection;
using static Prowl.Editor.MenuItem;

namespace Prowl.Editor
{
    public static class MeshRaycaster
    {
        public struct MeshHitInfo
        {
            public GameObject gameObject;
            public Vector3 worldPosition;
        }

        public static MeshHitInfo Raycast(Ray ray)
        {
            return default;
        }
    }
}
