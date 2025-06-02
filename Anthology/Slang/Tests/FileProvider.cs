namespace Prowl.Slang.Test;


public class FileProvider : IFileProvider
{
    public Memory<byte>? LoadFile(string path)
    {
        if (!File.Exists(path))
            return null;

        return new Memory<byte>(File.ReadAllBytes(path));
    }
}
