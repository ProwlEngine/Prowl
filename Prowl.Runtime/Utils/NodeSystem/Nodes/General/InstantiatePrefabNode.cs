// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Runtime.Cloning;

namespace Prowl.Runtime.NodeSystem;

[Node("General/Instantiate Prefab")]
public class InstantiatePrefabNode : InOutFlowNode
{
    public override string Title => "Instantiate Prefab";
    public override float Width => 150;

    [Input] public AssetRef<Prefab> Prefab;
    [Output, SerializeIgnore, CloneField(CloneFieldFlags.Skip)] public GameObject Output;

    public override object GetValue(NodePort port) => Output;

    public override void Execute(NodePort input)
    {
        AssetRef<Prefab> prefab = GetInputValue("Prefab", Prefab);

        if (prefab != null && prefab.IsAvailable)
        {
            Output = prefab.Res.Instantiate();
            SceneManagement.SceneManager.Scene.Add(Output);
        }

        ExecuteNext();
    }
}
