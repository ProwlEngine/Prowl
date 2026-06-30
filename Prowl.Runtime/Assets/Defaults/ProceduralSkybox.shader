Shader "Default/ProceduralSkybox"
{
    Pass
    {
        Name "Skybox"
        Tags { "RenderOrder" = "Opaque" }

        Cull Off
        ZTest Disabled
        ZWrite Off

        SLANGPROGRAM
        import ProwlCG;

        struct MaterialData { float3 _SunDir; }
        ParameterBlock<MaterialData> Mat;

        struct VertexInput { float3 position : POSITION0; }
        struct Varyings
        {
            float4 position : SV_Position;
            float3 vSkyColor : TEXCOORD0;
            float3 vDirection : TEXCOORD1;
            float3 vSunDir : TEXCOORD2;
        }

        #define PI 3.141592
        #define iSteps 16
        #define jSteps 8

        float2 rsi(float3 r0, float3 rd, float sr) {
            float a = dot(rd, rd);
            float b = 2.0 * dot(rd, r0);
            float c = dot(r0, r0) - (sr * sr);
            float d = (b*b) - 4.0*a*c;
            if (d < 0.0) return float2(1e5, -1e5);
            return float2(
                (-b - sqrt(d))/(2.0*a),
                (-b + sqrt(d))/(2.0*a)
            );
        }

        float3 atmosphere(float3 r, float3 r0, float3 pSun, float iSun, float rPlanet, float rAtmos, float3 kRlh, float kMie, float shRlh, float shMie, float g) {
            pSun = normalize(pSun);
            r = normalize(r);

            float2 p = rsi(r0, r, rAtmos);
            if (p.x > p.y) return float3(0,0,0);
            p.y = min(p.y, rsi(r0, r, rPlanet).x);
            float iStepSize = (p.y - p.x) / float(iSteps);

            float iTime = 0.0;

            float3 totalRlh = float3(0,0,0);
            float3 totalMie = float3(0,0,0);

            float iOdRlh = 0.0;
            float iOdMie = 0.0;

            float mu = dot(r, pSun);
            float mumu = mu * mu;
            float gg = g * g;
            float pRlh = 3.0 / (16.0 * PI) * (1.0 + mumu);
            float pMie = 3.0 / (8.0 * PI) * ((1.0 - gg) * (mumu + 1.0)) / (pow(1.0 + gg - 2.0 * mu * g, 1.5) * (2.0 + gg));

            for (int i = 0; i < iSteps; i++) {
                float3 iPos = r0 + r * (iTime + iStepSize * 0.5);
                float iHeight = length(iPos) - rPlanet;

                float odStepRlh = exp(-iHeight / shRlh) * iStepSize;
                float odStepMie = exp(-iHeight / shMie) * iStepSize;

                iOdRlh += odStepRlh;
                iOdMie += odStepMie;

                float jStepSize = rsi(iPos, pSun, rAtmos).y / float(jSteps);
                float jTime = 0.0;
                float jOdRlh = 0.0;
                float jOdMie = 0.0;

                for (int j = 0; j < jSteps; j++) {
                    float3 jPos = iPos + pSun * (jTime + jStepSize * 0.5);
                    float jHeight = length(jPos) - rPlanet;

                    jOdRlh += exp(-jHeight / shRlh) * jStepSize;
                    jOdMie += exp(-jHeight / shMie) * jStepSize;

                    jTime += jStepSize;
                }

                float3 attn = exp(-(kMie * (iOdMie + jOdMie) + kRlh * (iOdRlh + jOdRlh)));

                totalRlh += odStepRlh * attn;
                totalMie += odStepMie * attn;

                iTime += iStepSize;
            }

            return iSun * (pRlh * kRlh * totalRlh + pMie * kMie * totalMie);
        }

        [shader("vertex")]
        Varyings Vertex(VertexInput input)
        {
            float4x4 viewNoTranslation = Frame.prowl_MatV;
            viewNoTranslation[0][3] = 0.0;
            viewNoTranslation[1][3] = 0.0;
            viewNoTranslation[2][3] = 0.0;

            Varyings output;
            output.position = mul(Frame.prowl_MatP, mul(viewNoTranslation, float4(input.position, 1.0)));
            output.position.z = output.position.w;

            output.vDirection = input.position;
            output.vSunDir = normalize(Mat._SunDir);
            output.vSkyColor = atmosphere(
                normalize(output.vDirection),
                float3(0, 6372e3, 0),
                output.vSunDir,
                22.0,
                6371e3,
                6471e3,
                float3(5.5e-6, 13.0e-6, 22.4e-6),
                21e-6,
                8e3,
                1.2e3,
                0.758
            );
            return output;
        }

        [shader("fragment")]
        float4 Fragment(Varyings input) : SV_Target
        {
            float3 color = input.vSkyColor;
            color.rgb += smoothstep(0.996, 0.9965, dot(normalize(input.vDirection), input.vSunDir)); // Sun
            color = 1.0 - exp(-1.0 * color); // Exposure
            return float4(color, 1.0);
        }
        ENDSLANG
    }
}
