using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

using Supabase;
using Supabase.Gotrue;
using Supabase.Postgrest.Models;
using Supabase.Postgrest.Attributes;

using static Supabase.Postgrest.Constants;

using SupabaseClient = Supabase.Client;

namespace Prowl.Editor;

public static class ProwlService
{
    private static SupabaseClient? s_instance;
    private static bool s_isProduction;
    private static HttpListener? s_oauthListener;
    private static CancellationTokenSource? s_oauthCts;

    public static bool IsSigningIn { get; private set; }

    public static async Task Initialize()
    {
        s_isProduction = true; // Set to false to use local dev Supabase instance

        var url = s_isProduction ? "https://skkeeysnpyaevansnnah.supabase.co" : "http://127.0.0.1:54321";
        var key = s_isProduction ? "sb_publishable_0A-aISx-Mcyyax8l3reBog_rIh1uaNT" : "sb_publishable_ACJWlzQHlZjBrEguHvfOxg_3BJgxAaH";

        var options = new SupabaseOptions { AutoConnectRealtime = false };
        s_instance = new SupabaseClient(url, key, options);
        await s_instance.InitializeAsync();
    }

    public static async Task<Session?> SignInWithGitHubAsync()
    {
        if (s_instance == null)
            await Initialize();

        IsSigningIn = true;

        // Abort any previous sign-in attempt that never completed
        s_oauthCts?.Cancel();
        s_oauthCts?.Dispose();
        s_oauthListener?.Stop();
        s_oauthListener?.Close();

        const string redirectUri = "http://localhost:7777/auth/callback";

        s_oauthCts = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        s_oauthListener = new HttpListener();
        s_oauthListener.Prefixes.Add("http://localhost:7777/auth/callback/");
        s_oauthListener.Start();

        try
        {
            var signInState = await s_instance!.Auth.SignIn(
                Supabase.Gotrue.Constants.Provider.Github,
                new SignInOptions
                {
                    FlowType = Supabase.Gotrue.Constants.OAuthFlowType.PKCE,
                    RedirectTo = redirectUri
                }
            );

            Process.Start(new ProcessStartInfo(signInState.Uri.ToString()) { UseShellExecute = true });

            // GetContextAsync has no cancellation support — race it against the timeout
            var contextTask = s_oauthListener.GetContextAsync();
            var timeoutTask = Task.Delay(Timeout.Infinite, s_oauthCts.Token);

            if (await Task.WhenAny(contextTask, timeoutTask) != contextTask)
                return null;

            var context = await contextTask;
            var code = context.Request.QueryString["code"];

            var html = "<html><body><h2>Signed in! You can close this tab and return to Prowl.</h2></body></html>";
            var buffer = System.Text.Encoding.UTF8.GetBytes(html);
            context.Response.ContentLength64 = buffer.Length;
            await context.Response.OutputStream.WriteAsync(buffer);
            context.Response.OutputStream.Close();

            if (string.IsNullOrEmpty(code))
                return null;

            return await s_instance.Auth.ExchangeCodeForSession(signInState.PKCEVerifier, code);
        }
        finally
        {
            s_oauthListener.Stop();
            s_oauthListener.Close();
            s_oauthListener = null;
            s_oauthCts?.Dispose();
            s_oauthCts = null;
            IsSigningIn = false;
        }
    }

    public static async Task SignOutAsync()
    {
        if (s_instance?.Auth.CurrentSession != null)
            await s_instance.Auth.SignOut();
    }

    public static bool IsSignedIn => s_instance?.Auth.CurrentUser != null;
    public static User? GetCurrentUser() => s_instance?.Auth.CurrentUser;

    public static async Task<List<NewsPost>> FetchNewsPostsAsync()
    {
        if (s_instance == null)
            await Initialize();

        var response = await s_instance!.From<NewsPost>()
            .Order("published_at", Ordering.Descending, NullPosition.Last)
            .Order("created_at", Ordering.Descending)
            .Get();

        return response.Models;
    }

    public static async Task<List<MarketplacePackage>> FetchPackagesAsync()
    {
        if (s_instance == null)
            await Initialize();

        var pkgResponse = await s_instance!.From<MarketplacePackage>()
            .Order("published_at", Ordering.Descending, NullPosition.Last)
            .Get();

        var packages = pkgResponse.Models;
        if (packages.Count == 0) return packages;

        var verResponse = await s_instance.From<PackageVersion>()
            .Order("created_at", Ordering.Descending)
            .Get();

        var versionsByPackage = verResponse.Models
            .GroupBy(v => v.PackageId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(v => v.CreatedAt).ToList());

        foreach (var pkg in packages)
        {
            if (versionsByPackage.TryGetValue(pkg.Id, out var versions))
                pkg.LatestVersion = versions[0];
        }

        return packages;
    }

    public static string? GetPackagePublicUrl(string packageId, string filePath)
    {
        if (s_instance == null || string.IsNullOrEmpty(filePath)) return null;
        return s_instance.Storage.From($"package-{packageId}").GetPublicUrl(filePath);
    }
}

[Table("news")]
public class NewsPost : BaseModel
{
    [PrimaryKey("id", false)]
    public string Id { get; set; } = "";

    [Column("title")]
    public string Title { get; set; } = "";

    [Column("description")]
    public string Description { get; set; } = "";

    [Column("image_url")]
    public string ImageUrl { get; set; } = "";

    [Column("slug")]
    public string? Slug { get; set; }

    [Column("published_at")]
    public DateTime? PublishedAt { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; }

    public override bool Equals(object? obj) => obj is NewsPost post && Id == post.Id;
    public override int GetHashCode() => HashCode.Combine(Id);
}

[Table("packages")]
public class MarketplacePackage : BaseModel
{
    [PrimaryKey("id", false)]
    public string Id { get; set; } = "";

    [Column("name")]
    public string Name { get; set; } = "";

    [Column("description")]
    public string Description { get; set; } = "";

    [Column("category")]
    public string Category { get; set; } = "";

    [Column("tags")]
    public string Tags { get; set; } = "";

    [Column("published_at")]
    public DateTime? PublishedAt { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; }

    // Populated client-side after fetch
    public PackageVersion? LatestVersion { get; set; }

    public override bool Equals(object? obj) => obj is MarketplacePackage p && Id == p.Id;
    public override int GetHashCode() => HashCode.Combine(Id);
}

[Table("package_versions")]
public class PackageVersion : BaseModel
{
    [PrimaryKey("id", false)]
    public string Id { get; set; } = "";

    [Column("package_id")]
    public string PackageId { get; set; } = "";

    [Column("version")]
    public string Version { get; set; } = "";

    [Column("file_path")]
    public string FilePath { get; set; } = "";

    [Column("file_name")]
    public string FileName { get; set; } = "";

    [Column("file_size")]
    public long FileSize { get; set; }

    [Column("download_count")]
    public int DownloadCount { get; set; }

    [Column("release_notes")]
    public string ReleaseNotes { get; set; } = "";

    [Column("thumbnail_path")]
    public string ThumbnailPath { get; set; } = "";

    [Column("created_at")]
    public DateTime CreatedAt { get; set; }

    public override bool Equals(object? obj) => obj is PackageVersion v && Id == v.Id;
    public override int GetHashCode() => HashCode.Combine(Id);
}
