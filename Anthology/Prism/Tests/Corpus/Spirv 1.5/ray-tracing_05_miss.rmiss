#version 460
#extension GL_EXT_ray_tracing : require

layout(location = 0) rayPayloadInEXT vec4 payload;

void main() {
    vec3 direction = gl_WorldRayDirectionEXT;
    float t = 0.5 * (direction.y + 1.0);
    vec3 sky = mix(vec3(1.0), vec3(0.3, 0.5, 1.0), t);
    payload = vec4(sky, 1.0);
}
