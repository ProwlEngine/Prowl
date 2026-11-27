#version 460
#extension GL_EXT_ray_tracing : require
struct Payload { vec3 c; int depth; };
layout(location=0) rayPayloadInEXT Payload pl;
hitAttributeEXT vec2 bary;
layout(location=3) callableDataEXT float cdd;
void main(){ pl.c = vec3(1.0-bary.x-bary.y, bary) * gl_HitTEXT + gl_WorldRayOriginEXT + gl_ObjectRayDirectionEXT + gl_WorldRayDirectionEXT + gl_ObjectRayOriginEXT;
 pl.depth = gl_PrimitiveID + gl_InstanceID + gl_InstanceCustomIndexEXT + gl_GeometryIndexEXT + int(gl_HitKindEXT) + int(gl_IncomingRayFlagsEXT);
 mat4x3 o2w = gl_ObjectToWorldEXT; mat3x4 w2o = gl_WorldToObject3x4EXT; pl.c += o2w[3] + w2o[0].xyz + vec3(gl_RayTminEXT); executeCallableEXT(0, 3); pl.c.x += cdd; }
