#version 460 core

layout(location=0) out vec4 FragColor;

struct particle_t {
    vec3 position;
    float energy;
};

// takes struct by value, returns a modified copy
particle_t apply_boost(particle_t p, float amount)
{
    particle_t result = p;
    result.position = result.position + vec3(0.0, amount, 0.0);
    result.energy = result.energy * amount;
    return result;
}

void main()
{
    particle_t p;
    p.position = vec3(0.5, 0.5, 0.5);
    p.energy = 1.5;

    particle_t boosted = apply_boost(p, 2.0);

    FragColor = vec4(boosted.position * boosted.energy * 0.1, 1.0);
}
