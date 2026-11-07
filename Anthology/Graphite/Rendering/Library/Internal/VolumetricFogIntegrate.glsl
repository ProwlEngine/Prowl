// The volumetric fog's light accumulated toward the camera, one column of the view's grid per invocation, front to back
// through its slices: each cell's light per metre is integrated over the slice's thickness against how much the fog in
// the slice lets through, dimmed by the fog in front. Each cell of the volume gets the light gathered and what is let
// through from the camera to its slice's far side.
Compute
{
    #pragma resource_set 3
    #define GRAPHITE_NO_OBJECT 1

    layout(local_size_x = 8, local_size_y = 8) in;

    #include "Graphite/VolumetricFog.glsl"

    layout(rgba16f) uniform readonly image3D Fog_Light;
    layout(rgba16f) uniform writeonly image3D Fog_Volume;
    uniform vec4 Fog_Grid;      // cells across and down, the layer's first slice in the grid

    void main()
    {
        ivec2 id = ivec2(gl_GlobalInvocationID.xy);
        if (id.x >= int(Fog_Grid.x) || id.y >= int(Fog_Grid.y))
            return;
        int first = int(Fog_Grid.z), slices = int(graphite_World.volumetricGrid.z);

        vec3 gathered = vec3(0.0);
        float through = 1.0;
        float near = Graphite_FogSliceDistance(0.0);
        for (int s = 0; s < slices; s++)
        {
            ivec3 at = ivec3(id, first + s);
            vec4 light = imageLoad(Fog_Light, at);
            float far = Graphite_FogSliceDistance(float(s + 1));
            float thickness = far - near;
            near = far;
            float slice = exp(-light.a * thickness);
            vec3 scattered = light.a > 1e-6 ? light.rgb * (1.0 - slice) / light.a : light.rgb * thickness;
            gathered += through * scattered;
            through *= slice;
            imageStore(Fog_Volume, at, vec4(gathered, through));
        }
    }
}
