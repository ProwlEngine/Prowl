#version 460 core

layout(location=0) out vec4 FragColor;

struct material_input
{
    vec3 color;
    float intensity;
};

struct material_output
{
    vec3 shaded_color;
};

material_output shade(material_input mat)
{
    material_output result;
    result.shaded_color = mat.color * mat.intensity;
    return result;
}

void main()
{
    material_input mat;
    mat.color = vec3(0.8, 0.4, 0.2);
    mat.intensity = 1.5;

    material_output shaded = shade(mat);

    FragColor = vec4(shaded.shaded_color, 1.0);
}
