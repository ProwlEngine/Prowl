#version 450
layout(location=0) out vec4 o;
void main(){
  mat2x3 a = mat2x3(1.0);   // 2 columns x 3 rows
  mat3x2 b = mat3x2(2.0);   // 3 columns x 2 rows
  mat3 c = a * b;           // mat2x3 * mat3x2 = mat3
  mat2 d = b * a;           // mat3x2 * mat2x3 = mat2
  vec3 e = a * vec2(1,2);
  vec2 f = vec3(1,2,3) * a;
  o = vec4(c[1][1], d[1][1], e.x + f.y, 1.0);
}