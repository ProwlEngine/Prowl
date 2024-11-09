Shader "Default/ProceduralSky"

Pass "ProceduralSky"
{
    // Rasterizer culling mode
    Cull None


	DepthStencil
	{
		DepthWrite Off
        DepthTest LessEqual
	}


	HLSLPROGRAM
        #pragma vertex Vertex
        #pragma fragment Fragment
        
        #include "Prowl.hlsl"

        #define PI 3.1415926535f
		#define PI_2 (3.1415926535f * 2.0)

		#define EPSILON 1e-5

		#define SAMPLES_NUMS 16


        struct Appdata
        {
            float3 pos : POSITION;
        };


        struct Varyings
        {
            float4 pos : SV_POSITION;
            float3 vCol : COLOR;
            float3 vDir : NORMAL;
        };

        float3 _SunDir;

		struct ScatteringParams
		{
			float sunRadius;
			float sunRadiance;

			float mieG;
			float mieHeight;

			float rayleighHeight;

			float3 waveLambdaMie;
			float3 waveLambdaOzone;
			float3 waveLambdaRayleigh;

			float earthRadius;
			float earthAtmTopRadius;
			float3 earthCenter;
		};


		float3 ComputeSphereNormal(float2 coord, float phiStart, float phiLength, float thetaStart, float thetaLength)
		{
			float3 normal;
			normal.x = -sin(thetaStart + coord.y * thetaLength) * sin(phiStart + coord.x * phiLength);
			normal.y = -cos(thetaStart + coord.y * thetaLength);
			normal.z = -sin(thetaStart + coord.y * thetaLength) * cos(phiStart + coord.x * phiLength);
			return normalize(normal);
		}


		float2 ComputeRaySphereIntersection(float3 position, float3 dir, float3 center, float radius)
		{
			float3 origin = position - center;
			float B = dot(origin, dir);
			float C = dot(origin, origin) - radius * radius;
			float D = B * B - C;

			float2 minimaxIntersections;
			if (D < 0)
			{
				minimaxIntersections = (float2)-1;
			}
			else
			{
				D = sqrt(D);
				minimaxIntersections = float2(-B - D, -B + D);
			}

			return minimaxIntersections;
		}


		float3 ComputeWaveLambdaRayleigh(float3 lambda)
		{
			const float n = 1.0003;
			const float N = 2.545E25;
			const float pn = 0.035;
			const float n2 = n * n;
			const float pi3 = PI * PI * PI;
			const float rayleighConst = (8 * pi3 * pow(n2 - 1, 2)) / (3 * N) * ((6 + 3 * pn) / (6 - 7 * pn));

			return rayleighConst / (lambda * lambda * lambda * lambda);
		}


		float ComputePhaseMie(float theta, float g)
		{
			float g2 = g * g;
			return (1 - g2) / pow(1 + g2 - 2 * g * saturate(theta), 1.5) / (4 * PI);
		}


		float ComputePhaseRayleigh(float theta)
		{
			float theta2 = theta * theta;
			return (theta2 * 0.75 + 0.75) / (4 * PI);
		}


		float ChapmanApproximation(float X, float h, float cosZenith)
		{
			float c = sqrt(X + h);
			float c_exp_h = c * exp(-h);

			if (cosZenith >= 0)
			{
				return c_exp_h / (c * cosZenith + 1);
			}
			else
			{
				float x0 = sqrt(1 - cosZenith * cosZenith) * (X + h);
				float c0 = sqrt(x0);

				return 2 * c0 * exp(X - x0) - c_exp_h / (1 - c * cosZenith);
			}
		}


		float GetOpticalDepthSchueler(float h, float H, float earthRadius, float cosZenith)
		{
			return H * ChapmanApproximation(earthRadius / H, h / H, cosZenith);
		}


		float3 GetTransmittance(ScatteringParams setting, float3 L, float3 V)
		{
			float ch = GetOpticalDepthSchueler(L.y, setting.rayleighHeight, setting.earthRadius, V.y);
			return exp(-(setting.waveLambdaMie + setting.waveLambdaRayleigh) * ch);
		}


		float2 ComputeOpticalDepth(ScatteringParams setting, float3 samplePoint, float3 V, float3 L, float neg)
		{
			float rl = length(samplePoint);
			float h = rl - setting.earthRadius;
			float3 r = samplePoint / rl;

			float cos_chi_sun = dot(r, L);
			float cos_chi_ray = dot(r, V * neg);

			float opticalDepthSun = GetOpticalDepthSchueler(h, setting.rayleighHeight, setting.earthRadius, cos_chi_sun);
			float opticalDepthCamera = GetOpticalDepthSchueler(h, setting.rayleighHeight, setting.earthRadius, cos_chi_ray) * neg;

			return float2(opticalDepthSun, opticalDepthCamera);
		}


		void AerialPerspective(ScatteringParams setting,
            float3 start,
            float3 end,
            float3 V,
            float3 L,
            bool infinite,
            out float3 transmittance,
            out float3 insctrMie,
            out float3 insctrRayleigh)
		{
			float inf_neg = infinite ? 1 : -1;

			float3 sampleStep = (end - start) / float(SAMPLES_NUMS);
			float3 samplePoint = end - sampleStep;
			float3 sampleLambda = setting.waveLambdaMie + setting.waveLambdaRayleigh + setting.waveLambdaOzone;

			float sampleLength = length(sampleStep);

			float3 scattering = (float3)0;
			float2 lastOpticalDepth = ComputeOpticalDepth(setting, end, V, L, inf_neg);

			for (int i = 1; i < SAMPLES_NUMS; i++, samplePoint -= sampleStep)
			{
				float2 opticalDepth = ComputeOpticalDepth(setting, samplePoint, V, L, inf_neg);

				float3 segment_s = exp(-sampleLambda * (opticalDepth.x + lastOpticalDepth.x));
				float3 segment_t = exp(-sampleLambda * (opticalDepth.y - lastOpticalDepth.y));

				transmittance *= segment_t;

				scattering = scattering * segment_t;
				scattering += exp(-(length(samplePoint) - setting.earthRadius) / setting.rayleighHeight) * segment_s;

				lastOpticalDepth = opticalDepth;
			}

			insctrMie = scattering * setting.waveLambdaMie * sampleLength;
			insctrRayleigh = scattering * setting.waveLambdaRayleigh * sampleLength;
		}


		float ComputeSkyboxChapman(ScatteringParams setting,
            float3 eye,
            float3 V,
            float3 L,
            out float3 transmittance,
            out float3 insctrMie,
            out float3 insctrRayleigh)
		{
			bool neg = true;

			float2 outerIntersections = ComputeRaySphereIntersection(eye, V, setting.earthCenter, setting.earthAtmTopRadius);

			if (outerIntersections.y < 0)
                return 0;

			float2 innerIntersections = ComputeRaySphereIntersection(eye, V, setting.earthCenter, setting.earthRadius);

			if (innerIntersections.x > 0)
			{
				neg = false;
				outerIntersections.y = innerIntersections.x;
			}

			eye -= setting.earthCenter;

			float3 start = eye + V * max(0, outerIntersections.x);
			float3 end = eye + V * outerIntersections.y;

			AerialPerspective(setting, start, end, V, L, neg, transmittance, insctrMie, insctrRayleigh);

			bool intersectionTest = innerIntersections.x < 0 && innerIntersections.y < 0;

			return intersectionTest ? 1 : 0;
		}

		float4 ComputeSkyInscattering(ScatteringParams setting, float3 eye, float3 V, float3 L)
		{
			float3 insctrMie = (float3)0;
			float3 insctrRayleigh = (float3)0;
			float3 insctrOpticalLength = (float3)1;
			float intersectionTest = ComputeSkyboxChapman(setting, eye, V, L, insctrOpticalLength, insctrMie, insctrRayleigh);

			float phaseTheta = dot(V, L);
			float phaseMie = ComputePhaseMie(phaseTheta, setting.mieG);
			float phaseRayleigh = ComputePhaseRayleigh(phaseTheta);
			float phaseNight = 1 - saturate(insctrOpticalLength.x * EPSILON);

			float3 insctrTotalMie = insctrMie * phaseMie;
			float3 insctrTotalRayleigh = insctrRayleigh * phaseRayleigh;

			float3 sky = (insctrTotalMie + insctrTotalRayleigh) * setting.sunRadiance;

			return float4(sky, phaseNight * intersectionTest);
		}


		Varyings Vertex(Appdata input)
		{
            Varyings output = (Varyings)0;

			// Extract the rotational part of the view matrix
			output.pos = mul(PROWL_MATRIX_VP, float4(input.pos, 1));
			output.pos.z = output.pos.w;  // This sets depth to 1.0 so its always in the background

			ScatteringParams setting;

			setting.sunRadius = 500.0;
			setting.sunRadiance = 20.0;
			setting.mieG = 0.76;
			setting.mieHeight = 1200.0;
			setting.rayleighHeight = 8000.0;
			setting.earthRadius = 6360000.0;

			setting.earthAtmTopRadius = setting.earthRadius + (setting.rayleighHeight * 10);
			setting.earthCenter = float3(0, -setting.earthRadius, 0);
			setting.waveLambdaMie = (float3)2e-7;

			// wavelength with 680nm, 550nm, 450nm
			setting.waveLambdaRayleigh = ComputeWaveLambdaRayleigh(float3(680e-9, 550e-9, 450e-9));

			// see https://www.shadertoy.com/view/MllBR2
			setting.waveLambdaOzone = float3(1.36820899679147, 3.31405330400124, 0.13601728252538) * 0.6e-6 * 2.504;

			float3 eye = float3(0, 1000.0, 0);
			float4 sky = ComputeSkyInscattering(setting, eye, normalize(input.pos), normalize(-_SunDir));

			output.vCol = sky.rgb;
			output.vDir = input.pos;

            return output;
		}

		float4 Fragment(Varyings input) : SV_TARGET
		{
			float3 sunDisc = (float3)(1 - step(dot(normalize(input.vDir), normalize(-_SunDir)), 0.9995));

			return float4(sunDisc + (input.vCol * 2), 1);
		}
	ENDHLSL
}
