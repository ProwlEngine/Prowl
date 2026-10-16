#version 460 core

layout(location=0) out vec4 FragColor;

layout(push_constant) uniform PushConstants
{
    vec3 u_a;
    vec3 u_b;
    vec3 u_n;
    float u_eta;
};

void main() {
    vec3 a = u_a;
    vec3 b = u_b;
    vec3 n = normalize(u_n);

    float r_length = length(a);
    float r_distance = distance(a, b);
    float r_dot = dot(a, b);
    vec3 r_cross = cross(a, b);
    vec3 r_normalize = normalize(a);
    vec3 r_faceforward = faceforward(n, a, b);
    vec3 r_reflect = reflect(normalize(a), n);
    vec3 r_refract = refract(normalize(a), n, u_eta);

    vec3 total = r_cross + r_normalize + r_faceforward + r_reflect + r_refract +
                 vec3(r_length + r_distance + r_dot);

    FragColor = vec4(total, 1.0);
}
