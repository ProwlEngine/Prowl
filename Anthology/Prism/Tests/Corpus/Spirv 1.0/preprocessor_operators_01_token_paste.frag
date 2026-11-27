#version 450
#define CONCAT(a, b) a ## b
#define PREFIX(name) prefix_ ## name
layout(location = 0) out vec4 color;
void main()
{
    float CONCAT(my, Var) = 2.0;
    int PREFIX(count) = 3;
    color = vec4(myVar * float(prefix_count));
}
