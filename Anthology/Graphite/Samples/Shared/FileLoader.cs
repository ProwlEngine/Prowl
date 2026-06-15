using System;
using System.IO;


namespace Prowl.Graphite.Samples;


public static class FileLoader
{
    private static DirectoryInfo[] s_searchDirectories =
    [
        new DirectoryInfo(Directory.GetCurrentDirectory())
    ];

    public static DirectoryInfo[] SearchDirectories => s_searchDirectories;


    private static Func<string, Memory<byte>?> s_fileLoader = (x) =>
    {
        if (!File.Exists(x))
            return null;

        return File.ReadAllBytes(x);
    };

    public static Func<string, Memory<byte>?> Load => s_fileLoader;
}
