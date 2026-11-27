#version 460
#extension GL_EXT_ray_tracing : require

layout(location = 0) rayPayloadInEXT vec4 payload;
hitAttributeEXT vec2 attribs;

void main() {
    vec3 bary = vec3(1.0 - attribs.x - attribs.y, attribs.x, attribs.y);

    vec3 worldPos = gl_WorldRayOriginEXT + gl_WorldRayDirectionEXT * gl_HitTEXT;
    vec3 objectPos = gl_WorldToObjectEXT * vec4(worldPos, 1.0);
    vec3 roundTripWorld = gl_ObjectToWorldEXT * vec4(objectPos, 1.0);

    float ids = float(gl_InstanceCustomIndexEXT + gl_PrimitiveID);
    payload = vec4(bary + roundTripWorld, ids);
}
