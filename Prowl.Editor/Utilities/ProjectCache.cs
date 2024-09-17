// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Runtime;
using Prowl.Runtime.Utils;

namespace Prowl.Editor;

[FilePath("Projects.cache", FilePathAttribute.Location.EditorPreference)]
public class ProjectCache : ScriptableSingleton<ProjectCache>, ISerializationCallbackReceiver
{
    [NonSerialized]
    private List<Project> _projectCache = [];

    [SerializeField]
    private List<string> _serializedProjects = [];

    public int ProjectsCount => _projectCache.Count;

    [SerializeField]
    private string _savedProjectsFolder = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    public string SavedProjectsFolder
    {
        get
        {
            if (!Directory.Exists(_savedProjectsFolder))
            {
                Save();
                _savedProjectsFolder = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            }

            return _savedProjectsFolder;
        }

        set
        {
            if (!Directory.Exists(value))
                return;

            _savedProjectsFolder = value;
            Save();
        }
    }


    public void AddProject(Project project)
    {
        if (!_projectCache.Exists(x => x.ProjectPath == project.ProjectPath))
        {
            _projectCache.Add(project);
            Save();
        }
    }


    // Project starring which keeps track of a sub-range of projects at the top of the array.
    /*
    public void StarProject(int projectIndex, bool isStarred)
    {
        if (projectIndex >= _starredProjects && isStarred)
        {
            (_projectCache[_starredProjects], _projectCache[projectIndex]) = (_projectCache[projectIndex], _projectCache[_starredProjects]);
            _starredProjects++;
            return;
        }

        if (projectIndex < _starredProjects && !isStarred)
        {
            _starredProjects--;
        }

        SortProjects();
    }


    private void SortProjects()
    {
        _projectCache.Sort(0, _starredProjects, ProjectComparer.Instance);
        _projectCache.Sort(_starredProjects, _projectCache.Count - _starredProjects, ProjectComparer.Instance);
    }


    private class ProjectComparer : IComparer<Project>
    {
        public static ProjectComparer Instance = new();

        public int Compare(Project? a, Project? b)
        {
            return a.ProjectDirectory.LastAccessTime.CompareTo(b.ProjectDirectory.LastAccessTime);
        }
    }
    */


    public Project GetProject(int index)
    {
        Project project = _projectCache[index];
        project.Refresh();

        return project;
    }

    public void RemoveProject(Project project)
    {
        _projectCache.RemoveAll(x => x.ProjectPath == project.ProjectPath);
        Save();
    }


    public void OnBeforeSerialize()
    {
        _serializedProjects = _projectCache.Select(x => x.ProjectPath).ToList();
    }


    public void OnAfterDeserialize()
    {
        _projectCache = [];

        foreach (string path in _serializedProjects)
        {
            Project project = new Project(new DirectoryInfo(path));

            if (!_projectCache.Exists(x => x.ProjectPath == project.ProjectPath))
            {
                _projectCache.Add(project);
            }
        }
    }
}
