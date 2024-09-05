// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Diagnostics;
using System.Reflection;

using Prowl.Editor.Assets;
using Prowl.Runtime;
using Prowl.Runtime.SceneManagement;

namespace Prowl.Editor;

/// <summary>
/// Project Cache is a static class that stores a list of all projects recognized by the editor.
/// </summary>
public static class ProjectCache
{
    private static readonly string cachePath = Path.Join(AppContext.BaseDirectory, ".projectcache");

    private static List<Project> s_projectCache;
    public static int ProjectsCount
    {
        get
        {
            UpdateProjectPaths();
            return s_projectCache.Count;
        }
    }


    public static void AddProject(Project project)
    {
        UpdateProjectPaths();
        s_projectCache.Add(project);
        SaveProjectPaths();
    }

    public static Project? GetProject(int index)
    {
        UpdateProjectPaths();

        Project project = s_projectCache[index];

        if (!project.Exists)
        {
            s_projectCache.RemoveAt(index);
            SaveProjectPaths();
            return null;
        }

        return project;
    }

    public static void RemoveProject(Project project)
    {
        UpdateProjectPaths();
        s_projectCache.RemoveAll(x => x.ProjectPath == project.ProjectPath);
        SaveProjectPaths();
    }


    private static void SaveProjectPaths()
    {
        if (!File.Exists(cachePath))
            File.Create(cachePath);

        SerializedProperty property = SerializedProperty.NewCompound();

        SerializedProperty listProperty = SerializedProperty.NewList();

        for (int i = 0; i < s_projectCache.Count; i++)
            listProperty.ListAdd(new(s_projectCache[i].ProjectPath));

        property.Add("Projects", listProperty);

        StringTagConverter.WriteToFile(property, new FileInfo(cachePath));
    }


    private static void UpdateProjectPaths()
    {
        if (s_projectCache != null)
            return;

        if (!File.Exists(cachePath))
            File.Create(cachePath);

        s_projectCache = [];

        try
        {
            SerializedProperty property = StringTagConverter.ReadFromFile(new FileInfo(cachePath));

            SerializedProperty? listProperty = property.Find("Projects");

            if (listProperty != null)
            {
                for (int i = 0; i < listProperty.Count; i++)
                {
                    DirectoryInfo info = new DirectoryInfo(listProperty[i].StringValue);

                    if (info.Exists)
                        s_projectCache.Add(new Project(info));
                }
            }
        }
        catch
        { }
    }
}
