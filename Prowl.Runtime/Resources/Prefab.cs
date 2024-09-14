// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Runtime;

public class Prefab : EngineObject
{
    public SerializedProperty GameObject;

    public GameObject Instantiate()
    {
        var go = Serializer.Deserialize<GameObject>(GameObject);
        go.AssetID = AssetID;
        return go;
    }
}
