using System;
using System.IO;
using System.Runtime.CompilerServices;

using Prowl.Slang;


public class FileProvider : IFileProvider
{
    public Memory<byte>? LoadFile(string path)
    {
        if (!File.Exists(path))
            return null;

        return new Memory<byte>(File.ReadAllBytes(path));
    }
}
