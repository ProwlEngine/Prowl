// The volumetric fog's light, one cell of the view's grid per invocation: the fog's density at a point jittered through
// the cell each frame, and the light it scatters toward the camera per metre there, from every directional light through
// its shadow cascades, every local light through its shadow, cookie and profile, and the ambient light. Light is
// pre-exposed. Written as light per metre and density, which VolumetricFogBlend.glsl blends over last frame's.
Compute
{
    #pragma resource_set 3
    #define GRAPHITE_NO_OBJECT 1
    #define GRAPHITE_LIGHT_MASKS 1
    #define GRAPHITE_HARD_SHADOWS 1

    layout(local_size_x = 4, local_size_y = 4, local_size_z = 4) in;

    #include "Graphite/Lighting.glsl"
    #include "Graphite/VolumetricFog.glsl"

    layout(rgba16f) uniform writeonly image3D Fog_Raw;
    uniform vec4 Fog_Grid;      // cells across and down, the layer's first slice in the grid
    uniform vec4 Fog_Jitter;    // this frame's point in its cell, 0 to 1 on each axis

    // Henyey Greenstein, cosTheta between the way the camera looks and the way to the light
    float Fog_Phase(float cosTheta, float g)
    {
        float g2 = g * g;
        return (1.0 - g2) / (4.0 * GRAPHITE_PI * pow(max(1.0 + g2 - 2.0 * g * cosTheta, 1e-4), 1.5));
    }

    // The light reaching a point that the fog there scatters toward the camera, per unit of scattering, pre-exposed.
    // Local lights fall off over the cell's size too, the cell's average rather than the spike at its point.
    vec3 Fog_InScattered(vec3 p, vec3 ray, float cell)
    {
        float g = graphite_World.volumetricDensity.w;
        float exposure = Graphite_Exposure();
        vec3 total = vec3(0.0);
        uint count = Graphite_DirectionalCount();
        for (uint i = 0u; i < count; i++)
        {
            Graphite_DirectionalLight d = Graphite_GetDirectional(i);
            // The shadow maps alone, the fog leaves the clouds' shadow out
            float shadow = d.shadow ? Graphite_DirectionalShadow(i, d.direction, d.shadowDepthBias, d.shadowNormalBias, d.shadowStrength, d.shadowFilter, 0.0, p, d.direction) : 1.0;
            total += d.color * (exposure * shadow * Fog_Phase(dot(ray, d.direction), g));
        }

        Graphite_LightIterator it = Graphite_BeginLights(p);
        Graphite_LightSample L;
        while (Graphite_NextLight(it, L))
        {
            bool spot = L.type == GRAPHITE_LIGHT_SPOT;
            if (L.type != GRAPHITE_LIGHT_POINT && !spot)
                continue;
            vec3 d = L.position - p;
            float dist2 = dot(d, d);
            vec3 toLight = d * inversesqrt(max(dist2, 1e-8));
            float attenuation = Graphite_Window(dist2, L.range) / max(dist2 + cell * cell, 0.01);
            if (spot)
            {
                // The cone's edges widened by the angle the cell covers seen from the light, keeping its light
                vec2 spotCos = Graphite_LightSpotCos(L);
                float outer = acos(spotCos.x), inner = acos(spotCos.y);
                float blur = min(cell * inversesqrt(max(dist2, 1e-8)), 1.0);
                float wideOuter = outer + blur + 1e-4, wideInner = max(inner - blur, 0.0);
                float spread = (outer + inner) / (wideOuter + wideInner);
                float angle = acos(clamp(dot(Graphite_LightDirection(L), -toLight), -1.0, 1.0));
                attenuation *= (1.0 - smoothstep(wideInner, wideOuter, angle)) * spread * spread;
            }
            if (attenuation <= 0.0)
                continue;
            attenuation *= Graphite_LocalLightShadow(L, p, toLight);
            total += Graphite_LightRadiance(L) * Graphite_LightMask(L, -toLight) * (exposure * attenuation * Fog_Phase(dot(ray, toLight), g));
        }

        vec3 ambient = 0.5 * (Graphite_AmbientLight(vec3(0.0, 1.0, 0.0)) + Graphite_AmbientLight(vec3(0.0, -1.0, 0.0)));
        return total + ambient * graphite_World.volumetricAlbedo.w;
    }

    void main()
    {
        ivec3 id = ivec3(gl_GlobalInvocationID);
        int slices = int(graphite_World.volumetricGrid.z);
        ivec2 cells = ivec2(Fog_Grid.xy);
        if (id.x >= cells.x || id.y >= cells.y || id.z >= slices)
            return;

        Graphite_ViewData view = Graphite_View();
        vec2 uv = (vec2(id.xy) + Fog_Jitter.xy) * 8.0 * view.screen.zw;
        vec3 ray = normalize(Graphite_RelativeFromDepth(view, uv, 1.0));
        float along = Graphite_FogSliceDistance(float(id.z) + Fog_Jitter.z);
        vec3 p = ray * along;

        vec3 albedo;
        float density = Graphite_FogDensity(p, albedo);
        float cell = along * 8.0 * view.screen.z;
        vec4 current = vec4(density > 0.0 ? albedo * density * Fog_InScattered(p, ray, cell) : vec3(0.0), density);

        imageStore(Fog_Raw, ivec3(id.xy, int(Fog_Grid.z) + id.z), current);
    }
}
