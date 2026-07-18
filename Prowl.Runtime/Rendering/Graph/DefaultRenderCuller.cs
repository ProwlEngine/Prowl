// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Graphite;

using Prowl.Runtime.Resources;
using Prowl.Vector;

namespace Prowl.Runtime.Rendering;

/// <summary>
/// The default pipeline's culler. Frustum/layer culls the scene once per camera (via
/// <see cref="RenderCullingUtility"/>), then answers each pass's <see cref="DrawCommandQuery"/> by
/// filtering the survivors down to the renderables whose material has a pass matching the query tag,
/// sorting them, and emitting one <see cref="DrawCommand"/> per draw.
/// </summary>
public sealed class DefaultRenderCuller : IRenderCuller<DrawCommand>
{
    private readonly RenderCullingUtility _culling = new();

    private IReadOnlyList<IRenderable> _renderables = Array.Empty<IRenderable>();
    private bool[] _culled = Array.Empty<bool>();
    private RenderPipeline.CameraSnapshot _camera;
    private ViewerData _viewer;

    private readonly List<DrawCommand> _commands = new();
    private bool[] _queryMask = Array.Empty<bool>();
    private int[] _queryPass = Array.Empty<int>();

    public void Cull(in RenderPipeline.CameraSnapshot camera, IReadOnlyList<IRenderable> renderables, IReadOnlyList<IRenderableLight> lights)
    {
        _camera = camera;
        _viewer = new ViewerData(camera);
        _renderables = renderables ?? (IReadOnlyList<IRenderable>)Array.Empty<IRenderable>();
        _culled = _culling.ComputeCullMask(_renderables, camera.WorldFrustum, camera.CullingMask);
    }

    public IReadOnlyList<DrawCommand> GetDrawCommands(in DrawCommandQuery query)
    {
        _commands.Clear();

        int count = _renderables.Count;
        if (count == 0)
            return _commands;

        if (_queryMask.Length < count)
        {
            _queryMask = new bool[count];
            _queryPass = new int[count];
        }

        for (int i = 0; i < count; i++)
            _queryMask[i] = _culled[i] || !Matches(_renderables[i], query, out _queryPass[i]);

        List<int> sorted = _culling.SortIndices(_renderables, _queryMask, _camera.CameraPosition, query.Sort);

        for (int s = 0; s < sorted.Count; s++)
        {
            int index = sorted[s];
            IRenderable renderable = _renderables[index];
            renderable.GetRenderingData(_viewer, out PropertySet properties, out Mesh mesh, out Float4x4 model, out _);

            _commands.Add(new DrawCommand
            {
                Mesh = mesh,
                Material = renderable.GetMaterial(),
                Model = model,
                Layer = renderable.GetLayer(),
                Properties = properties,
                PassIndex = _queryPass[index],
            });
        }

        return _commands;
    }

    /// <summary>
    /// True when this renderable should be drawn for <paramref name="query"/>: it passes the optional
    /// layer filter and its material has a shader pass carrying the query tag. Outputs the index of
    /// the matched pass (0 when the query has no tag).
    /// </summary>
    private static bool Matches(IRenderable renderable, in DrawCommandQuery query, out int passIndex)
    {
        passIndex = 0;

        if (query.LayerMask.HasValue && !query.LayerMask.Value.HasLayer(renderable.GetLayer()))
            return false;

        Shader? shader = renderable.GetMaterial()?.Shader;
        if (shader == null)
            return false;

        if (string.IsNullOrEmpty(query.Tag))
            return true;

        int? found = shader.GetPassWithTag(query.Tag, query.TagValue);
        if (found == null)
            return false;

        passIndex = found.Value;
        return true;
    }
}
