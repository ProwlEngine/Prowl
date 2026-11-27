using System.IO;
using System.Linq;

namespace Prowl.Recast.Core;

public static class RcDirectory
{
    public static string SearchPath(string searchPath, int depth, out bool isDir)
    {
        isDir = false;

        for (int i = 0; i < depth; ++i)
        {
            string relativePath = string.Join("", Enumerable.Range(0, i).Select(x => "../"));
            string searchingPath = Path.Combine(relativePath, searchPath);
            string fullSearchingPath = Path.GetFullPath(searchingPath);

            if (File.Exists(fullSearchingPath))
            {
                return fullSearchingPath;
            }
            
            if (Directory.Exists(fullSearchingPath))
            {
                isDir = true;
                return fullSearchingPath;
            }
        }

        return string.Empty;
    }

    // only directory
    public static string SearchDirectory(string dirname, int depth = 10)
    {
        string searchingPath = SearchPath(dirname, depth, out bool isDir);
        if (isDir)
        {
            return searchingPath;
        }

        string path = Path.GetDirectoryName(searchingPath) ?? string.Empty;
        return path;
    }

    public static string SearchFile(string filename, int depth = 10)
    {
        string searchingPath = SearchPath(filename, depth, out bool isDir);
        if (!isDir)
        {
            return searchingPath;
        }

        return string.Empty;
    }
}