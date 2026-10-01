Shader "Skybox/Procedural"

Properties
{
}

Pass "Skybox"
{
    Tags { "RenderOrder" = "Opaque" }

    Blend Off
    Cull None
    ZTest Off
    ZWrite Off

    GLSLPROGRAM

        Vertex
        {
            #include "ProwlCG"

            in vec3 vertexPosition;

            out vec3 vDirection;

            void main()
            {
                // Keep the sky centered on the camera
                mat4 viewNoTranslation = PROWL_MATRIX_V;
                viewNoTranslation[3][0] = 0.0;
                viewNoTranslation[3][1] = 0.0;
                viewNoTranslation[3][2] = 0.0;

                gl_Position = prowlSkyProjection() * viewNoTranslation * vec4(vertexPosition, 1.0);
                gl_Position.z = gl_Position.w;
                vDirection = vertexPosition;
            }
        }

        Fragment
        {
            layout(location = 0) out vec4 fragColor;

            in vec3 vDirection;

            // Everything that only depends on the sun, computed on the CPU in ProceduralSky.cs
            uniform vec3 _SkySunDir;
            uniform vec2 _SkySunFlat;
            uniform vec3 _SkySunDepth;
            uniform vec3 _SkySunTransmittance;
            uniform vec3 _SkySunDepthSlope;
            uniform float _SkyMieScale;
            uniform vec3 _SkyBounce;
            uniform float _SkyTwilight;
            uniform vec3 _SkyFadeBase;
            uniform float _SkyAwayFade;
            uniform float _SkyHighFade;
            uniform float _SkyHeightFade;
            uniform float _SkyHighTint;
            uniform float _SkyShadowRise;
            uniform float _SkyInverseShadowWidth;
            uniform float _SkyInverseBeltWidth;
            uniform float _SkyShadowRamp;

            #define PI 3.14159265

            const vec3 RAYLEIGH = vec3(5.802e-6, 13.558e-6, 33.1e-6);
            const float MIE_EXTINCTION = 4.44e-6;
            const float MIE_G = 0.8;
            const float RAYLEIGH_HEIGHT = 8000.0, RAYLEIGH_K = 8.0e-4;
            const float MIE_HEIGHT = 1200.0, MIE_K = 1.2e-4;

            const float SUN_FAR_FADE = 20.192;        // how fast the far air sun shift fades with view height
            const float BELT_REDDENING = 162650.0;    // extra air the sunlight crosses to reach the pink band
            const float BOUNCE_VIEW_SCALE = 1.2543;   // how quickly the bounce light levels off along long views

            const float EXPOSURE = 40.0;
            const float SUN_DISK = 2000.0;

            float sigmoid(float x)
            {
                return 0.5 + 0.5 * x * inversesqrt(1.0 + x * x);
            }

            float airMass(float mu, float k)
            {
                return 1.0 / (0.641433 * mu + sqrt(0.128567 * mu * mu + k));
            }

            vec3 atmosphere(vec3 view)
            {
                float viewMu = max(view.y, 0.0);
                float cosToSun = dot(view, _SkySunDir);

                float viewRayleigh = RAYLEIGH_HEIGHT * airMass(viewMu, RAYLEIGH_K);
                float viewMie = MIE_HEIGHT * airMass(viewMu, MIE_K);
                vec3 viewDepth = RAYLEIGH * viewRayleigh + MIE_EXTINCTION * viewMie;
                vec3 viewTransmittance = exp(-viewDepth);

                vec3 d = viewDepth - _SkySunDepth;
                vec3 lit = mix((_SkySunTransmittance - viewTransmittance) / d, 0.5 * (_SkySunTransmittance + viewTransmittance), lessThan(abs(d), vec3(1e-3)));

                float horizontal = sqrt(max(1.0 - view.y * view.y, 1e-6));
                float cosAzimuth = clamp(dot(view.xz, _SkySunFlat) / horizontal, -1.0, 1.0) * smoothstep(0.0, 0.3, horizontal);

                lit *= exp(_SkySunDepthSlope * cosAzimuth / (1.0 + SUN_FAR_FADE * viewMu));

                float mieSpread = inversesqrt(1.0 + MIE_G * MIE_G - 2.0 * MIE_G * cosToSun);
                vec3 single = (RAYLEIGH * (3.0 / (16.0 * PI) * viewRayleigh * (1.0 + cosToSun * cosToSun))
                             + _SkyMieScale * (viewMie * mieSpread * mieSpread * mieSpread)) * lit;

                if (_SkyTwilight > 0.5)
                {
                    float above = viewMu + _SkyShadowRise * cosAzimuth;
                    float sunlit = sigmoid(above * _SkyInverseShadowWidth);
                    float belt = sunlit * (1.0 - sigmoid(above * _SkyInverseBeltWidth));

                    float fade = viewMu * (_SkyHighFade + cosAzimuth * _SkyHeightFade) - cosAzimuth * _SkyAwayFade;
                    vec3 depth = _SkyFadeBase + fade + RAYLEIGH * (_SkyHighTint * viewMu + BELT_REDDENING * _SkyShadowRamp * belt);
                    single *= exp(-depth) * (1.0 - _SkyShadowRamp * (1.0 - sunlit));
                }

                vec3 seen = viewDepth * BOUNCE_VIEW_SCALE;
                vec3 multiple = _SkyBounce * viewRayleigh * (1.0 - exp(-seen)) / seen;

                return single + multiple;
            }

            vec3 jodieReinhardTonemap(vec3 c)
            {
                float l = dot(c, vec3(0.2126, 0.7152, 0.0722));
                vec3 tc = c / (c + 1.0);
                return mix(c / (l + 1.0), tc, tc);
            }

            void main()
            {
                vec3 view = normalize(vDirection);
                vec3 color = atmosphere(view);

                // Sun disk, and a darker ground below the horizon
                float disk = smoothstep(0.99985, 0.99999, dot(view, _SkySunDir)) * step(0.0, view.y);
                color += disk * SUN_DISK * _SkySunTransmittance;
                color = mix(color, color * 0.25, smoothstep(0.0, -0.02, view.y));

                fragColor = vec4(jodieReinhardTonemap(color * EXPOSURE), 1.0);
            }
        }
    ENDGLSL
}
