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

    public override void Start()
    {
        foreach (OnStartEventNode node in _startNodes)
            node.Execute(null);
    }

    public override void OnEnable()
    {
        foreach (OnEnableEventNode node in _enableNodes)
            node.Execute(null);
    }

    public override void OnDisable()
    {
        foreach (OnDisableEventNode node in _disableNodes)
            node.Execute(null);
    }

    public override void OnDestroy()
    {
        foreach (OnDestroyEventNode node in _destroyNodes)
            node.Execute(null);
    }

    public override void Update()
    {
        foreach (OnUpdateEventNode node in _updateNodes)
            node.Execute(null);
    }

    public override void LateUpdate()
    {
        foreach (OnLateUpdateEventNode node in _lateUpdateNodes)
            node.Execute(null);
    }

    public override void FixedUpdate()
    {
        foreach (OnFixedUpdateEventNode node in _fixedUpdateNodes)
            node.Execute(null);
    }
}
