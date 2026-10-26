Shader "Default/Refraction"
{
    Properties
    {
        _RefractionStrength("Refraction Strength", Float) = 0.1
        _NoiseScale("Noise Scale", Float) = 1.0
        _Tint("Tint", Color) = (1.0, 1.0, 1.0, 1.0)
        _BlurRadius("Blur Radius", Float) = 0.02
        _BlurMIP("Blur MIP Bias", Float) = 2.0
        _BlurSteps("Blur Steps", Integer) = 4 [Range(1, 4)]
    }

    // The opaque scene behind the surface, bent by noise and blurred through the scene colour's mips, then tinted
    Pass
    {
        Name "Forward"
        Tags { "LightMode" = "Forward" "RenderQueue" = "3000" "SceneColor" = "Read" }
        Cull Back
        ZWrite Off
        Blend SourceAlpha InverseSourceAlpha
        GLSLPROGRAM
        Vertex
        {
            #include "Graphite/Vertex.glsl"

            out vec3 vWorldPos;

            void main()
            {
                vec3 relative = Graphite_ObjectToRelative(vertexPosition);
                vWorldPos = Graphite_RelativeToWorld(relative);
                gl_Position = Graphite_RelativeToClip(relative);
            }
        }
        Fragment
        {
            #include "Graphite/SceneColor.glsl"

            uniform float _RefractionStrength;
            uniform float _NoiseScale;
            uniform vec4 _Tint;
            uniform float _BlurRadius;
            uniform float _BlurMIP;
            uniform int _BlurSteps;

            in vec3 vWorldPos;
            layout(location = 0) out vec4 fragColor;

            float hash(vec3 p)
            {
                p = fract(p * 0.3183099 + 0.1);
                p *= 17.0;
                return fract(p.x * p.y * p.z * (p.x + p.y + p.z));
            }

            float noise(vec3 x)
            {
                vec3 i = floor(x);
                vec3 f = fract(x);
                f = f * f * (3.0 - 2.0 * f);
                return mix(mix(mix(hash(i + vec3(0.0, 0.0, 0.0)), hash(i + vec3(1.0, 0.0, 0.0)), f.x),
                               mix(hash(i + vec3(0.0, 1.0, 0.0)), hash(i + vec3(1.0, 1.0, 0.0)), f.x), f.y),
                           mix(mix(hash(i + vec3(0.0, 0.0, 1.0)), hash(i + vec3(1.0, 0.0, 1.0)), f.x),
                               mix(hash(i + vec3(0.0, 1.0, 1.0)), hash(i + vec3(1.0, 1.0, 1.0)), f.x), f.y), f.z);
            }

            float InterleavedGradientNoise(vec2 pixel, int frame)
            {
                pixel += float(frame) * vec2(2.083, 4.867);
                return fract(52.9829189 * fract(dot(pixel, vec2(0.06711056, 0.00583715))));
            }

            void main()
            {
                Graphite_ViewData view = Graphite_View();
                float time = view.time.x;

                vec3 noiseInput = vWorldPos * _NoiseScale + vec3(0.5, 1.0, 2.0) * time * 0.1;
                vec2 offset = vec2(noise(noiseInput), noise(noiseInput + vec3(5.2, 1.3, 0.0)));
                offset = (offset * 2.0 - 1.0) * _RefractionStrength;
                vec2 refractedUV = clamp(Graphite_ScreenUV() + offset, 0.0, 1.0);

                // One step mip blur: a few taps around the refracted point at a rotating angle
                float angle = InterleavedGradientNoise(gl_FragCoord.xy, int(view.time.z) % 60) * 6.28318530718;
                vec2 dir = vec2(cos(angle), sin(angle));
                vec2 aspect = vec2(min(1.0, view.screen.y / view.screen.x), min(1.0, view.screen.x / view.screen.y));

                int steps = clamp(_BlurSteps, 1, 4);
                vec3 sum = vec3(0.0);
                for (int i = 0; i < steps; i++)
                {
                    sum += Graphite_SampleSceneColor(refractedUV + dir * _BlurRadius * aspect, _BlurMIP);
                    dir = vec2(-dir.y, dir.x);
                }

                fragColor = vec4(sum / float(steps) * _Tint.rgb, _Tint.a);
            }
        }
        ENDGLSL
    }

    Fallback "Graphite/Fallback"
}
