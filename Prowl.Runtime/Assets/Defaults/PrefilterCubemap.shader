Shader "Hidden/PrefilterCubemap"

Properties
{
}

// Convolves a captured environment with the GGX lobe of one roughness, one face at a time, for a reflection
// probe mip. Samples read the source mip whose texels match the solid angle each sample stands for, so few
// samples give a smooth result.
Pass "Prefilter"
{
    Tags { "RenderOrder" = "Opaque" }

    Blend Off
    Cull None
    ZTest Off
    ZWrite Off

    GLSLPROGRAM

    Vertex
    {
        layout (location = 0) in vec3 vertexPosition;
        layout (location = 1) in vec2 vertexTexCoord;

        out vec2 TexCoords;

        void main()
        {
            TexCoords = vertexTexCoord;
            gl_Position = vec4(vertexPosition, 1.0);
        }
    }

    Fragment
    {
        layout (location = 0) out vec4 finalColor;

        in vec2 TexCoords;

        uniform samplerCube _Source;
        uniform int _Face;
        uniform float _Roughness;
        uniform float _SourceSize;
        uniform int _SampleCount;

        const float PI = 3.14159265359;

        // Inverse of the GL face selection, texel row 0 at the bottom of the face
        vec3 FaceDirection(int face, vec2 uv)
        {
            float s = uv.x * 2.0 - 1.0;
            float t = uv.y * 2.0 - 1.0;
            if (face == 0) return vec3(1.0, -t, -s);
            if (face == 1) return vec3(-1.0, -t, s);
            if (face == 2) return vec3(s, 1.0, t);
            if (face == 3) return vec3(s, -1.0, -t);
            if (face == 4) return vec3(s, -t, 1.0);
            return vec3(-s, -t, -1.0);
        }

        vec2 Hammersley(uint i, uint n)
        {
            return vec2(float(i) / float(n), float(bitfieldReverse(i)) * 2.3283064365386963e-10);
        }

        vec3 ImportanceSampleGGX(vec2 xi, vec3 n, float a)
        {
            float phi = 2.0 * PI * xi.x;
            float cosTheta = sqrt((1.0 - xi.y) / (1.0 + (a * a - 1.0) * xi.y));
            float sinTheta = sqrt(1.0 - cosTheta * cosTheta);
            vec3 h = vec3(cos(phi) * sinTheta, sin(phi) * sinTheta, cosTheta);

            vec3 up = abs(n.z) < 0.999 ? vec3(0.0, 0.0, 1.0) : vec3(1.0, 0.0, 0.0);
            vec3 tangent = normalize(cross(up, n));
            vec3 bitangent = cross(n, tangent);
            return normalize(tangent * h.x + bitangent * h.y + n * h.z);
        }

        void main()
        {
            vec3 n = normalize(FaceDirection(_Face, TexCoords));
            if (_Roughness <= 0.0)
            {
                finalColor = vec4(textureLod(_Source, n, 0.0).rgb, 1.0);
                return;
            }

            float a = _Roughness * _Roughness;
            float a2 = a * a;
            float texelSolidAngle = 4.0 * PI / (6.0 * _SourceSize * _SourceSize);
            uint count = uint(_SampleCount);

            vec3 sum = vec3(0.0);
            float weight = 0.0;
            for (uint i = 0u; i < count; i++)
            {
                vec3 h = ImportanceSampleGGX(Hammersley(i, count), n, a);
                float nDotH = max(dot(n, h), 0.0);
                vec3 l = 2.0 * nDotH * h - n;
                float nDotL = dot(n, l);
                if (nDotL <= 0.0) continue;

                // With the view along the normal the pdf of the reflected direction is D / 4
                float d = nDotH * nDotH * (a2 - 1.0) + 1.0;
                float pdf = a2 / (PI * d * d) * 0.25;
                float sampleSolidAngle = 1.0 / (float(count) * pdf + 1e-4);
                float mip = max(0.5 * log2(sampleSolidAngle / texelSolidAngle) + 1.0, 0.0);

                vec3 c = textureLod(_Source, l, mip).rgb;
                sum += min(c, vec3(256.0)) * nDotL;
                weight += nDotL;
            }

            finalColor = vec4(sum / max(weight, 1e-4), 1.0);
        }
    }

    ENDGLSL
}
