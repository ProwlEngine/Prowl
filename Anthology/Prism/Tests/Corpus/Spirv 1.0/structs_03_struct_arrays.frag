#version 460 core

layout(location=0) out vec4 FragColor;

// fixed size array of struct instances
struct sample_t {
    vec3 color;
    float weight;
};

void main()
{
    sample_t samples[4];

    for (int i = 0; i < 4; i++) {
        samples[i].color = vec3(float(i) * 0.25, 1.0 - float(i) * 0.25, 0.5);
        samples[i].weight = float(i + 1) * 0.1;
    }

    vec3 accum = vec3(0.0);
    for (int i = 0; i < 4; i++) {
        accum += samples[i].color * samples[i].weight;
    }

    FragColor = vec4(accum, 1.0);
}
