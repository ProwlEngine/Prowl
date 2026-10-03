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
            uniform vec3 _SkySunColor;
            uniform vec3 _SkySunDepthSlope;
            uniform vec3 _SkyRayleighScale;
            uniform vec3 _SkyMieScale;
            uniform vec2 _SkyMiePhase;
            uniform vec3 _SkyBounce;
            uniform vec3 _SkyBounceRedden;
            uniform float _SkyBounceSide;
            uniform float _SkyTwilight;
            uniform vec3 _SkyFadeBase;
            uniform float _SkyAwayFade;
            uniform float _SkyHighFade;
            uniform float _SkyHeightFade;
            uniform float _SkyShadowRise;
            uniform float _SkyInverseShadowWidth;
            uniform float _SkyRampStart;
            uniform float _SkyRampSpread;

            const vec3 RAYLEIGH = vec3(5.802e-6, 13.558e-6, 33.1e-6);
            const float MIE_EXTINCTION = 4.44e-6;

            const float SUN_FAR_FADE = 12.266;       // how fast the far air sun shift fades with view height
            const float BELT_REDDENING = 142550.0;   // extra air the sunlight crosses to reach the pink band
            const float BELT_FADE = 0.055559;        // how softly the pink band fades out above the shadow edge
            const float SUN_FAR_ASYMMETRY = -0.39738;  // how much weaker the far air shift is toward the sun
            const float BOUNCE_SIDE_FADE = 2.5771;    // how fast the bounce light's lean toward the sun fades looking up

            const float EXPOSURE = 40.0;
            const float SUN_DISK = 2000.0;

            float sigmoid(float x)
            {
                return 0.5 + 0.5 * x * inversesqrt(1.0 + x * x);
            }

            float airGradient(float mu, vec4 g)
            {
                return g.x * (mu + g.y) / (mu * (mu + g.z) + g.w);
            }

            vec3 atmosphere(vec3 view)
            {
                float viewMu = max(view.y, 0.0);
                float cosToSun = dot(view, _SkySunDir);

                float viewRayleigh = airGradient(viewMu, vec4(7978.28, 0.07868, 0.0735028, 0.00224883));
                float viewMie = airGradient(viewMu, vec4(1198.75, 0.027243, 0.0258633, 0.000305857));
                vec3 viewDepth = RAYLEIGH * viewRayleigh + MIE_EXTINCTION * viewMie;
                vec3 viewTransmittance = exp(-viewDepth);

                vec3 d = viewDepth - _SkySunDepth;
                vec3 lit = mix((_SkySunTransmittance - viewTransmittance) / d, 0.5 * (_SkySunTransmittance + viewTransmittance), lessThan(abs(d), vec3(1e-3)));

                float cosAzimuth = dot(view.xz, _SkySunFlat);

                lit *= max(1.0 + _SkySunDepthSlope * (cosAzimuth * (1.0 + SUN_FAR_ASYMMETRY * cosAzimuth) / (1.0 + SUN_FAR_FADE * viewMu)), 0.0);

                float mieSpread = inversesqrt(_SkyMiePhase.x - _SkyMiePhase.y * cosToSun);
                vec3 single = (_SkyRayleighScale * (viewRayleigh * (1.0 + cosToSun * cosToSun))
                             + _SkyMieScale * (viewMie * mieSpread * mieSpread * mieSpread)) * lit;

                if (_SkyTwilight > 0.5)
                {
                    float above = viewMu + _SkyShadowRise * cosAzimuth;
                    float sunlit = sigmoid(above * _SkyInverseShadowWidth);
                    float belt = sunlit * (1.0 - sigmoid(above * (1.0 / BELT_FADE)));
                    float strength = smoothstep(0.0, 1.0, _SkyRampStart - _SkyRampSpread * cosAzimuth);

                    float fade = viewMu * (_SkyHighFade + cosAzimuth * _SkyHeightFade) - cosAzimuth * _SkyAwayFade;
                    vec3 depth = _SkyFadeBase + fade + RAYLEIGH * (BELT_REDDENING * strength * belt);
                    single *= exp(-depth) * (1.0 - strength * (1.0 - sunlit));
                }

                vec3 multiple = _SkyBounce * viewRayleigh * (1.0 - viewTransmittance) / viewDepth
                              * (1.0 + _SkyBounceSide * cosAzimuth / (1.0 + BOUNCE_SIDE_FADE * viewMu))
                              * exp(-_SkyBounceRedden * viewRayleigh);

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
                color += disk * SUN_DISK * _SkySunColor;
                color = mix(color, color * 0.25, smoothstep(0.0, -0.02, view.y));

                fragColor = vec4(jodieReinhardTonemap(color * EXPOSURE), 1.0);
            }
        }
    ENDGLSL
}
