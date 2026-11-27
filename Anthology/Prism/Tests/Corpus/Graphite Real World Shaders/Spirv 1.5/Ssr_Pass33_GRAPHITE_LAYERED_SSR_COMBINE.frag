#version 460
#extension GL_GOOGLE_include_directive : enable
#line 1 "Graphite/Ssr"

#extension GL_GOOGLE_cpp_style_line_directive : require
#line 7 "Graphite/Ssr"

#line 1 "Graphite/Fullscreen.glsl"


#pragma variantGRAPHITE_LAYERED
#pragma variantGRAPHITE_NO_SOURCE

#extension GL_EXT_multiview : require

































layout(location = 0) in vec2 vUV;

layout(location = 0) out vec4 outColor;


layout(set = 0, binding = 0) uniform sampler2DArray Graphite_Source;
vec4 Graphite_SampleSource(vec2 uv) { return texture(Graphite_Source, vec3(uv, float(gl_ViewIndex))); }


#line 9 "Graphite/Ssr"
#line 1 "Graphite/Internal/Ssr.glsl"
#pragma variantSSRCOPYRAYCASTBLURRESOLVETEMPORALREPROJECTCOMBINE







#line 1 "Graphite/Internal/PostView.glsl"



#line 1 "Graphite/ViewData.glsl"



struct Graphite_ViewData
{
    mat4 view;
    mat4 projection;
    mat4 viewProjection;
    mat4 inverseViewProjection;
    mat4 previousViewProjection;
    vec4 cameraHigh;
    vec4 cameraLow;
    vec4 previousCameraHigh;
    vec4 previousCameraLow;
    vec4 screen;
    vec4 jitter;
    vec4 time;
    vec4 depth;
    vec4 treeOffset;
};
#line 5 "Graphite/Internal/PostView.glsl"
#line 1 "Graphite/Octahedral.glsl"



vec2 Graphite_OctWrap(vec2 v)
{
    return(1.0 - abs(v.yx)) * vec2(v.x >= 0.0 ? 1.0 : - 1.0, v.y >= 0.0 ? 1.0 : - 1.0);
}


vec2 Graphite_OctEncode(vec3 n)
{
    n /= abs(n.x) + abs(n.y) + abs(n.z);
    return n.z >= 0.0 ? n.xy : Graphite_OctWrap(n.xy);
}

vec3 Graphite_OctDecode(vec2 e)
{
    vec3 n = vec3(e, 1.0 - abs(e.x) - abs(e.y));
    if (n.z < 0.0)
        n.xy = Graphite_OctWrap(n.xy);
    return normalize(n);
}
#line 6 "Graphite/Internal/PostView.glsl"

layout(std430, set = 0, binding = 1) readonly buffer Graphite_PostViews { Graphite_ViewData graphite_PostViews[]; };


Graphite_ViewData Graphite_PostView() { return graphite_PostViews[int(gl_ViewIndex)]; }


bool Graphite_PostOrthographic(Graphite_ViewData view) { return abs(view.projection[2][3]) < 0.5; }


vec3 Graphite_PostViewPosition(Graphite_ViewData view, vec2 uv, float depth)
{
    vec4 p = view.inverseViewProjection * vec4(uv.x * 2.0 - 1.0, 1.0 - uv.y * 2.0, depth, 1.0);
    return mat3(view.view) * (p.xyz / p.w);
}


vec3 Graphite_PostRelativePosition(Graphite_ViewData view, vec2 uv, float depth)
{
    vec4 p = view.inverseViewProjection * vec4(uv.x * 2.0 - 1.0, 1.0 - uv.y * 2.0, depth, 1.0);
    return p.xyz / p.w;
}


vec2 Graphite_PostPreviousUV(Graphite_ViewData view, vec3 relative)
{
    vec3 moved = relative + (view.cameraHigh.xyz - view.previousCameraHigh.xyz) + (view.cameraLow.xyz - view.previousCameraLow.xyz);
    vec4 clip = view.previousViewProjection * vec4(moved, 1.0);
    vec2 ndc = clip.xy / clip.w;
    return vec2(ndc.x * 0.5 + 0.5, 0.5 - ndc.y * 0.5);
}
#line 10 "Graphite/Internal/Ssr.glsl"



float Luminance(vec3 c) { return dot(c, vec3(0.2126, 0.7152, 0.0722)); }

vec3 SsrViewNormal(Graphite_ViewData view, vec2 encoded) { return normalize(mat3(view.view) * Graphite_OctDecode(encoded)); }


vec3 SsrCameraToSurface(Graphite_ViewData view, vec2 uv, vec3 viewPos)
{
    if (Graphite_PostOrthographic(view))
        return normalize(Graphite_PostViewPosition(view, uv, 0.0) - Graphite_PostViewPosition(view, uv, 1.0));
    return normalize(viewPos);
}























































































































































































































































































