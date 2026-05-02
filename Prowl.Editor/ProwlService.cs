using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Supabase;
using Supabase.Postgrest.Models;
using Supabase.Postgrest.Attributes;

using static Supabase.Postgrest.Constants;

namespace Prowl.Editor;

public static class ProwlService
{
    private static Client? s_instance;

    public static async Task Initialize()
    {
        var url = "https://skkeeysnpyaevansnnah.supabase.co";
        var key = "sb_publishable_0A-aISx-Mcyyax8l3reBog_rIh1uaNT";

        var options = new SupabaseOptions { AutoConnectRealtime = false };
        s_instance = new Client(url, key, options);
        await s_instance.InitializeAsync();
    }

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

    public static async Task<List<ProwlPackage>> FetchPackagesAsync()
    {
        if (s_instance == null)
            await Initialize();

        var pkgResponse = await s_instance!.From<ProwlPackage>()
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
public class ProwlPackage : BaseModel
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

    public override bool Equals(object? obj) => obj is ProwlPackage p && Id == p.Id;
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
