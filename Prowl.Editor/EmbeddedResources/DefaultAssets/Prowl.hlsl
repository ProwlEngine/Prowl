﻿#ifndef SHADER_PROWL
#define SHADER_PROWL

#include "ShaderVariables.hlsl"

#define PROWL_PI            3.14159265359f
#define PROWL_TWO_PI        6.28318530718f
#define PROWL_FOUR_PI       12.56637061436f
#define PROWL_INV_PI        0.31830988618f
#define PROWL_INV_TWO_PI    0.15915494309f
#define PROWL_INV_FOUR_PI   0.07957747155f
#define PROWL_HALF_PI       1.57079632679f
#define PROWL_INV_HALF_PI   0.636619772367f




// Fog ===========================================================
// Macro to declare fog coordinates in vertex shader output struct
#define PROWL_FOG_COORDS(idx) float fogCoord : TEXCOORD##idx;

// Initialize fog coords in vertex shader
#define PROWL_TRANSFER_FOG(o, clipPos) \
    { \
        o.fogCoord = clipPos.z; \
    }

// Apply fog in fragment/pixel shader - Should match Unity's fog calculation
#define PROWL_APPLY_FOG(i, col) \
    { \
        float prowlFog = 0.0; \
            prowlFog += (i.fogCoord * prowl_FogParams.z + prowl_FogParams.w) * prowl_FogStates.x; \
            prowlFog += exp2(-i.fogCoord * prowl_FogParams.y) * prowl_FogStates.y; \
            prowlFog += exp2(-i.fogCoord * i.fogCoord * prowl_FogParams.x * prowl_FogParams.x) * prowl_FogStates.z; \
        col.rgb = lerp(prowl_FogColor.rgb, col.rgb, saturate(prowlFog)); \
    }
// ============================================================================




// Ambient Lighting ===========================================================
float3 CalculateAmbient(float3 worldNormal)
{
    float3 ambient = 0;
    
    // Uniform ambient
    ambient += prowl_AmbientColor.rgb * prowl_AmbientMode.x;
    
    // Hemisphere ambient
    float upDot = dot(worldNormal, float3(0, 1, 0));
    ambient += lerp(prowl_AmbientGroundColor.rgb, prowl_AmbientSkyColor.rgb, upDot * 0.5 + 0.5) * prowl_AmbientMode.y;
    
    return ambient;
}

#define PROWL_AMBIENT(worldNormal, outColor) \
    { \
        outColor.rgb *= CalculateAmbient(worldNormal.xyz); \
    }
// ============================================================================



// Colors ===========================================================
// http://chilliant.blogspot.com.au/2012/08/srgb-approximations-for-hlsl.html?m=1
float3 LinearToGammaSpace(float3 lin)
{
	return max(1.055 * pow(max(lin, float3(0.0, 0.0, 0.0)), 0.416666667) - 0.055, 0.0);
}

// http://chilliant.blogspot.com.au/2012/08/srgb-approximations-for-hlsl.html?m=1
float3 GammaToLinearSpace(float3 gamma)
{
    return gamma * (gamma * (gamma * 0.305306011 + 0.682171111) + 0.012522878);
}
// ============================================================================



float LinearizeDepth(float depth, float near, float far) 
{
	float z = depth * 2.0 - 1.0; // Back to NDC [-1,1] range
	return (2.0 * near * far) / (far + near - z * (far - near));
}

float GetFovFromProjectionMatrix(float4x4 proj)
{
	// proj[1][1] is M11, the Y scale
	// FOV = 2 * atan(1/M11)
	return 2.0 * atan(1.0 / proj[1][1]);
}

