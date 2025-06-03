#load "ProgressBar.csx"
#r "nuget: Octokit, 14.0.0"

using System;
using System.Threading.Tasks;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Collections.Generic;

using Octokit;
using System.IO.Compression;


const string RepoOwner = "shader-slang";
const string Repo = "slang";
const string ReleaseTag = "v2025.6.4";

(string, string)?[] s_targets =
[
    ("windows-x86_64.zip", "windows-x64"),
    ("windows-aarch64.zip", "windows-arm64"),
    ("linux-x86_64.zip", "linux-x64"),
    ("linux-aarch64.zip", "linux-arm64"),
    ("macos-x86_64.zip", "macos-x64"),
    ("macos-aarch64.zip", "macos-arm64"),
];

string[] binariesToKeep = ["slang-glsl-module", "slang-glslang", "slang"];

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
    Console.WriteLine(text + new string(' ', Console.WindowWidth - text.Length));
}


void CleanupUnusedBinaries(string path)
{
    string[] extensions = [".dll", ".so", ".dylib"]; // Change to your desired extension

    var matchingFiles = new List<string>();

    foreach (var ext in extensions)
    {
        var files = Directory.GetFiles(path, "*" + ext, SearchOption.AllDirectories);
        matchingFiles.AddRange(files);
    }

    foreach (string file in matchingFiles)
    {
        Console.WriteLine("Checking file: " + file);

        string filename = Path.GetFileNameWithoutExtension(file);

        if (filename.StartsWith("lib"))
            filename = filename.Substring(3);

        if (!binariesToKeep.Any(x => filename.Equals(x)))
            File.Delete(file);
    }
}
