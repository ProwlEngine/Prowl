// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Editor;

public class TreePath<T>
{
    public TreePath<T> Parent { get; set; }
    public string Name { get; set; }
    public T Data { get; set; }
    public List<TreePath<T>> Children { get; } = new List<TreePath<T>>();

    public TreePath() { }

    public TreePath(string name, T data = default)
    {
        Name = name;
        Data = data;
    }

    public void AddChild(string path, T data = default)
    {
        string[] parts = path.Split('/');
        TreePath<T> currentNode = this;

        for (int i = 0; i < parts.Length - 1; i++)
        {
            string part = parts[i];
            TreePath<T> childNode = currentNode.Children.Find(c => c.Name == part);
            if (childNode == null)
            {
                childNode = new TreePath<T> { Name = part };
                childNode.Parent = currentNode;
                currentNode.Children.Add(childNode);
            }
            currentNode = childNode;
        }

        TreePath<T> leafNode = new TreePath<T>(parts[^1], data);
        leafNode.Parent = currentNode;
        currentNode.Children.Add(leafNode);
    }

    public TreePath<T>? FindNode(string path)
    {
        string[] parts = path.Split('/');
        TreePath<T>? currentNode = this;

        foreach (string part in parts)
        {
            currentNode = currentNode.Children.Find(c => c.Name == part);
            if (currentNode == null)
                return null;
        }

        return currentNode;
    }

    public List<TreePath<T>> Search(string query, bool ignoreCase = true)
    {
        List<TreePath<T>> results = new List<TreePath<T>>();
        SearchRecursive(this, query, results, ignoreCase);
        return results;
    }

    private void SearchRecursive(TreePath<T> node, string query, List<TreePath<T>> results, bool ignoreCase)
    {
        if (node.Name.Contains(query, ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            results.Add(node);

        foreach (TreePath<T> child in node.Children)
            SearchRecursive(child, query, results, ignoreCase);
    }
}