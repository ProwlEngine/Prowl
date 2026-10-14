#version 460

struct Light {
    vec3 pos;
    float intensity;
};

layout(location = 0) in int inIdx;

void main() {
    Light lights[2] = Light[2](
        Light(vec3(0.0), 1.0),
        Light(vec3(1.0), 2.0)
    );
    Light l = lights[inIdx];
    gl_Position = vec4(l.pos, l.intensity);
}
