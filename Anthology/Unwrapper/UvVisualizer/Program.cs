using Prowl.Clay.Importer;
using Prowl.Unwrapper;
using Prowl.Unwrapper.Visualizer;
using Prowl.Vector;

Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true });

const string ModelPath = @"C:\Users\acer1\Desktop\New folder\glTF-Sample-Models-main\2.0\Sponza\glTF\Sponza.gltf";
const string OutputPath = "uv_atlas.png";
const int ImageSize = 16384;

if (!File.Exists(ModelPath)) { Console.WriteLine($"Model not found: {ModelPath}"); return 1; }

Console.WriteLine($"Loading {ModelPath}...");
var loadSw = System.Diagnostics.Stopwatch.StartNew();
var model = ModelImporter.Load(ModelPath);
Console.WriteLine($"  loaded in {loadSw.ElapsedMilliseconds} ms — {model.Meshes.Count} meshes, {model.Materials.Count} materials");

var allVerts = new List<Double3>();
var allNormals = new List<Double3>();
var allTris = new List<int>();

foreach (var mesh in model.Meshes)
{
    int vertexBase = allVerts.Count;

    var meshNormals = mesh.Normals;
    for (int v = 0; v < mesh.Vertices.Length; ++v)
    {
        allVerts.Add(new Double3(mesh.Vertices[v].X, mesh.Vertices[v].Y, mesh.Vertices[v].Z));
        if (meshNormals is not null)
            allNormals.Add(new Double3(meshNormals[v].X, meshNormals[v].Y, meshNormals[v].Z));
        else
            allNormals.Add(default);
    }

    foreach (var sub in mesh.SubMeshes)
    {
        for (int i = 0; i < sub.IndexCount; i += 3)
        {
            allTris.Add(vertexBase + (int)mesh.Indices[sub.IndexStart + i + 0]);
            allTris.Add(vertexBase + (int)mesh.Indices[sub.IndexStart + i + 1]);
            allTris.Add(vertexBase + (int)mesh.Indices[sub.IndexStart + i + 2]);
        }
    }
}

bool allHaveNormals = model.Meshes.All(m => m.Normals is not null);
Double3[]? combinedNormals = allHaveNormals ? allNormals.ToArray() : null;
Console.WriteLine($"Combined: {allVerts.Count} verts, {allTris.Count / 3} tris (normals: {(allHaveNormals ? "yes" : "no")})");
if (allVerts.Count == 0 || allTris.Count == 0)
{
    Console.WriteLine("Nothing to unwrap.");
    return 1;
}

Console.WriteLine("Unwrapping...");
var stopwatch = System.Diagnostics.Stopwatch.StartNew();
var unwrapBuilder = new UnwrapMesh(allVerts.ToArray(), allTris.ToArray())
    .WithProgress(s => Console.WriteLine($"    {s}"));
if (combinedNormals is not null) unwrapBuilder.WithNormals(combinedNormals);
var result = unwrapBuilder.Unwrap();
stopwatch.Stop();
Console.WriteLine($"  Done in {stopwatch.ElapsedMilliseconds} ms");

int triangleCount = allTris.Count / 3;
int dropped = result.DegenerateTriangleIndices?.Length ?? 0;
Console.WriteLine($"  Got {result.PerCornerUVs.Length} per-corner UVs, {dropped} degenerate triangles dropped");

// Bounds check (also useful for spotting NaN output).
Double2 boundsMin = new(double.PositiveInfinity, double.PositiveInfinity);
Double2 boundsMax = new(double.NegativeInfinity, double.NegativeInfinity);
int nonZero = 0;
foreach (var uv in result.PerCornerUVs)
{
    if (uv.X == 0.0 && uv.Y == 0.0) continue;
    ++nonZero;
    if (uv.X < boundsMin.X) boundsMin.X = uv.X;
    if (uv.Y < boundsMin.Y) boundsMin.Y = uv.Y;
    if (uv.X > boundsMax.X) boundsMax.X = uv.X;
    if (uv.Y > boundsMax.Y) boundsMax.Y = uv.Y;
}
Console.WriteLine($"  Non-zero UVs: {nonZero}; bounds: ({boundsMin.X:F4},{boundsMin.Y:F4}) -> ({boundsMax.X:F4},{boundsMax.Y:F4})");

// Render: white background, black antialiased triangle wireframes. Parallel: each thread
// processes a contiguous slice of triangles and writes directly into the shared canvas.
// Two threads can race on the same pixel; Plot's min-blend keeps the result mostly correct
// (a race may leave a pixel one increment lighter than it should be — invisible in practice).
var renderSw = System.Diagnostics.Stopwatch.StartNew();
var canvas = new Canvas(ImageSize, ImageSize);
canvas.Clear(255, 255, 255);
Console.WriteLine($"Allocated + cleared {ImageSize}x{ImageSize} canvas in {renderSw.ElapsedMilliseconds} ms");

renderSw.Restart();
int drawn = 0;
Parallel.For(0, triangleCount, t =>
{
    Double2 a = result.PerCornerUVs[3 * t + 0];
    Double2 b = result.PerCornerUVs[3 * t + 1];
    Double2 c = result.PerCornerUVs[3 * t + 2];

    // Skip the degenerate-triangle holes (their UVs are all zero).
    if (a.X == 0 && a.Y == 0 && b.X == 0 && b.Y == 0 && c.X == 0 && c.Y == 0) return;

    double ax = ToPixelF(a.X, ImageSize);
    double ay = ToPixelF(1.0 - a.Y, ImageSize); // flip Y so the image reads top-down
    double bx = ToPixelF(b.X, ImageSize);
    double by = ToPixelF(1.0 - b.Y, ImageSize);
    double cx = ToPixelF(c.X, ImageSize);
    double cy = ToPixelF(1.0 - c.Y, ImageSize);

    canvas.DrawLineAA(ax, ay, bx, by, 0, 0, 0);
    canvas.DrawLineAA(bx, by, cx, cy, 0, 0, 0);
    canvas.DrawLineAA(cx, cy, ax, ay, 0, 0, 0);
    System.Threading.Interlocked.Increment(ref drawn);
});

Console.WriteLine($"Drew {drawn} triangle wireframes in {renderSw.ElapsedMilliseconds} ms");

renderSw.Restart();
PngWriter.WriteRgb(OutputPath, canvas.Pixels, ImageSize, ImageSize);
Console.WriteLine($"Encoded PNG in {renderSw.ElapsedMilliseconds} ms");
Console.WriteLine($"Saved {Path.GetFullPath(OutputPath)}");

return 0;

static double ToPixelF(double u, int size) => System.Math.Clamp(u, 0.0, 1.0) * (size - 1);
