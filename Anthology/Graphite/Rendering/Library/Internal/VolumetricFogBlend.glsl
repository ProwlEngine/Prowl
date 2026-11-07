// The volumetric fog's light blended over last frame's, one cell of the view's grid per invocation. The cell's centre is
// found in last frame's grid and its light read there filtered. Kept for next frame and integrated by
// VolumetricFogIntegrate.glsl.
Compute
{
    #pragma resource_set 3
    #define GRAPHITE_NO_OBJECT 1

    layout(local_size_x = 4, local_size_y = 4, local_size_z = 4) in;

    #include "Graphite/VolumetricFog.glsl"

    layout(rgba16f) uniform readonly image3D Fog_Raw;
    layout(rgba16f) uniform writeonly image3D Fog_Light;
    uniform sampler3D Fog_History;
    uniform vec4 Fog_Grid;      // cells across and down, the layer's first slice in the grid, how much of the history to keep

    void main()
    {
        ivec3 id = ivec3(gl_GlobalInvocationID);
        int slices = int(graphite_World.volumetricGrid.z);
        ivec2 cells = ivec2(Fog_Grid.xy);
        if (id.x >= cells.x || id.y >= cells.y || id.z >= slices)
            return;
        int first = int(Fog_Grid.z);
        ivec3 at = ivec3(id.xy, first + id.z);
        vec4 raw = imageLoad(Fog_Raw, at);
        vec4 light = raw;
        if (Fog_Grid.w > 0.0)
        {
            Graphite_ViewData view = Graphite_View();
            vec3 ray = normalize(Graphite_RelativeFromDepth(view, (vec2(id.xy) + 0.5) * 8.0 * view.screen.zw, 1.0));
            vec3 moved = (view.cameraHigh.xyz - view.previousCameraHigh.xyz) + (view.cameraLow.xyz - view.previousCameraLow.xyz);
            vec3 previous = ray * Graphite_FogSliceDistance(float(id.z) + 0.5) + moved;
            vec4 clip = view.previousViewProjection * vec4(previous, 1.0);
            vec2 cell = vec2(clip.x / clip.w * 0.5 + 0.5, 0.5 - clip.y / clip.w * 0.5) * view.screen.xy / 8.0;
            float slice = Graphite_FogSlice(length(previous));
            if (clip.w > 0.0 && slice >= 0.0 && slice < float(slices) && all(greaterThanEqual(cell, vec2(0.0))) && all(lessThan(cell, vec2(cells))))
            {
                vec3 uvw = vec3(cell, float(first) + clamp(slice, 0.5, float(slices) - 0.5)) / vec3(textureSize(Fog_History, 0));
                light = mix(raw, texture(Fog_History, uvw), Fog_Grid.w);
            }
        }
        imageStore(Fog_Light, at, light);
    }
}
