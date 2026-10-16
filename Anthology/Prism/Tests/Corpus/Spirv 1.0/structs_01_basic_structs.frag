#version 460 core

layout(location=0) out vec4 FragColor;

// basic struct with fields of different types
struct material_t {
    vec3 color;
    float shininess;
    int id;
    bool is_active;
};

void main()
{
    material_t mat;
    mat.color = vec3(0.2, 0.4, 0.6);
    mat.shininess = 32.0;
    mat.id = 1;
    mat.is_active = true;

    mat.color = mat.color + vec3(0.1, 0.1, 0.1);
    mat.shininess = mat.shininess * 2.0;

    float active_mult = mat.is_active ? 1.0 : 0.0;
    vec3 result = mat.color * (mat.shininess / 64.0) * active_mult + vec3(float(mat.id) * 0.01);

    FragColor = vec4(result, 1.0);
}