#line 1 "Graphite/PBR.glsl"






const float PI = 3.14159265359;











float DistributionGGX(vec3 N, vec3 H, float roughness)
{


    roughness = max(roughness, 0.045);

    float a2 = roughness * roughness;
    float NdotH = max(dot(N, H), 0.0);
    float NdotH2 = NdotH * NdotH;

    float denom = (NdotH2 * (a2 - 1.0) + 1.0);
    denom = PI * denom * denom;

    return a2 / denom;
}



float GeometrySchlickGGX(float NdotV, float roughness)
{
    float r = (roughness + 1.0);
    float k = (r * r) / 8.0;

    return NdotV / (NdotV * (1.0 - k) + k);
}



float GeometrySmith(vec3 N, vec3 V, vec3 L, float roughness)
{
    float NdotV = abs(dot(N, V));
    float NdotL = max(dot(N, L), 0.0);
    float ggx2 = GeometrySchlickGGX(NdotV, roughness);
    float ggx1 = GeometrySchlickGGX(NdotL, roughness);

    return ggx1 * ggx2;
}


vec3 FresnelSchlick(float cosTheta, vec3 F0)
{
    return F0 + (1.0 - F0) * exp2(- 9.28 * cosTheta);
}


vec3 EnvBRDF(vec3 F0, float roughness, float NdotV)
{
    vec2 t = clamp(NdotV * (vec2(- 1.443, - 1.49143) + vec2(1.83297, 1.3451) * roughness) + (vec2(0.841227, 0.96635) + vec2(- 0.12654, - 0.705379) * roughness), 0.0, 1.0);
    t *= t;
    float scale = 1.01549 - 0.135605 * roughness + t.x * (0.558371 * roughness - 1.08784);
    float bias = t.y * (0.805868 - 0.770431 * roughness);
    return F0 * scale + bias;
}







float DisneyDiffuse(float NdotV, float NdotL, float LdotH, float perceptualRoughness)
{
    float fd90 = 0.5 + 2.0 * LdotH * LdotH * perceptualRoughness;
    float lightScatter = 1.0 + (fd90 - 1.0) * exp2(- 8.35 * NdotL);
    float viewScatter = 1.0 + (fd90 - 1.0) * exp2(- 8.35 * NdotV);
    return lightScatter * viewScatter / PI;
}








float DistributionGGXAniso(float TdotH, float BdotH, float NdotH, float mt, float mb)
{


    const float minAlpha = 0.045 * 0.045;
    mt = max(mt, minAlpha);
    mb = max(mb, minAlpha);

    float d = TdotH * TdotH / (mt * mt) + BdotH * BdotH / (mb * mb) + NdotH * NdotH;
    return 1.0 / (PI * mt * mb * d * d);
}



float GeometrySmithAniso(
    float TdotV, float BdotV, float NdotV,
    float TdotL, float BdotL, float NdotL,
    float mt, float mb)
{
    float lambdaV = NdotL * sqrt(mt * TdotV * TdotV + mb * BdotV * BdotV + NdotV * NdotV);
    float lambdaL = NdotV * sqrt(mt * TdotL * TdotL + mb * BdotL * BdotL + NdotL * NdotL);
    return 0.5 / max(1e-5, lambdaV + lambdaL);
}











float ApplySpecularAA(float roughness, vec3 worldNormal)
{
    vec3 ddxN = dFdx(worldNormal);
    vec3 ddyN = dFdy(worldNormal);
    float variance = 0.5 * (dot(ddxN, ddxN) + dot(ddyN, ddyN));
    float kernelRoughnessSq = min(2.0 * variance, 0.1);
    float squaredRoughness = clamp(roughness * roughness + kernelRoughnessSq, 0.0, 1.0);
    return sqrt(squaredRoughness);
}












