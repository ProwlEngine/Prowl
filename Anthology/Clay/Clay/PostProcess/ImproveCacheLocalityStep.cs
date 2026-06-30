using Prowl.Clay.Importer;
using Prowl.Clay.Internal.Intermediate;

namespace Prowl.Clay.PostProcess;

/// <summary>
/// Reorders triangle indices for better post-transform vertex-cache reuse, using a port of
/// "Tipsify" (Sander, Nehab, Barczak 2007).
/// </summary>
/// <remarks>
/// Operates only on triangle faces; lines and points are left alone. Vertex order is not changed,
/// so this step is safe to run after <see cref="JoinIdenticalVerticesStep"/> without invalidating
/// references in other steps.
/// </remarks>
internal sealed class ImproveCacheLocalityStep : IPostProcess
{
    public PostProcessFlags Flag => PostProcessFlags.ImproveCacheLocality;
    public string Name => "ImproveCacheLocality";

    /// <summary>FIFO cache size used to score vertex reuse (typical GPU post-T&amp;L cache size).</summary>
    private const int CacheSize = 32;

    public void Execute(IntermediateScene scene, ImportContext context)
    {
        foreach (var mesh in scene.Meshes)
            OptimizeOne(mesh);
        _ = context;
    }

    private static void OptimizeOne(IntermediateMesh mesh)
    {
        // Collect triangle faces and keep non-triangle ones aside.
        var triIndex = new List<int>();
        for (int i = 0; i < mesh.Faces.Count; i++)
            if (mesh.Faces[i].Indices.Length == 3)
                triIndex.Add(i);
        if (triIndex.Count <= 1) return;

        int vertexCount = mesh.Positions.Count;
        int triCount = triIndex.Count;

        int[] triVerts = new int[triCount * 3];
        for (int i = 0; i < triCount; i++)
        {
            var face = mesh.Faces[triIndex[i]].Indices;
            triVerts[i * 3 + 0] = face[0];
            triVerts[i * 3 + 1] = face[1];
            triVerts[i * 3 + 2] = face[2];
        }

        // Build per-vertex triangle adjacency.
        int[] valence = new int[vertexCount];
        for (int t = 0; t < triCount * 3; t++)
            valence[triVerts[t]]++;

        int[] offsets = new int[vertexCount + 1];
        for (int v = 0; v < vertexCount; v++)
            offsets[v + 1] = offsets[v] + valence[v];

        int[] cursor = new int[vertexCount];
        int[] adjTri = new int[triCount * 3];
        for (int t = 0; t < triCount; t++)
        {
            int v0 = triVerts[t * 3 + 0];
            int v1 = triVerts[t * 3 + 1];
            int v2 = triVerts[t * 3 + 2];
            adjTri[offsets[v0] + cursor[v0]++] = t;
            adjTri[offsets[v1] + cursor[v1]++] = t;
            adjTri[offsets[v2] + cursor[v2]++] = t;
        }

        // Tipsify state.
        int[] liveTris = new int[vertexCount];
        Array.Copy(valence, liveTris, vertexCount);

        int[] cacheTime = new int[vertexCount];
        Array.Fill(cacheTime, -1);

        bool[] triEmitted = new bool[triCount];

        int[] outIndices = new int[triCount * 3];
        int outCursor = 0;
        int timeStamp = CacheSize + 1;
        int next = 0;

        int fanningVertex = -1;

        for (int writeTri = 0; writeTri < triCount; writeTri++)
        {
            if (fanningVertex < 0)
            {
                // Skip-back: find the next un-emitted triangle starting at `next`.
                while (next < triCount && triEmitted[next]) next++;
                if (next >= triCount) break;
                int t = next++;
                EmitTri(t, triVerts, outIndices, ref outCursor, triEmitted, liveTris, cacheTime, ref timeStamp);
                fanningVertex = NextFan(triVerts, t, liveTris, cacheTime, timeStamp);
            }
            else
            {
                int bestTri = -1;
                int bestPriority = int.MinValue;
                int start = offsets[fanningVertex];
                int end = offsets[fanningVertex + 1];
                for (int i = start; i < end; i++)
                {
                    int t = adjTri[i];
                    if (triEmitted[t]) continue;
                    int priority = TriScore(t, triVerts, cacheTime, timeStamp, liveTris);
                    if (priority > bestPriority)
                    {
                        bestPriority = priority;
                        bestTri = t;
                    }
                }
                if (bestTri < 0)
                {
                    fanningVertex = -1;
                    writeTri--;
                    continue;
                }
                EmitTri(bestTri, triVerts, outIndices, ref outCursor, triEmitted, liveTris, cacheTime, ref timeStamp);
                fanningVertex = NextFan(triVerts, bestTri, liveTris, cacheTime, timeStamp);
            }
        }

        // Write reordered triangles back into the mesh's face list (preserve non-triangle faces).
        var newFaces = new List<IntermediateFace>(mesh.Faces.Count);
        int triRead = 0;
        for (int i = 0; i < mesh.Faces.Count; i++)
        {
            var face = mesh.Faces[i];
            if (face.Indices.Length == 3)
            {
                if (triRead < triCount)
                {
                    newFaces.Add(new IntermediateFace(new[]
                    {
                        outIndices[triRead * 3 + 0],
                        outIndices[triRead * 3 + 1],
                        outIndices[triRead * 3 + 2],
                    }));
                    triRead++;
                }
            }
            else
            {
                newFaces.Add(face);
            }
        }
        mesh.Faces.Clear();
        mesh.Faces.AddRange(newFaces);
    }

    private static void EmitTri(
        int t,
        int[] triVerts,
        int[] outIndices,
        ref int outCursor,
        bool[] triEmitted,
        int[] liveTris,
        int[] cacheTime,
        ref int timeStamp)
    {
        triEmitted[t] = true;
        for (int k = 0; k < 3; k++)
        {
            int v = triVerts[t * 3 + k];
            outIndices[outCursor++] = v;
            liveTris[v]--;
            if (timeStamp - cacheTime[v] > CacheSize)
                cacheTime[v] = timeStamp++;
        }
    }

    private static int TriScore(int t, int[] triVerts, int[] cacheTime, int timeStamp, int[] liveTris)
    {
        int sum = 0;
        for (int k = 0; k < 3; k++)
        {
            int v = triVerts[t * 3 + k];
            int age = timeStamp - cacheTime[v];
            int s = age < CacheSize ? 750 - 10 * age : 0;
            // Bonus for vertices that still have many triangles waiting to be emitted: prefer to
            // peel off frequently-shared vertices first.
            s += 200 - 10 * Math.Min(liveTris[v], 19);
            sum += s;
        }
        return sum;
    }

    private static int NextFan(int[] triVerts, int t, int[] liveTris, int[] cacheTime, int timeStamp)
    {
        // Choose the vertex of t with the most remaining live triangles and the freshest cache time
        // as the fan center for the next iteration.
        int best = -1;
        int bestScore = int.MinValue;
        for (int k = 0; k < 3; k++)
        {
            int v = triVerts[t * 3 + k];
            if (liveTris[v] <= 0) continue;
            int score = liveTris[v] * 100 - (timeStamp - cacheTime[v]);
            if (score > bestScore)
            {
                bestScore = score;
                best = v;
            }
        }
        return best;
    }
}
