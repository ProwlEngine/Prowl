#version 460
#extension GL_EXT_ray_tracing : require

layout(location = 0) rayPayloadInEXT vec4 payload;
hitAttributeEXT vec2 attribs;

void main()
{
    vec3 hitPos = gl_WorldRayOriginEXT + gl_WorldRayDirectionEXT * gl_HitTEXT;
    vec3 objPos = gl_ObjectRayOriginEXT + gl_ObjectRayDirectionEXT * gl_RayTmaxEXT;
    float bary = attribs.x + attribs.y;
    float ids = float(gl_InstanceCustomIndexEXT + gl_GeometryIndexEXT + int(gl_HitKindEXT));
    payload = vec4(hitPos + objPos, bary + ids + gl_RayTminEXT);
}
