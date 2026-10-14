#version 460 core

// The geometric built-ins on constant vectors: glslang folds these in double and narrows only when the
// constant is materialized, so the result must match bit for bit rather than merely closely.

layout(location = 0) out vec4 FragColor;

const vec3 N = vec3(0.0, 1.0, 0.0);
const vec3 I = vec3(0.6, -0.8, 0.0);

void main()
{
    vec3 r = reflect(I, N);
    vec3 rr = refract(I, N, 0.75);
    vec3 ff = faceforward(N, I, N);
    vec3 c = cross(N, vec3(1.0, 0.0, 0.0));
    float d = dot(I, N);
    float len = length(vec3(1.0, 2.0, 2.0));
    float dist = distance(vec3(0.0), vec3(3.0, 4.0, 0.0));
    vec3 nrm = normalize(vec3(1.0, 2.0, 2.0));

    vec3 sum = r + rr + ff + c + nrm;
    FragColor = vec4(sum, d + len + dist);
}
