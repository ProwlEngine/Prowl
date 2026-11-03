// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections.Generic;

using Prowl.Graphite;
using Prowl.Graphite.Rendering;
using Prowl.Runtime;

namespace Prowl.Editor;

/// <summary>
/// The editor's ground grid: the plane y = 0 traced per pixel over the finished image, hidden behind geometry, with the
/// X and Z axes drawn in red and blue and a fade into the distance.
/// </summary>
internal sealed class GridFeature : RenderFeature
{
    private const string Fragment = """
        #include "Graphite/Internal/PostView.glsl"

        uniform GRAPHITE_SCREEN_TEXTURE _CameraDepthTexture;
        uniform vec4 _GridColor;
        uniform float _PrimaryGridSize;
        uniform float _SecondaryGridSize;
        uniform float _LineWidth;
        uniform float _Falloff;
        uniform float _MaxDist;

        // Anti aliased grid lines from screen space derivatives
        float pristineGrid(vec2 uv, vec2 lineWidth)
        {
            lineWidth = clamp(lineWidth, vec2(0.0), vec2(0.5));
            vec4 uvDDXY = vec4(dFdx(uv), dFdy(uv));
            vec2 uvDeriv = vec2(length(uvDDXY.xz), length(uvDDXY.yw));
            vec2 drawWidth = clamp(lineWidth, uvDeriv, vec2(0.5));
            vec2 lineAA = max(uvDeriv, vec2(0.000001)) * 1.5;
            vec2 gridUV = 1.0 - abs(fract(uv) * 2.0 - 1.0);
            vec2 grid2 = smoothstep(drawWidth + lineAA, drawWidth - lineAA, gridUV);
            grid2 *= clamp(lineWidth / drawWidth, vec2(0.0), vec2(1.0));
            grid2 = mix(grid2, lineWidth, clamp(uvDeriv * 2.0 - 1.0, vec2(0.0), vec2(1.0)));
            return mix(grid2.x, 1.0, grid2.y);
        }

        void main()
        {
            vec4 source = Graphite_SampleSource(vUV);
            Graphite_ViewData view = Graphite_PostView();
            vec3 camera = view.cameraHigh.xyz + view.cameraLow.xyz;

            vec3 nearPoint = Graphite_RelativeFromDepth(view, vUV, 1.0);
            vec3 farPoint = Graphite_RelativeFromDepth(view, vUV, 0.5);
            vec3 direction = farPoint - nearPoint;
            float t = abs(direction.y) > 1e-6 ? (-camera.y - nearPoint.y) / direction.y : -1.0;
            vec3 hit = nearPoint + direction * t;
            vec2 uv = hit.xz + camera.xz;

            float sceneDepth = Graphite_Sample(_CameraDepthTexture, vUV).r;
            vec3 scene = Graphite_RelativeFromDepth(view, vUV, max(sceneDepth, 1e-7));
            bool hidden = t <= 0.0 || (sceneDepth > 0.0 && dot(hit, hit) > dot(scene, scene) * 0.99);

            float sg = pristineGrid(uv * _PrimaryGridSize, vec2(_LineWidth));
            float bg = pristineGrid(uv * _SecondaryGridSize, vec2(_LineWidth));
            float gridAlpha = max(sg, bg * 0.5);
            vec3 color = _GridColor.rgb;

            float dzPerPx = length(vec2(dFdx(uv.y), dFdy(uv.y)));
            float dxPerPx = length(vec2(dFdx(uv.x), dFdy(uv.x)));
            float xAxis = 1.0 - smoothstep(0.0, dzPerPx * 1.5, abs(uv.y));
            float zAxis = 1.0 - smoothstep(0.0, dxPerPx * 1.5, abs(uv.x));
            color = mix(color, vec3(0.9, 0.2, 0.2), xAxis);
            gridAlpha = max(gridAlpha, xAxis * 0.9);
            color = mix(color, vec3(0.2, 0.4, 0.9), zAxis);
            gridAlpha = max(gridAlpha, zAxis * 0.9);

            float fade = 1.0 - pow(clamp(length(hit) / _MaxDist, 0.0, 1.0), _Falloff);
            float alpha = hidden ? 0.0 : clamp(gridAlpha * _GridColor.a * fade, 0.0, 1.0);
            outColor = vec4(mix(source.rgb, color, alpha), source.a);
        }
        """;

    private static GridFeature? s_feature;
    private static Renderer? s_renderer;
    private static readonly List<RenderFeature> s_list = [];

    /// <summary>The grid as the extra features of a camera render, made for the current renderer.</summary>
    public static IReadOnlyList<RenderFeature>? List
    {
        get
        {
            if (Graphics.Renderer == null) return null;
            if (s_feature == null || s_renderer != Graphics.Renderer)
            {
                s_renderer = Graphics.Renderer;
                s_feature = new GridFeature(s_renderer);
                s_list.Clear();
                s_list.Add(s_feature);
            }
            return s_list;
        }
    }

    private readonly FullscreenProgram _program;
    private readonly PropertySet _properties = new();
    private readonly Sampler _point;

    private GridFeature(Renderer renderer) : base(RenderSlot.Overlay, runsOn: ViewKinds.All)
    {
        _program = renderer.CreateFullscreenProgram(Fragment, "Editor Grid");
        _point = Graphics.GetSampler(SamplerDescription.Point);
    }

    public override void Setup(FeatureBuilder builder, in FeatureView view)
    {
        builder.ReadScene();
        builder.WriteColor();
    }

    public override void Render(FeatureContext context)
    {
        _properties.SetTexture("_CameraDepthTexture", context.Depth!, _point);
        _properties.SetFloat4("_GridColor", new Prowl.Vector.Float4(0.8f, 0.8f, 0.8f, 0.65f));
        _properties.SetFloat("_PrimaryGridSize", 1f);
        _properties.SetFloat("_SecondaryGridSize", 0.25f);
        _properties.SetFloat("_LineWidth", 0.02f);
        _properties.SetFloat("_Falloff", 1.5f);
        _properties.SetFloat("_MaxDist", 500f);
        context.Fullscreen(_program, _properties);
    }
}
