#version 460
#extension GL_NV_ray_tracing : require
layout(location = 0) rayPayloadInNV vec4 payload;
layout(location = 1) callableDataNV vec3 cdata;
hitAttributeNV vec2 attribs;
void main()
{
    payload = vec4(gl_WorldRayOriginNV + gl_WorldRayDirectionNV * gl_HitTNV, float(gl_InstanceCustomIndexNV));
    executeCallableNV(0u, 1);
    payload.xyz += cdata;
}
