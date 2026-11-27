#version 460
#extension GL_GOOGLE_include_directive : enable
#line 1 "Graphite/Unlit/Prepass"

#extension GL_GOOGLE_cpp_style_line_directive : require
#line 16 "Graphite/Unlit/Prepass"




#line 26 "Graphite/Unlit/Prepass"

#line 1 "Graphite/Standard/Standard.glsl"

















#pragma variant_SURFACEOPAQUECUTOUTTRANSPARENT
#pragma variant_INSTANCE_TINT






















layout(set = 0, binding = 0, std140) uniform _GraphiteGlobal { vec2 _Tiling; vec2 _Offset; vec4 _MainColor; int _MainTexUV; };



layout(set = 0, binding = 1) uniform sampler2D _MainTex;



















































                        layout(location = 0) in vec2 vUV;
                        layout(location = 1) in vec2 vUV1;















void Graphite_StandardTangentFrame(vec3 worldNormal, vec3 worldTangent, float handedness, bool hasTangent, out vec3 tangent, out vec3 bitangent)
{
    float sign = handedness < 0.0 ? - 1.0 : 1.0;
    if (hasTangent)
    {
        tangent = worldTangent;
        bitangent = cross(tangent, worldNormal) * sign;
        if (dot(bitangent, bitangent) > 0.000001)
            return;
    }
    tangent = abs(worldNormal.y) < 0.999 ? normalize(cross(worldNormal, vec3(0.0, 1.0, 0.0))) : normalize(cross(worldNormal, vec3(1.0, 0.0, 0.0)));
    bitangent = cross(tangent, worldNormal) * sign;
}








































struct Graphite_SurfaceData
{
    vec2 UV;
    vec2 UV1;
    vec4 VertexColor;
    vec3 WorldPos;
    vec3 ViewDir;
    vec3 GeometryNormal;
    vec3 BaseColor;
    float Alpha;
    vec3 Normal;
    float Metallic;
    float Roughness;
    float Occlusion;
    vec3 Emission;
};





Graphite_SurfaceData Graphite_NewSurfaceData(vec2 uv, vec2 uv1, vec3 baseColor, float alpha)
{
    Graphite_SurfaceData s;
    s.UV = uv;
    s.UV1 = uv1;
    s.VertexColor = vec4(1.0);
    s.WorldPos = vec3(0.0);
    s.ViewDir = vec3(0.0, 0.0, 1.0);
    s.GeometryNormal = vec3(0.0, 1.0, 0.0);
    s.BaseColor = baseColor;
    s.Alpha = alpha;
    s.Normal = vec3(0.0, 1.0, 0.0);
    s.Metallic = 0.0;
    s.Roughness = 1.0;
    s.Occlusion = 1.0;
    s.Emission = vec3(0.0);
    return s;
}

















































































































































































void Graphite_StandardFragment()
{











}




#line 28 "Graphite/Unlit/Prepass"

void main() { Graphite_StandardFragment(); }