vec2 ParallaxOcclusionMapping(sampler2D heightTex, vec2 uv, vec3 viewDirTS, float heightScale, int linearSteps)
{

    float slopeDamp = 1.0 - clamp(dot(viewDirTS, vec3(0.0, 0.0, 1.0)), 0.0, 1.0);


    vec2 vMaxOffset = (viewDirTS.xy / - viewDirTS.z) * heightScale * (1.0 - slopeDamp * slopeDamp);

    float fStepSize = 1.0 / float(linearSteps);
    vec2 finalStepSize = fStepSize * vMaxOffset;


    vec2 dx = dFdx(uv);
    vec2 dy = dFdy(uv);


    float fCurrRayHeight = 1.0;
    vec2 vCurrOffset = vec2(0.0);
    vec2 vLastOffset = vec2(0.0);
    float fLastSampledHeight = 1.0;
    float fCurrSampledHeight = 1.0;

    for (int i = 0; i < linearSteps; i ++)
    {
        fCurrSampledHeight = textureGrad(heightTex, uv + vCurrOffset, dx, dy).g;

        if (fCurrSampledHeight > fCurrRayHeight)
            break;

        fCurrRayHeight -= fStepSize;
        vLastOffset = vCurrOffset;
        vCurrOffset += finalStepSize;
        fLastSampledHeight = fCurrSampledHeight;
    }


    float pt0 = fCurrRayHeight + fStepSize;
    float pt1 = fCurrRayHeight;
    float delta0 = pt0 - fLastSampledHeight;
    float delta1 = pt1 - fCurrSampledHeight;

    for (int j = 0; j < 3; j ++)
    {


        float secantDenom = delta1 - delta0;
        if (! (abs(secantDenom) > 1e-8)) break;

        float intersectionHeight = (pt0 * delta1 - pt1 * delta0) / secantDenom;
        vCurrOffset = (1.0 - intersectionHeight) * finalStepSize * float(linearSteps);

        fCurrSampledHeight = textureGrad(heightTex, uv + vCurrOffset, dx, dy).g;

        float delta = intersectionHeight - fCurrSampledHeight;
        if (delta < 0.0)
        {
            delta1 = delta;
            pt1 = intersectionHeight;
        }
        else
        {
            delta0 = delta;
            pt0 = intersectionHeight;
        }
    }

    return uv + vCurrOffset;
}














vec3 CalculateTranslucency(vec3 lightDir, vec3 viewDir, vec3 normal,
                           float translucency, float scatteringPower,
                           float distortion, float scale, vec3 lightColor)
{
    if (translucency <= 0.0) return vec3(0.0);

    vec3 lightScattering = vec3(0.0);

    if (scatteringPower < 0.001)
    {


        float wrap = 0.5;
        float wrappedNdotL = clamp( (dot(- normal, lightDir) + wrap) / ( (1.0 + wrap) * (1.0 + wrap)), 0.0, 1.0);


        float VdotL = clamp(dot(viewDir, - lightDir), 0.0, 1.0);
        float a2 = 0.49;
        float d = (VdotL * a2 - VdotL) * VdotL + 1.0;
        float GGX = (a2 / PI) / (d * d);

        lightScattering = wrappedNdotL * GGX * translucency * lightColor;
    }
    else
    {


        vec3 transLightDir = lightDir + normal * distortion;
        float transDot = dot(- transLightDir, viewDir);
        transDot = exp2(clamp(transDot, 0.0, 1.0) * scatteringPower - scatteringPower) * translucency;

        lightScattering = transDot * lightColor;
    }

    return lightScattering * scale;
}
#line 304 "Graphite/Internal/Ssr.glsl"

layout(set = 0, binding = 2) uniform sampler2DArray _ReflectionBuffer;
layout(set = 0, binding = 3) uniform sampler2DArray _CameraDepthTexture;
layout(set = 0, binding = 4) uniform sampler2DArray _CameraNormalsTexture;
layout(set = 0, binding = 5) uniform sampler2DArray _MaterialTexture;
layout(set = 0, binding = 6, std140) uniform _GraphiteGlobal { int _UseFresnel; };

void main()
{
    Graphite_ViewData view = Graphite_PostView();
    vec4 scene = Graphite_SampleSource(vUV);
    vec3 sceneColor = scene.rgb;
    vec4 reflection = texture(_ReflectionBuffer, vec3(vUV, float(gl_ViewIndex)));
    vec2 material = texture(_MaterialTexture, vec3(vUV, float(gl_ViewIndex))).rg;
    vec3 viewNormal = SsrViewNormal(view, texture(_CameraNormalsTexture, vec3(vUV, float(gl_ViewIndex))).xy);
    float depth = max(texture(_CameraDepthTexture, vec3(vUV, float(gl_ViewIndex))).r, 1e-7);
    vec3 V = - SsrCameraToSurface(view, vUV, Graphite_PostViewPosition(view, vUV, depth));
    float NdotV = clamp(dot(viewNormal, V), 0.0, 1.0);


    vec3 approxAlbedo = sceneColor / (1.0 + sceneColor);
    vec3 F0 = mix(vec3(0.04), approxAlbedo, material.g);
    float mask = reflection.a * reflection.a;
    vec3 refl = reflection.rgb;
    if (_UseFresnel == 1)
        refl *= EnvBRDF(F0, material.r, NdotV);


    refl = min(refl, vec3(8.0));
    outColor = vec4(sceneColor + refl * mask, scene.a);
}





#line 10 "Graphite/Ssr"

