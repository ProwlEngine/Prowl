// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections.Generic;

using Prowl.Runtime.NodeSystem;

namespace Prowl.Runtime.Components;

public class BlueprintExecuter : MonoBehaviour
{

    public AssetRef<Blueprint> Blueprint;

    private List<OnStartEventNode> _startNodes = [];
    private List<OnEnableEventNode> _enableNodes = [];
    private List<OnDisableEventNode> _disableNodes = [];
    private List<OnDestroyEventNode> _destroyNodes = [];

    private List<OnUpdateEventNode> _updateNodes = [];
    private List<OnLateUpdateEventNode> _lateUpdateNodes = [];
    private List<OnFixedUpdateEventNode> _fixedUpdateNodes = [];


    public override void Awake()
    {
        if (Blueprint.IsAvailable)
        {
            foreach (Node node in Blueprint.Res.nodes)
            {
                if (node is OnAwakeEventNode awakeNode)
                    awakeNode.Execute(null); // Can execute immediately and not cache since Awake is never* called twice
                else if(node is OnStartEventNode startNode)
                    _startNodes.Add(startNode);
                else if (node is OnEnableEventNode enableNode)
                    _enableNodes.Add(enableNode);
                else if (node is OnDisableEventNode disableNode)
                    _disableNodes.Add(disableNode);
                else if (node is OnDestroyEventNode destroyNode)
                    _destroyNodes.Add(destroyNode);
                else if (node is OnUpdateEventNode updateNode)
                    _updateNodes.Add(updateNode);
                else if (node is OnLateUpdateEventNode lateUpdateNode)
                    _lateUpdateNodes.Add(lateUpdateNode);
                else if (node is OnFixedUpdateEventNode fixedUpdateNode)
                    _fixedUpdateNodes.Add(fixedUpdateNode);
            }
        }
    }

    private void ExecuteEventNodes<T>(List<T> nodes) where T : BasicEventNode
    {
        if (!Blueprint.IsAvailable) return;
        Blueprint.Res.SetActiveGameObject(GameObject);
        foreach (BasicEventNode node in nodes)
            node.Execute(null);
    }

    public override void Start() => ExecuteEventNodes(_startNodes);
    public override void OnEnable() => ExecuteEventNodes(_enableNodes);
    public override void OnDisable() => ExecuteEventNodes(_disableNodes);
    public override void OnDestroy() => ExecuteEventNodes(_destroyNodes);

    public override void Update() => ExecuteEventNodes(_updateNodes);
    public override void LateUpdate() => ExecuteEventNodes(_lateUpdateNodes);
    public override void FixedUpdate() => ExecuteEventNodes(_fixedUpdateNodes);
}
