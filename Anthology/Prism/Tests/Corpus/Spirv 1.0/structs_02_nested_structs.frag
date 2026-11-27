#version 460 core

layout(location=0) out vec4 FragColor;

// struct nested inside another struct
struct inner_t {
    vec3 base_color;
    float intensity;
};

struct outer_t {
    inner_t light;
    float falloff;
};

void main()
{
    outer_t scene;
    scene.light.base_color = vec3(1.0, 0.8, 0.6);
    scene.light.intensity = 2.0;
    scene.falloff = 0.5;

    vec3 shaded = scene.light.base_color * scene.light.intensity * scene.falloff;

    FragColor = vec4(shaded, 1.0);
}
