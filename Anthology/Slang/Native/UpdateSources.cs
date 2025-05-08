using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;

using Octokit;
using System.IO.Compression;


static class Build
{
    public const string RepoOwner = "shader-slang";
    public const string Repo = "slang";
    public const string ReleaseTag = "v2025.6.4";


    static readonly (string, string)?[] s_targets =
    [
        ("windows-x86_64.zip", "windows-x64"),
        ("windows-aarch64.zip", "windows-arm64"),
        ("linux-x86_64.zip", "linux-x64"),
        ("linux-aarch64.zip", "linux-arm64"),
        ("macos-x86_64.zip", "macos-x64"),
        ("macos-aarch64.zip", "macos-arm64"),
    ];

    private static string s_targetPath;


    static async Task Main()
    {
        s_targetPath = Path.Join(Directory.GetCurrentDirectory(), "lib");

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
    }

    private static async Task DownloadRelease(ReleaseAsset asset, string targetPathName, int ID)
    {
        string outputPath = Path.Join(s_targetPath, targetPathName);
        string tempPath = outputPath + ".temp.zip";

        using HttpClient client = new();
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("UpdateSources", "1.0"));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/octet-stream"));

        using HttpResponseMessage response = await client.GetAsync(asset.Url, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();

        long totalBytes = response.Content.Headers.ContentLength ?? 0;
        long downloadedBytes = 0L;

        const int blockSize = 1024;

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

                progressBar.Report(downloadedBytes / totalBytes);
            }

            progressBar.Report(1);

            ZipFile.ExtractToDirectory(fileStream, outputPath, true);
        }

        File.Delete(tempPath);

        string text = $"\rFile saved to {outputPath}";
        Console.WriteLine(text + new string(' ', Console.WindowWidth - text.Length));
    }
}
