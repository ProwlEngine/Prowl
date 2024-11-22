// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Linq;

namespace Prowl.Runtime.Utils;

public class CircularDependencyDetector
{
    private readonly Dictionary<string, HashSet<string>> _dependencyGraph;
    private readonly HashSet<string> _visitedNodes;
    private readonly Stack<string> _currentPath;

    public CircularDependencyDetector()
    {
        _dependencyGraph = [];
        _visitedNodes = [];
        _currentPath = new Stack<string>();
    }

    public void AddDependency(string entry, string dependency)
    {
        if (!_dependencyGraph.ContainsKey(entry))
            _dependencyGraph[entry] = [];
        _dependencyGraph[entry].Add(dependency);
    }

    public bool HasCircularDependency(string startEntry, out IList<string>? circle)
    {
        _visitedNodes.Clear();
        _currentPath.Clear();
        circle = null;

        if (DetectCircle(startEntry))
        {
            circle = _currentPath.Reverse().ToList();
            return true;
        }
        return false;
    }

    private bool DetectCircle(string entry)
    {
        if (_currentPath.Contains(entry))
        {
            // Found a circle - add the current entry to complete the circle
            _currentPath.Push(entry);
            return true;
        }

        if (_visitedNodes.Contains(entry))
        {
            return false; // Already checked this branch
        }

        _visitedNodes.Add(entry);
        _currentPath.Push(entry);

        if (_dependencyGraph.TryGetValue(entry, out HashSet<string>? dependencies))
        {
            foreach (string dependency in dependencies)
            {
                if (DetectCircle(dependency))
                {
                    return true;
                }
            }
        }

        _currentPath.Pop();
        return false;
    }
}
