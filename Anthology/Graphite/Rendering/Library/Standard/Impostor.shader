Shader "Graphite/Impostor"
{
    Properties
    {
        _ImpostorAlbedo("Albedo", Texture2D) = "white" {}
        _ImpostorNormalDepth("Normal and Depth", Texture2D) = "white" {}
        _ImpostorMaterial("Material", Texture2D) = "white" {}
        _ImpostorFrames("Frames", Float) = 12
        _ImpostorHemi("Hemi", Float) = 1
        _ImpostorSphere("Sphere", Vector) = (0, 0, 0, 1)
    }

    // A card facing the viewer across the baked sphere, showing the three frames nearest the view direction blended
    Surface "Lit"
    {
        GLSLPROGRAM
        #include "Graphite/Impostor.glsl"
        #include "Graphite/Octahedral.glsl"
        uniform sampler2D _ImpostorAlbedo;
        uniform sampler2D _ImpostorNormalDepth;
        uniform sampler2D _ImpostorMaterial;
        uniform float _ImpostorFrames;
        uniform float _ImpostorHemi;

        // One frame's texels where the card point projects into it. Bakes leave zero where nothing covers, so every channel
        // is already weighted by coverage and filtering keeps them in step
        void Impostor_Sample(vec2 frame, float weight, vec3 point, inout vec4 albedo, inout vec4 normalDepth, inout vec4 material)
        {
            vec3 right, up;
            Graphite_ImpostorBasis(Graphite_ImpostorDirection(frame / (_ImpostorFrames - 1.0) * 2.0 - 1.0, _ImpostorHemi > 0.5), right, up);
            float inset = 0.5 * _ImpostorFrames / float(textureSize(_ImpostorAlbedo, 0).x);
            vec2 uv = clamp(vec2(dot(point, right), dot(point, up)) * 0.5 + 0.5, inset, 1.0 - inset);
            vec2 at = (frame + uv) / _ImpostorFrames;
            albedo += texture(_ImpostorAlbedo, at) * weight;
            normalDepth += texture(_ImpostorNormalDepth, at) * weight;
            material += texture(_ImpostorMaterial, at) * weight;
        }

        void Surface(inout PBR_Params p, Graphite_SurfaceData i)
        {
            bool hemi = _ImpostorHemi > 0.5;
            vec3 right, up;
            Graphite_ImpostorBasis(Graphite_ImpostorDirection(i.uv1 * 2.0 - 1.0, hemi), right, up);
            vec3 point = right * (i.uv.x * 2.0 - 1.0) + up * (i.uv.y * 2.0 - 1.0);

            // The grid triangle the view falls in, its corners weighted by where in it
            vec2 grid = clamp(i.uv1 * (_ImpostorFrames - 1.0), vec2(0.0), vec2(_ImpostorFrames - 1.0));
            vec2 cell = min(floor(grid), vec2(_ImpostorFrames - 2.0));
            vec2 f = grid - cell;
            vec4 albedo = vec4(0.0), normalDepth = vec4(0.0), material = vec4(0.0);
            if (f.x + f.y < 1.0)
            {
                Impostor_Sample(cell, 1.0 - f.x - f.y, point, albedo, normalDepth, material);
                Impostor_Sample(cell + vec2(1.0, 0.0), f.x, point, albedo, normalDepth, material);
                Impostor_Sample(cell + vec2(0.0, 1.0), f.y, point, albedo, normalDepth, material);
            }
            else
            {
                Impostor_Sample(cell + vec2(1.0), f.x + f.y - 1.0, point, albedo, normalDepth, material);
                Impostor_Sample(cell + vec2(0.0, 1.0), 1.0 - f.x, point, albedo, normalDepth, material);
                Impostor_Sample(cell + vec2(1.0, 0.0), 1.0 - f.y, point, albedo, normalDepth, material);
            }

            float coverage = max(albedo.a, 1e-4);
            normalDepth /= coverage;
            material /= coverage;
            vec3 normal = Graphite_OctDecode(normalDepth.xy * 2.0 - 1.0);
            p.baseColor = albedo.rgb / coverage;
            p.geometryOpacity = albedo.a;
            p.geometryNormal = normalize(Graphite_NormalMatrix(graphite_Transforms[Graphite_ObjectIndex()]) * normal);
            p.specularRoughness = material.r;
            p.baseMetalness = material.g;
            p.occlusion = normalDepth.a;
        }
        ENDGLSL

        Vertex
        {
            GLSLPROGRAM
            #include "Graphite/Impostor.glsl"
            uniform float _ImpostorHemi;
            uniform vec4 _ImpostorSphere;

            void SurfaceVertex(inout Graphite_SurfaceVertex v)
            {
                // Toward the viewer from the center, along the view for orthographic views such as shadows
                vec3 toViewer = Graphite_View().projection[3][3] > 0.5 ? -Graphite_ViewRay(vec2(0.0)) : -Graphite_ObjectToRelative(_ImpostorSphere.xyz);
                vec3 d = normalize(transpose(Graphite_Linear(Graphite_ObjectTransform())) * toViewer);
                vec3 right, up;
                Graphite_ImpostorBasis(d, right, up);
                vec2 corner = v.uv * 2.0 - 1.0;
                v.position = _ImpostorSphere.xyz + (right * corner.x + up * corner.y) * _ImpostorSphere.w;
                v.normal = d;
                v.tangent = vec4(right, 1.0);
                v.uv1 = Graphite_ImpostorGrid(d, _ImpostorHemi > 0.5) * 0.5 + 0.5;
            }
            ENDGLSL
        }
    }
}
