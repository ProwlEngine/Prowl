Shader "Default/ProceduralSky"

Pass "ProceduralSky"
{
    // Rasterizer culling mode
    Cull None
	Blend Override
	
	DepthStencil
	{
		DepthWrite Off
		DepthTest Off
	}
	
	Inputs
	{
		VertexInput 
        {
            Position // Input location 0
        }
		
        // Set 0
        Set
        {
            // Binding 0
            Buffer DefaultUniforms
            {
                Mat_V Matrix4x4
                Mat_P Matrix4x4
                Mat_ObjectToWorld Matrix4x4
                Mat_WorldToObject Matrix4x4
                Mat_MVP Matrix4x4
				Time Float
				_SunDir Vector3
            }
		}
	}

	PROGRAM VERTEX
		layout(location = 0) in vec3 vertexPosition;
		
		layout(set = 0, binding = 0, std140) uniform DefaultUniforms
		{
			mat4 Mat_V;
			mat4 Mat_P;
			mat4 Mat_ObjectToWorld;
			mat4 Mat_WorldToObject;
			mat4 Mat_MVP;
			float Time;
			vec3 _SunDir;
		};
		
		
		layout(location = 0) out vec3 vCol;
		layout(location = 1) out vec3 vDir;
		
		#define PI 3.1415926535f
		#define PI_2 (3.1415926535f * 2.0)
		
		#define EPSILON 1e-5
		
		#define SAMPLES_NUMS 16
		
		float saturate(float x){ return clamp(x, 0.0, 1.0); }
		
		struct ScatteringParams
		{
			float sunRadius;
			float sunRadiance;
		
			float mieG;
			float mieHeight;
		
			float rayleighHeight;
		
			vec3 waveLambdaMie;
			vec3 waveLambdaOzone;
			vec3 waveLambdaRayleigh;
		
			float earthRadius;
			float earthAtmTopRadius;
			vec3 earthCenter;
		};
		
		vec3 ComputeSphereNormal(vec2 coord, float phiStart, float phiLength, float thetaStart, float thetaLength)
		{
			vec3 normal;
			normal.x = -sin(thetaStart + coord.y * thetaLength) * sin(phiStart + coord.x * phiLength);
			normal.y = -cos(thetaStart + coord.y * thetaLength);
			normal.z = -sin(thetaStart + coord.y * thetaLength) * cos(phiStart + coord.x * phiLength);
			return normalize(normal);
		}
		
		vec2 ComputeRaySphereIntersection(vec3 position, vec3 dir, vec3 center, float radius)
		{
			vec3 origin = position - center;
			float B = dot(origin, dir);
			float C = dot(origin, origin) - radius * radius;
			float D = B * B - C;
		
			vec2 minimaxIntersections;
			if (D < 0.0)
			{
				minimaxIntersections = vec2(-1.0, -1.0);
			}
			else
			{
				D = sqrt(D);
				minimaxIntersections = vec2(-B - D, -B + D);
			}
		
			return minimaxIntersections;
		}
		
		vec3 ComputeWaveLambdaRayleigh(vec3 lambda)
		{
			const float n = 1.0003;
			const float N = 2.545E25;
			const float pn = 0.035;
			const float n2 = n * n;
			const float pi3 = PI * PI * PI;
			const float rayleighConst = (8.0 * pi3 * pow(n2 - 1.0,2.0)) / (3.0 * N) * ((6.0 + 3.0 * pn) / (6.0 - 7.0 * pn));
			return rayleighConst / (lambda * lambda * lambda * lambda);
		}
		
		float ComputePhaseMie(float theta, float g)
		{
			float g2 = g * g;
			return (1.0 - g2) / pow(1.0 + g2 - 2.0 * g * saturate(theta), 1.5) / (4.0 * PI);
		}
		
		float ComputePhaseRayleigh(float theta)
		{
			float theta2 = theta * theta;
			return (theta2 * 0.75 + 0.75) / (4.0 * PI);
		}
		
		float ChapmanApproximation(float X, float h, float cosZenith)
		{
			float c = sqrt(X + h);
			float c_exp_h = c * exp(-h);
		
			if (cosZenith >= 0.0)
			{
				return c_exp_h / (c * cosZenith + 1.0);
			}
			else
			{
				float x0 = sqrt(1.0 - cosZenith * cosZenith) * (X + h);
				float c0 = sqrt(x0);
		
				return 2.0 * c0 * exp(X - x0) - c_exp_h / (1.0 - c * cosZenith);
			}
		}
		
		float GetOpticalDepthSchueler(float h, float H, float earthRadius, float cosZenith)
		{
			return H * ChapmanApproximation(earthRadius / H, h / H, cosZenith);
		}
		
		vec3 GetTransmittance(ScatteringParams setting, vec3 L, vec3 V)
		{
			float ch = GetOpticalDepthSchueler(L.y, setting.rayleighHeight, setting.earthRadius, V.y);
			return exp(-(setting.waveLambdaMie + setting.waveLambdaRayleigh) * ch);
		}
		
		vec2 ComputeOpticalDepth(ScatteringParams setting, vec3 samplePoint, vec3 V, vec3 L, float neg)
		{
			float rl = length(samplePoint);
			float h = rl - setting.earthRadius;
			vec3 r = samplePoint / rl;
		
			float cos_chi_sun = dot(r, L);
			float cos_chi_ray = dot(r, V * neg);
		
			float opticalDepthSun = GetOpticalDepthSchueler(h, setting.rayleighHeight, setting.earthRadius, cos_chi_sun);
			float opticalDepthCamera = GetOpticalDepthSchueler(h, setting.rayleighHeight, setting.earthRadius, cos_chi_ray) * neg;
		
			return vec2(opticalDepthSun, opticalDepthCamera);
		}
		
		void AerialPerspective(ScatteringParams setting, vec3 start, vec3 end, vec3 V, vec3 L, bool infinite, out vec3 transmittance, out vec3 insctrMie, out vec3 insctrRayleigh)
		{
			float inf_neg = infinite ? 1.0 : -1.0;
		
			vec3 sampleStep = (end - start) / float(SAMPLES_NUMS);
			vec3 samplePoint = end - sampleStep;
			vec3 sampleLambda = setting.waveLambdaMie + setting.waveLambdaRayleigh + setting.waveLambdaOzone;
		
			float sampleLength = length(sampleStep);
		
			vec3 scattering = vec3(0.0);
			vec2 lastOpticalDepth = ComputeOpticalDepth(setting, end, V, L, inf_neg);
		
			for (int i = 1; i < SAMPLES_NUMS; i++, samplePoint -= sampleStep)
			{
				vec2 opticalDepth = ComputeOpticalDepth(setting, samplePoint, V, L, inf_neg);
		
				vec3 segment_s = exp(-sampleLambda * (opticalDepth.x + lastOpticalDepth.x));
				vec3 segment_t = exp(-sampleLambda * (opticalDepth.y - lastOpticalDepth.y));
				
				transmittance *= segment_t;
				
				scattering = scattering * segment_t;
				scattering += exp(-(length(samplePoint) - setting.earthRadius) / setting.rayleighHeight) * segment_s;
		
				lastOpticalDepth = opticalDepth;
			}
		
			insctrMie = scattering * setting.waveLambdaMie * sampleLength;
			insctrRayleigh = scattering * setting.waveLambdaRayleigh * sampleLength;
		}
		
		float ComputeSkyboxChapman(ScatteringParams setting, vec3 eye, vec3 V, vec3 L, out vec3 transmittance, out vec3 insctrMie, out vec3 insctrRayleigh)
		{
			bool neg = true;
		
			vec2 outerIntersections = ComputeRaySphereIntersection(eye, V, setting.earthCenter, setting.earthAtmTopRadius);
			if (outerIntersections.y < 0.0) return 0.0;
		
			vec2 innerIntersections = ComputeRaySphereIntersection(eye, V, setting.earthCenter, setting.earthRadius);
			if (innerIntersections.x > 0.0)
			{
				neg = false;
				outerIntersections.y = innerIntersections.x;
			}
		
			eye -= setting.earthCenter;
		
			vec3 start = eye + V * max(0.0, outerIntersections.x);
			vec3 end = eye + V * outerIntersections.y;
		
			AerialPerspective(setting, start, end, V, L, neg, transmittance, insctrMie, insctrRayleigh);
		
			bool intersectionTest = innerIntersections.x < 0.0 && innerIntersections.y < 0.0;
			return intersectionTest ? 1.0 : 0.0;
		}
		
		vec4 ComputeSkyInscattering(ScatteringParams setting, vec3 eye, vec3 V, vec3 L)
		{
			vec3 insctrMie = vec3(0.0);
			vec3 insctrRayleigh = vec3(0.0);
			vec3 insctrOpticalLength = vec3(1.0);
			float intersectionTest = ComputeSkyboxChapman(setting, eye, V, L, insctrOpticalLength, insctrMie, insctrRayleigh);
		
			float phaseTheta = dot(V, L);
			float phaseMie = ComputePhaseMie(phaseTheta, setting.mieG);
			float phaseRayleigh = ComputePhaseRayleigh(phaseTheta);
			float phaseNight = 1.0 - saturate(insctrOpticalLength.x * EPSILON);
		
			vec3 insctrTotalMie = insctrMie * phaseMie;
			vec3 insctrTotalRayleigh = insctrRayleigh * phaseRayleigh;
		
			vec3 sky = (insctrTotalMie + insctrTotalRayleigh) * setting.sunRadiance;
		
			return vec4(sky, phaseNight * intersectionTest);
		}

		
		void main() 
		{
			// Extract the rotational part of the view matrix
			mat4 rotViewMatrix = Mat_V;
			rotViewMatrix[3] = vec4(0.0, 0.0, 0.0, 1.0);  // Remove the translation component
			gl_Position = Mat_P * rotViewMatrix * vec4(vertexPosition, 1.0);
			
			ScatteringParams setting;
			setting.sunRadius = 500.0;
			setting.sunRadiance = 20.0;
			setting.mieG = 0.76;
			setting.mieHeight = 1200.0;
			setting.rayleighHeight = 8000.0;
			setting.earthRadius = 6360000.0;
			setting.earthAtmTopRadius = setting.earthRadius + (setting.rayleighHeight * 10.0);
			setting.earthCenter = vec3(0, -setting.earthRadius, 0);
			setting.waveLambdaMie = vec3(2e-7);
			
			// wavelength with 680nm, 550nm, 450nm
			setting.waveLambdaRayleigh = ComputeWaveLambdaRayleigh(vec3(680e-9, 550e-9, 450e-9));
			
			// see https://www.shadertoy.com/view/MllBR2
			setting.waveLambdaOzone = vec3(1.36820899679147, 3.31405330400124, 0.13601728252538) * 0.6e-6 * 2.504;
			
			vec3 eye = vec3(0,1000.0,0);
			vec4 sky = ComputeSkyInscattering(setting, eye, normalize(vertexPosition), normalize(-_SunDir));
			
			vCol = sky.rgb;
			vDir = vertexPosition;
		}
	ENDPROGRAM

	PROGRAM FRAGMENT	
		layout(location = 0) in vec3 vCol;
		layout(location = 1) in vec3 vDir;
		
		layout(set = 0, binding = 0, std140) uniform DefaultUniforms
		{
			mat4 Mat_V;
			mat4 Mat_P;
			mat4 Mat_ObjectToWorld;
			mat4 Mat_WorldToObject;
			mat4 Mat_MVP;
			float Time;
			vec3 _SunDir;
		};

		layout(location = 0) out vec4 OutputColor;

		void main()
		{
			vec3 sunDisc = vec3(step(0.9, dot(normalize(vec3(0, 1, 1)), -_SunDir)));
			sunDisc = vec3(1.0 - step(dot(normalize(vDir), normalize(-_SunDir)), 0.9995));
			OutputColor = vec4(sunDisc + (vCol * 2.0), 1.0);
		}
	ENDPROGRAM
}