float4x4 inverse(float4x4 m) {
	float n11 = m[0][0], n12 = m[1][0], n13 = m[2][0], n14 = m[3][0];
	float n21 = m[0][1], n22 = m[1][1], n23 = m[2][1], n24 = m[3][1];
	float n31 = m[0][2], n32 = m[1][2], n33 = m[2][2], n34 = m[3][2];
	float n41 = m[0][3], n42 = m[1][3], n43 = m[2][3], n44 = m[3][3];

	float t11 = n23 * n34 * n42 - n24 * n33 * n42 + n24 * n32 * n43 - n22 * n34 * n43 - n23 * n32 * n44 + n22 * n33 * n44;
	float t12 = n14 * n33 * n42 - n13 * n34 * n42 - n14 * n32 * n43 + n12 * n34 * n43 + n13 * n32 * n44 - n12 * n33 * n44;
	float t13 = n13 * n24 * n42 - n14 * n23 * n42 + n14 * n22 * n43 - n12 * n24 * n43 - n13 * n22 * n44 + n12 * n23 * n44;
	float t14 = n14 * n23 * n32 - n13 * n24 * n32 - n14 * n22 * n33 + n12 * n24 * n33 + n13 * n22 * n34 - n12 * n23 * n34;

	float det = n11 * t11 + n21 * t12 + n31 * t13 + n41 * t14;
	float idet = 1.0f / det;

	float4x4 ret;

	ret[0][0] = t11 * idet;
	ret[0][1] = (n24 * n33 * n41 - n23 * n34 * n41 - n24 * n31 * n43 + n21 * n34 * n43 + n23 * n31 * n44 - n21 * n33 * n44) * idet;
	ret[0][2] = (n22 * n34 * n41 - n24 * n32 * n41 + n24 * n31 * n42 - n21 * n34 * n42 - n22 * n31 * n44 + n21 * n32 * n44) * idet;
	ret[0][3] = (n23 * n32 * n41 - n22 * n33 * n41 - n23 * n31 * n42 + n21 * n33 * n42 + n22 * n31 * n43 - n21 * n32 * n43) * idet;

	ret[1][0] = t12 * idet;
	ret[1][1] = (n13 * n34 * n41 - n14 * n33 * n41 + n14 * n31 * n43 - n11 * n34 * n43 - n13 * n31 * n44 + n11 * n33 * n44) * idet;
	ret[1][2] = (n14 * n32 * n41 - n12 * n34 * n41 - n14 * n31 * n42 + n11 * n34 * n42 + n12 * n31 * n44 - n11 * n32 * n44) * idet;
	ret[1][3] = (n12 * n33 * n41 - n13 * n32 * n41 + n13 * n31 * n42 - n11 * n33 * n42 - n12 * n31 * n43 + n11 * n32 * n43) * idet;

	ret[2][0] = t13 * idet;
	ret[2][1] = (n14 * n23 * n41 - n13 * n24 * n41 - n14 * n21 * n43 + n11 * n24 * n43 + n13 * n21 * n44 - n11 * n23 * n44) * idet;
	ret[2][2] = (n12 * n24 * n41 - n14 * n22 * n41 + n14 * n21 * n42 - n11 * n24 * n42 - n12 * n21 * n44 + n11 * n22 * n44) * idet;
	ret[2][3] = (n13 * n22 * n41 - n12 * n23 * n41 - n13 * n21 * n42 + n11 * n23 * n42 + n12 * n21 * n43 - n11 * n22 * n43) * idet;

	ret[3][0] = t14 * idet;
	ret[3][1] = (n13 * n24 * n31 - n14 * n23 * n31 + n14 * n21 * n33 - n11 * n24 * n33 - n13 * n21 * n34 + n11 * n23 * n34) * idet;
	ret[3][2] = (n14 * n22 * n31 - n12 * n24 * n31 - n14 * n21 * n32 + n11 * n24 * n32 + n12 * n21 * n34 - n11 * n22 * n34) * idet;
	ret[3][3] = (n12 * n23 * n31 - n13 * n22 * n31 + n13 * n21 * n32 - n11 * n23 * n32 - n12 * n21 * n33 + n11 * n22 * n33) * idet;

	return ret;
}

// Function to reconstruct view-space position from depth
float3 GetViewPos(float2 uv, float depth, float4x4 projectionMatrix)
{
    // Convert to NDC space
    float4 clipPos = float4(uv * 2.0 - 1.0, depth, 1.0);
    
    // Reconstruct view space position
    float4 viewPos = mul(inverse(projectionMatrix), clipPos);
    return viewPos.xyz / viewPos.w;
}

#endif
