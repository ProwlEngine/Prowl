#version 450

layout(location = 0) in vec3 fsin_normal;
layout(location = 1) in vec3 fsin_color;
layout(location = 2) in vec3 fsin_eyePos;
layout(location = 3) in vec3 fsin_lightVec;
layout(location = 0) out vec4 fsout_color;

void main()
{
    vec3 Eye = normalize(-fsin_eyePos);
    vec3 Reflected = normalize(reflect(-fsin_lightVec, fsin_normal));
    vec4 IAmbient = vec4(0.1f, 0.1f, 0.1f, 1.0f);
    float diff = clamp(dot(fsin_normal, fsin_lightVec), 0.f, 100000);
    vec4 IDiffuse = vec4(diff, diff, diff, diff);
    float specular = 0.75f;
    vec4 ISpecular = vec4(0.0f, 0.0f, 0.0f, 0.0f);
    if (dot(fsin_eyePos, fsin_normal) < 0.0)
    {
        ISpecular = (vec4(0.5f, 0.5f, 0.5f, 1.0f) * pow(clamp(dot(Reflected, Eye), 0.0f, 100000), 16.0f)) * specular;
    }

    fsout_color = (IAmbient + IDiffuse) * vec4(fsin_color, 1.0f) + ISpecular;
}
