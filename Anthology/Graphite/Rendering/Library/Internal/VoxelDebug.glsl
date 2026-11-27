// The world's voxel volume traced from the camera for each pixel: albedo shaded by the face it hit, emission as stored,
// face normals, the cells the trace stepped through, or the light the hit sends back at the view's exposure with a
// simple tonemap, direct and bounced or bounced alone. Pixels whose ray misses show the scene darkened, or the sky when
// showing light.
#include "Graphite/Internal/PostView.glsl"
#include "Graphite/VoxelCache.glsl"

uniform vec4 _BoxMin;       // the box's first corner relative to the first layer's camera in metres, the voxel size
uniform int _Mode;

void main()
{
    Graphite_ViewData view = Graphite_PostView();
    vec3 near = Graphite_RelativeFromDepth(view, vUV, 1.0);
    vec3 direction = normalize(Graphite_RelativeFromDepth(view, vUV, 0.5) - near);
    vec3 origin = (near - (_BoxMin.xyz - view.treeOffset.xyz)) / _BoxMin.w;

    Graphite_VoxelHit hit = Graphite_TraceVoxels(origin, direction, 1e6, 512u);
    if (_Mode == 2)
    {
        float heat = float(hit.steps) / 64.0;
        outColor = vec4(heat, heat * heat, hit.hit ? 0.25 : 0.0, 1.0);
        return;
    }
    if (_Mode == 3 || _Mode == 4)
    {
        vec3 light = !hit.hit ? (_Mode == 3 ? Graphite_VoxelSkyRadiance(direction) : vec3(0.0))
            : _Mode == 3 ? Graphite_VoxelHitLight(hit, origin, direction, true)
            : Graphite_VoxelHitLight(hit, origin, direction, true) - Graphite_VoxelHitRadiance(hit, origin, direction);
        light = Graphite_VoxelToView(light, view.exposure.x);
        outColor = vec4(light / (1.0 + light), 1.0);
        return;
    }
    if (!hit.hit)
    {
        outColor = vec4(Graphite_SampleSource(vUV).rgb * 0.25, 1.0);
        return;
    }
    if (_Mode == 1)
    {
        outColor = vec4(hit.normal * 0.5 + 0.5, 1.0);
        return;
    }
    uint word = Graphite_VoxelColorWord(hit.voxel);
    vec3 color = Graphite_VoxelIsEmissive(word) ? graphite_VoxelChannels(word) : Graphite_VoxelAlbedo(word) * (0.55 + 0.45 * max(dot(hit.normal, -direction), 0.0));
    outColor = vec4(color, 1.0);
}
