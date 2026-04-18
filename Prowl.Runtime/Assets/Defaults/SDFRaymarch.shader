Shader "Default/SDFRaymarch"

Properties
{
    _SDF ("SDF Volume", Texture3D) = "white"
    _BoundsMin ("Bounds Min", Vector3) = (-0.5, -0.5, -0.5)
    _BoundsMax ("Bounds Max", Vector3) = (0.5, 0.5, 0.5)
    _VoxelSize ("Voxel Size", Vector3) = (0.02, 0.02, 0.02)
    _SurfaceEpsilon ("Surface Epsilon", Float) = 0.0008
    _StepScale ("Step Scale", Float) = 0.9
    _MaxSteps ("Max Steps", Float) = 160.0
    _SurfaceColor ("Surface Color", Color) = (0.72, 0.82, 1.0, 1.0)
    _AmbientStrength ("Ambient", Float) = 0.2
}

// Renders a tight oriented cube at the SDF bounds. The fragment shader clips the ray
// to the bounds, sphere-traces the signed distance texture, and shades the first hit
// with a central-difference gradient normal. Cull Front so the shader still runs when
// the camera is inside the volume.
Pass "SDFRaymarch"
{
    Tags { "RenderOrder" = "Opaque" }
    Cull Front

    GLSLPROGRAM
        Vertex
        {
            #include "Fragment"
            #include "VertexAttributes"

            out vec3 worldPos;

            void main()
            {
                gl_Position = TransformClip(vertexPosition);
                worldPos = TransformPosition(vertexPosition);
            }
        }

        Fragment
        {
            #include "Fragment"

            layout (location = 0) out vec4 fragColor;

            in vec3 worldPos;

            uniform sampler3D _SDF;
            uniform vec3 _BoundsMin;
            uniform vec3 _BoundsMax;
            uniform vec3 _VoxelSize;
            uniform float _SurfaceEpsilon;
            uniform float _StepScale;
            uniform float _MaxSteps;
            uniform vec4 _SurfaceColor;
            uniform float _AmbientStrength;

            vec2 intersectAABB(vec3 ro, vec3 rd, vec3 bmin, vec3 bmax)
            {
                vec3 invD = 1.0 / rd;
                vec3 t0 = (bmin - ro) * invD;
                vec3 t1 = (bmax - ro) * invD;
                vec3 tmin = min(t0, t1);
                vec3 tmax = max(t0, t1);
                float near = max(max(tmin.x, tmin.y), tmin.z);
                float far = min(min(tmax.x, tmax.y), tmax.z);
                return vec2(near, far);
            }

            float sampleSDF(vec3 wp)
            {
                vec3 uvw = (wp - _BoundsMin) / (_BoundsMax - _BoundsMin);
                return texture(_SDF, uvw).r;
            }

            vec3 calcNormal(vec3 wp)
            {
                vec3 h = _VoxelSize;
                float dx = sampleSDF(wp + vec3(h.x, 0, 0)) - sampleSDF(wp - vec3(h.x, 0, 0));
                float dy = sampleSDF(wp + vec3(0, h.y, 0)) - sampleSDF(wp - vec3(0, h.y, 0));
                float dz = sampleSDF(wp + vec3(0, 0, h.z)) - sampleSDF(wp - vec3(0, 0, h.z));
                vec3 n = vec3(dx, dy, dz);
                float ln = length(n);
                return ln > 1e-6 ? n / ln : vec3(0.0, 1.0, 0.0);
            }

            void main()
            {
                vec3 ro = _WorldSpaceCameraPos;
                vec3 rd = normalize(worldPos - ro);

                vec2 tBox = intersectAABB(ro, rd, _BoundsMin, _BoundsMax);
                float tStart = max(tBox.x, 0.0);
                float tEnd = tBox.y;
                if (tEnd <= tStart) discard;

                float t = tStart;
                int maxSteps = int(_MaxSteps);
                float minStep = min(min(_VoxelSize.x, _VoxelSize.y), _VoxelSize.z) * 0.25;

                for (int i = 0; i < 512; i++)
                {
                    if (i >= maxSteps) break;
                    if (t > tEnd) break;

                    vec3 p = ro + rd * t;
                    float d = sampleSDF(p);

                    if (d < _SurfaceEpsilon)
                    {
                        vec3 n = calcNormal(p);
                        vec3 L = normalize(vec3(0.5, 1.0, 0.3));
                        float lam = max(dot(n, L), 0.0);
                        float fres = pow(1.0 - max(dot(n, -rd), 0.0), 2.0);
                        vec3 col = _SurfaceColor.rgb * (_AmbientStrength + lam) + vec3(fres * 0.1);
                        fragColor = vec4(col, 1.0);
                        return;
                    }

                    t += max(d * _StepScale, minStep);
                }
                discard;
            }
        }
    ENDGLSL
}
