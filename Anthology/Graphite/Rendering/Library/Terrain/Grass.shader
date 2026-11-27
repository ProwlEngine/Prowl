// TerrainSurface's grass on the Foliage surface model, drawn indirect with one instance per card from TerrainGrass's
// instance texture, custom0 x the first row of the draw's slot. A card is two crossed quads of its slice of _GrassCards,
// cut out by its alpha. The mesh gives only where a vertex sits on its card: x across from -0.5 to 0.5, y up from root
// to tip, z the second quad. Cards bow with the world's wind more toward the tip, less the stiffer the type, and shade
// from the type's root to tip colour, leaning toward the colour of the ground they grow from by the type's ground tint.
Shader "Graphite/Terrain/Grass"
{
    Properties
    {
        base_color("Base Color", Color) = (1.0, 1.0, 1.0, 1.0)
        _GrassInstances("Grass Instances", Texture2D) = "black" {}
        _GrassTypes("Grass Types", Texture2D) = "black" {}
        _GrassCards("Grass Cards", Texture2DArray) = "white" {}
        foliage_normal_lean("Normal Lean", Float) = 0.45 [Range(0, 1)]
    }

    Surface "Foliage"
    {
        GLSLPROGRAM
        uniform sampler2DArray _GrassCards;

        void Surface(inout PBR_Params p, Graphite_SurfaceData i)
        {
            vec4 card = texture(_GrassCards, vec3(i.uv, i.uv1.x));
            p.baseColor *= card.rgb;
            p.geometryOpacity *= card.a;
        }
        ENDGLSL

        Vertex
        {
            GLSLPROGRAM
            uniform sampler2D _GrassInstances;
            uniform sampler2D _GrassTypes;

            void SurfaceVertex(inout Graphite_SurfaceVertex v)
            {
                int texel = int(Graphite_InstanceIndex()) * 2;
                ivec2 at = ivec2(texel % 1024, int(Graphite_Object().custom0.x) + texel / 1024);
                vec4 a = texelFetch(_GrassInstances, at, 0);
                vec4 b = texelFetch(_GrassInstances, at + ivec2(1, 0), 0);
                int type = int(b.w);
                float variation = 0.8 + fract(b.w) * 0.8;
                vec4 root = texelFetch(_GrassTypes, ivec2(1, type), 0);
                vec4 tip = texelFetch(_GrassTypes, ivec2(2, type), 0);
                vec4 card = texelFetch(_GrassTypes, ivec2(3, type), 0);

                vec3 mesh = v.position;
                float t = mesh.y;
                float height = b.x, width = b.y;
                float yaw = a.w + mesh.z * 1.5707963;
                vec3 facing = vec3(cos(yaw), 0.0, sin(yaw));
                vec3 across = vec3(-facing.z, 0.0, facing.x);

                vec3 sway = Graphite_WindSway(Graphite_ObjectToRelative(a.xyz), v.time, (1.0 - tip.w) * t * t * height);
                float drop = 1.0 - 0.35 * dot(sway, sway) / max(height * height, 1e-4);

                v.position = a.xyz + across * (mesh.x * width) + vec3(0.0, t * height * drop, 0.0) + sway;
                v.normal = facing;
                v.tangent = vec4(across, 1.0);
                // The colours lean toward the ground's under the card, fully at the root and half as much at the tip
                uint packed = uint(b.z);
                vec3 ground = vec3(uvec3(packed >> 16u, packed >> 8u, packed) & 255u) / 255.0;
                ground *= ground;
                float tint = root.w * (1.0 - 0.5 * t);
                v.color = vec4(mix(mix(root.rgb, tip.rgb, t), ground, tint) * variation, 1.0);
                v.uv = vec2(mesh.x + 0.5, 1.0 - t);
                v.uv1 = vec2(card.z, 0.0);
            }
            ENDGLSL
        }
    }
}
