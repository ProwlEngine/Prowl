#version 450
#extension GL_ARB_sparse_texture2 : require
layout(set=0,binding=0) uniform isampler2D s;
layout(set=0,binding=1) uniform usampler2DArray ua;
layout(set=0,binding=2, r32i) uniform iimage2D im;
layout(input_attachment_index=0, set=0, binding=3) uniform usubpassInput sp;
layout(set=0,binding=4) uniform sampler2D f;
layout(location=0) in vec2 uv;
layout(location=0) out ivec4 o;
void main() {
  ivec4 a = texture(s, uv) + textureLod(s, uv, 1.0) + textureGrad(s, uv, vec2(0.1), vec2(0.2)) + textureOffset(s, uv, ivec2(1)) + texelFetch(s, ivec2(0), 0)
          + textureGather(s, uv) + textureGather(s, uv, 1) + textureProj(s, vec3(uv, 1.0)) + textureGatherOffset(s, uv, ivec2(1));
  ivec4 t; sparseTextureARB(s, uv, t); a += t; sparseTexelFetchARB(s, ivec2(0), 0, t); a += t; sparseImageLoadARB(im, ivec2(0), t); a += t;
  a += ivec4(texture(ua, vec3(uv, 0.0))) + imageLoad(im, ivec2(1)) + ivec4(subpassLoad(sp)) + ivec4(texture(f, uv));
  imageStore(im, ivec2(2), a); imageAtomicAdd(im, ivec2(3), 1);
  o = a;
}
