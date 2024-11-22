// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.IO;
using System.Threading.Tasks;

namespace Prowl.Runtime.Utils;

public class AsyncFileLocker : IDisposable
{
    private readonly string _lockFilePath;
    private FileStream? _lockFileStream;

    public AsyncFileLocker(string packagesDirectory, string fileName)
    {
        _lockFilePath = Path.Combine(packagesDirectory, fileName);
    }

    public async Task<bool> AcquireLockAsync(TimeSpan timeout)
    {
        DateTime deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline)
        {
            try
            {
                _lockFileStream = new FileStream(
                    _lockFilePath,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None,
                    bufferSize: 1,
                    FileOptions.DeleteOnClose);

                // Write process ID for debugging
                string processId = System.Diagnostics.Process.GetCurrentProcess().Id.ToString();
                await _lockFileStream.WriteAsync(
                    System.Text.Encoding.UTF8.GetBytes(processId));

                return true;
            }
            catch (IOException)
            {
                await Task.Delay(100);
            }
        }
        return false;
    }

    void IDisposable.Dispose()
    {
        _lockFileStream?.Dispose();
    }
}
