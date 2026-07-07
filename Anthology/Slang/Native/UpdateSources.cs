#:package Octokit@14.0.0

using System;
using System.Threading.Tasks;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Collections.Generic;
using System.Text;
using System.Threading;

using Octokit;
using System.IO.Compression;


const string RepoOwner = "shader-slang";
const string Repo = "slang";
const string Release = "2026.12";
const string ReleaseTag = "v2026.12";

(string, string)?[] s_targets =
[
    ("windows-x86_64.zip", "windows-x64"),
    ("windows-aarch64.zip", "windows-arm64"),
    ("linux-x86_64.zip", "linux-x64"),
    ("linux-aarch64.zip", "linux-arm64"),
    ("macos-x86_64.zip", "macos-x64"),
    ("macos-aarch64.zip", "macos-arm64"),
];

(string, string)[] binaryMappings = [
    ($"libslang-glsl-module-{Release}.so", $"libslang-glsl-module-{Release}.so"),
    ($"libslang-glslang-{Release}.so", $"libslang-glslang-{Release}.so"),
    ($"libslang-compiler.so.0.{Release}", $"libslang-compiler.so"),

    ($"libslang-glsl-module-{Release}.dylib", $"libslang-glsl-module-{Release}.dylib"),
    ($"libslang-glslang-{Release}.dylib", $"libslang-glslang-{Release}.dylib"),
    ($"libslang-compiler.0.{Release}.dylib", "libslang-compiler.dylib"),

    ("slang-glsl-module.dll", "slang-glsl-module.dll"),
    ("slang-glslang.dll", "slang-glslang.dll"),
    ("slang-compiler.dll", "slang-compiler.dll")
];

string[] binariesToKeep = binaryMappings.Select((x, y) => x.Item1).ToArray();

string GetScriptPath([CallerFilePath] string filePath = null) => Directory.GetParent(filePath).FullName;

string s_targetPath = Path.Join(GetScriptPath(), "lib");

Directory.CreateDirectory(s_targetPath);

GitHubClient client = new GitHubClient(new Octokit.ProductHeaderValue("UpdateSources"));

Console.WriteLine($"Fetching Release: {ReleaseTag}");

try
{
    Release release = await client.Repository.Release.Get(RepoOwner, Repo, ReleaseTag);

    int id = 0;
    foreach (ReleaseAsset asset in release.Assets)
    {
        (string, string)? target = s_targets.FirstOrDefault(x => asset.Name.EndsWith(x.Value.Item1));

        if (target == null)
            continue;

        await DownloadRelease(asset, target.Value.Item2, id);
    }
}
catch (NotFoundException)
{
    Console.WriteLine($"Could not find owner {RepoOwner}, repository {Repo}, or release with tag {ReleaseTag}.");
    return;
}


async Task DownloadRelease(ReleaseAsset asset, string targetPathName, int ID)
{
    string outputPath = Path.Join(s_targetPath, targetPathName);
    string tempPath = outputPath + ".temp.zip";

    using HttpClient client = new();
    client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("UpdateSources", "1.0"));
    client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/octet-stream"));

    using HttpResponseMessage response = await client.GetAsync(asset.Url, HttpCompletionOption.ResponseHeadersRead);
    response.EnsureSuccessStatusCode();

    long totalBytes = response.Content.Headers.ContentLength ?? 0;
    long downloadedBytes = 0;

    const int blockSize = 2048;

    using Stream responseStream = await response.Content.ReadAsStreamAsync();
    using (FileStream fileStream = new FileStream(tempPath, System.IO.FileMode.Create, FileAccess.ReadWrite, FileShare.None, blockSize, true))
    {
        byte[] buffer = new byte[blockSize];
        int bytesRead;

        Console.Write($"\rDownloading {asset.Name} ");

        using ProgressBar progressBar = new();

        while ((bytesRead = await responseStream.ReadAsync(buffer)) > 0)
        {
            await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead));
            downloadedBytes += bytesRead;

            progressBar.Report((float)downloadedBytes / totalBytes);
        }

        ZipFile.ExtractToDirectory(fileStream, outputPath, true);

        CleanupUnusedBinaries(outputPath);
    }

    File.Delete(tempPath);

    string text = $"\rFile saved to {outputPath}";
    int padding = Math.Max(0, Console.WindowWidth - text.Length);
    Console.WriteLine(text + new string(' ', padding));
}


void CleanupUnusedBinaries(string path)
{
    var allFiles = Directory.GetFiles(path, "*", SearchOption.AllDirectories);
    var deleteFiles = allFiles.Where(x => !binariesToKeep.Contains(Path.GetFileName(x)));

    foreach (string file in deleteFiles)
    {
        File.Delete(file);
    }

    var keepFiles = allFiles.Where(x => binariesToKeep.Contains(Path.GetFileName(x)));

    foreach (string file in keepFiles)
    {
        string newName = binaryMappings.First(x => x.Item1 == Path.GetFileName(file)).Item2;

        string dir = Path.GetDirectoryName(file)!;
        string newPath = Path.Combine(dir, newName);

        File.Move(file, newPath);
    }
}


public class ProgressBar : IDisposable, IProgress<double>
{
    private const int blockCount = 10;
    private readonly TimeSpan animationInterval = TimeSpan.FromSeconds(1.0 / 8);
    private const string animation = @"|/-\";

    private readonly Timer timer;

    private double currentProgress = 0;
    private string currentText = string.Empty;
    private bool disposed = false;
    private int animationIndex = 0;

    public ProgressBar()
    {
        timer = new Timer(TimerHandler);

        // A progress bar is only for temporary display in a console window.
        // If the console output is redirected to a file, draw nothing.
        // Otherwise, we'll end up with a lot of garbage in the target file.
        if (!Console.IsOutputRedirected)
        {
            ResetTimer();
        }
    }

    public void Report(double value)
    {
        // Make sure value is in [0..1] range
        value = Math.Max(0, Math.Min(1, value));
        Interlocked.Exchange(ref currentProgress, value);
    }

    private void TimerHandler(object state)
    {
        lock (timer)
        {
            if (disposed) return;

            int progressBlockCount = (int)(currentProgress * blockCount);
            int percent = (int)(currentProgress * 100);
            string text = string.Format("[{0}{1}] {2,3}% {3}",
                new string('#', progressBlockCount), new string('-', blockCount - progressBlockCount),
                percent,
                animation[animationIndex++ % animation.Length]);
            UpdateText(text);

            ResetTimer();
        }
    }

    private void UpdateText(string text)
    {
        // Get length of common portion
        int commonPrefixLength = 0;
        int commonLength = Math.Min(currentText.Length, text.Length);
        while (commonPrefixLength < commonLength && text[commonPrefixLength] == currentText[commonPrefixLength])
        {
            commonPrefixLength++;
        }

        // Backtrack to the first differing character
        StringBuilder outputBuilder = new StringBuilder();
        outputBuilder.Append('\b', currentText.Length - commonPrefixLength);

        // Output new suffix
        outputBuilder.Append(text.Substring(commonPrefixLength));

        // If the new text is shorter than the old one: delete overlapping characters
        int overlapCount = currentText.Length - text.Length;
        if (overlapCount > 0)
        {
            outputBuilder.Append(' ', overlapCount);
            outputBuilder.Append('\b', overlapCount);
        }

        Console.Write(outputBuilder);
        currentText = text;
    }

    private void ResetTimer()
    {
        timer.Change(animationInterval, TimeSpan.FromMilliseconds(-1));
    }

    public void Dispose()
    {
        lock (timer)
        {
            disposed = true;
            UpdateText(string.Empty);
        }
    }
}
