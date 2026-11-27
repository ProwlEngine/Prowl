#version 460
#extension GL_GOOGLE_cpp_style_line_directive : require
#line 17 "Graphite/Internal/Prefilter.glsl"

#extension GL_EXT_multiview : require

    layout(set = 0, binding = 0) uniform samplerCube Graphite_Source;
    layout(push_constant) uniform Push { float roughness; float sourceSize; uint sampleCount; uint pad; } push;
    layout(location = 0) in vec2 vUV;
    layout(location = 0) out vec4 outColor;

    const float PI = 3.14159265359;


    vec3 FaceDirection(int face, vec2 uv)
    {
        float s = uv.x * 2.0 - 1.0;
        float t = 1.0 - uv.y * 2.0;
        if (face == 0) return vec3(1.0, - t, - s);
        if (face == 1) return vec3(- 1.0, - t, s);
        if (face == 2) return vec3(s, 1.0, t);
        if (face == 3) return vec3(s, - 1.0, - t);
        if (face == 4) return vec3(s, - t, 1.0);
        return vec3(- s, - t, - 1.0);
    }

    vec2 Hammersley(uint i, uint n)
    {
        return vec2(float(i) / float(n), float(bitfieldReverse(i)) * 2.3283064365386963e-10);
    }

    vec3 ImportanceSampleGGX(vec2 xi, vec3 n, float a)
    {
        float phi = 2.0 * PI * xi.x;
        float cosTheta = sqrt( (1.0 - xi.y) / (1.0 + (a * a - 1.0) * xi.y));
        float sinTheta = sqrt(1.0 - cosTheta * cosTheta);
        vec3 h = vec3(cos(phi) * sinTheta, sin(phi) * sinTheta, cosTheta);

        vec3 up = abs(n.z) < 0.999 ? vec3(0.0, 0.0, 1.0) : vec3(1.0, 0.0, 0.0);
        vec3 tangent = normalize(cross(up, n));
        vec3 bitangent = cross(n, tangent);
        return normalize(tangent * h.x + bitangent * h.y + n * h.z);
    }

    void main()
    {
        vec3 n = normalize(FaceDirection(int(gl_ViewIndex), vUV));
        if (push.roughness <= 0.0)
        {
            outColor = vec4(textureLod(Graphite_Source, n, 0.0).rgb, 1.0);
            return;
        }

        float a = push.roughness * push.roughness;
        float a2 = a * a;
        float texelSolidAngle = 4.0 * PI / (6.0 * push.sourceSize * push.sourceSize);
        uint count = push.sampleCount;

        vec3 sum = vec3(0.0);
        float weight = 0.0;
        for (uint i = 0u; i < count; i ++)
        {
            vec3 h = ImportanceSampleGGX(Hammersley(i, count), n, a);
            float nDotH = max(dot(n, h), 0.0);
            vec3 l = 2.0 * nDotH * h - n;
            float nDotL = dot(n, l);
            if (nDotL <= 0.0)
                continue;


            float d = nDotH * nDotH * (a2 - 1.0) + 1.0;
            float pdf = a2 / (PI * d * d) * 0.25;
            float sampleSolidAngle = 1.0 / (float(count) * pdf + 1e-4);
            float mip = max(0.5 * log2(sampleSolidAngle / texelSolidAngle) + 1.0, 0.0);

            vec3 c = textureLod(Graphite_Source, l, mip).rgb;
            sum += min(c, vec3(256.0)) * nDotL;
            weight += nDotL;
        }

        outColor = vec4(sum / max(weight, 1e-4), 1.0);
    }
