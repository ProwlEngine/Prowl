namespace Prowl.Clay.Tests;

/// <summary>
/// Centralized accessor for the <c>test-models/</c> fixtures committed alongside the repo. All
/// test classes resolve paths through here so the search logic lives in one place. Models are
/// committed and CI builds run with them present; if a path resolves to nothing the test fails
/// loudly rather than being silently skipped.
/// </summary>
internal static class TestModels
{
    public static string GltfRoot { get; } = Resolve("test-models", "gltf");
    public static string FbxRoot  { get; } = Resolve("test-models", "fbx");
    public static string ObjRoot  { get; } = Resolve("test-models", "obj");

    public static string Gltf(string relative) => Path.Combine(GltfRoot, relative);
    public static string Fbx(string relative)  => Path.Combine(FbxRoot,  relative);
    public static string Obj(string relative)  => Path.Combine(ObjRoot,  relative);

    private static string Resolve(params string[] segments)
    {
        string? dir = AppContext.BaseDirectory;
        for (int hops = 0; hops < 10 && dir is not null; hops++)
        {
            string candidate = Path.Combine(new[] { dir }.Concat(segments).ToArray());
            if (Directory.Exists(candidate)) return candidate;
            dir = Path.GetDirectoryName(dir);
        }
        throw new DirectoryNotFoundException(
            $"Could not locate '{Path.Combine(segments)}' walking up from {AppContext.BaseDirectory}. " +
            "Ensure the repo's test-models/ folder is committed and present.");
    }
}
