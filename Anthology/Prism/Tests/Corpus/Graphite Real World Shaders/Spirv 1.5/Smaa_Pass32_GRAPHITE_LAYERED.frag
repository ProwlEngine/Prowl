#version 460
#extension GL_GOOGLE_include_directive : enable
#line 1 "Graphite/Smaa"

#extension GL_GOOGLE_cpp_style_line_directive : require
#line 7 "Graphite/Smaa"

#line 1 "Graphite/Fullscreen.glsl"


#pragma variantGRAPHITE_LAYERED
#pragma variantGRAPHITE_NO_SOURCE

#extension GL_EXT_multiview : require

































layout(location = 0) in vec2 vUV;

layout(location = 0) out vec4 outColor;


layout(set = 0, binding = 0) uniform sampler2DArray Graphite_Source;
vec4 Graphite_SampleSource(vec2 uv) { return texture(Graphite_Source, vec3(uv, float(gl_ViewIndex))); }


#line 9 "Graphite/Smaa"
#line 1 "Graphite/Internal/Smaa.glsl"
#pragma variantSMAABLENDEDGESWEIGHTS




layout(set = 0, binding = 1, std140) uniform _GraphiteGlobal { vec2 _Resolution; float _EdgeThreshold; };








































#line 1 "Graphite/Internal/SmaaReference.glsl"






























































































































































































































































































































     vec3 SMAAGatherNeighbours(vec2 texcoord,
                                 vec4 offset[3],
                                             sampler2DArray tex) {

    return textureGather(tex, vec3(texcoord + vec4(1.0 / _Resolution.x, 1.0 / _Resolution.y, _Resolution.x, _Resolution.y).xy * vec2(- 0.5, - 0.5), float(gl_ViewIndex))).grb;






}




     vec2 SMAACalculatePredicatedThreshold(vec2 texcoord,
                                             vec4 offset[3],
                                                                    sampler2DArray predicationTex) {
         vec3 neighbours = SMAAGatherNeighbours(texcoord, offset, predicationTex);
         vec2 delta = abs(neighbours.xx - neighbours.yz);
         vec2 edges = step(0.01, delta);
    return 2.0 * _EdgeThreshold * (1.0 - 0.4 * edges);
}




void SMAAMovc(bvec2 cond, inout vec2 variable, vec2 value) {
                 if (cond.x) variable.x = value.x;
                 if (cond.y) variable.y = value.y;
}

void SMAAMovc(bvec4 cond, inout vec4 variable, vec4 value) {
    SMAAMovc(cond.xy, variable.xy, value.xy);
    SMAAMovc(cond.zw, variable.zw, value.zw);
}









void SMAAEdgeDetectionVS(vec2 texcoord,
                         out vec4 offset[3]) {
    offset[0] = fma(vec4(1.0 / _Resolution.x, 1.0 / _Resolution.y, _Resolution.x, _Resolution.y).xyxy, vec4(- 1.0, 0.0, 0.0, - 1.0), texcoord.xyxy);
    offset[1] = fma(vec4(1.0 / _Resolution.x, 1.0 / _Resolution.y, _Resolution.x, _Resolution.y).xyxy, vec4(1.0, 0.0, 0.0, 1.0), texcoord.xyxy);
    offset[2] = fma(vec4(1.0 / _Resolution.x, 1.0 / _Resolution.y, _Resolution.x, _Resolution.y).xyxy, vec4(- 2.0, 0.0, 0.0, - 2.0), texcoord.xyxy);
}




void SMAABlendingWeightCalculationVS(vec2 texcoord,
                                     out vec2 pixcoord,
                                     out vec4 offset[3]) {
    pixcoord = texcoord * vec4(1.0 / _Resolution.x, 1.0 / _Resolution.y, _Resolution.x, _Resolution.y).zw;


    offset[0] = fma(vec4(1.0 / _Resolution.x, 1.0 / _Resolution.y, _Resolution.x, _Resolution.y).xyxy, vec4(- 0.25, - 0.125, 1.25, - 0.125), texcoord.xyxy);
    offset[1] = fma(vec4(1.0 / _Resolution.x, 1.0 / _Resolution.y, _Resolution.x, _Resolution.y).xyxy, vec4(- 0.125, - 0.25, - 0.125, 1.25), texcoord.xyxy);


    offset[2] =

                                                      fma(vec4(1.0 / _Resolution.x, 1.0 / _Resolution.y, _Resolution.x, _Resolution.y).xxyy, vec4(- 2.0, 2.0, - 2.0, 2.0) * float(16), vec4(offset[0].xz, offset[1].yw));
}




void SMAANeighborhoodBlendingVS(vec2 texcoord,
                                out vec4 offset) {
    offset = fma(vec4(1.0 / _Resolution.x, 1.0 / _Resolution.y, _Resolution.x, _Resolution.y).xyxy, vec4(1.0, 0.0, 0.0, 1.0), texcoord.xyxy);
}












     vec2 SMAALumaEdgeDetectionPS(vec2 texcoord,
                                    vec4 offset[3],
                                                     sampler2DArray colorTex



                               ) {




         vec2 threshold = vec2(_EdgeThreshold, _EdgeThreshold);



         vec3 weights = vec3(0.2126, 0.7152, 0.0722);
    float L = dot(texture(colorTex, vec3(texcoord, float(gl_ViewIndex))).rgb, weights);

    float Lleft = dot(texture(colorTex, vec3(offset[0].xy, float(gl_ViewIndex))).rgb, weights);
    float Ltop = dot(texture(colorTex, vec3(offset[0].zw, float(gl_ViewIndex))).rgb, weights);


         vec4 delta;
    delta.xy = abs(L - vec2(Lleft, Ltop));
         vec2 edges = step(threshold, delta.xy);


    if (dot(edges, vec2(1.0, 1.0)) == 0.0)
        discard;


    float Lright = dot(texture(colorTex, vec3(offset[1].xy, float(gl_ViewIndex))).rgb, weights);
    float Lbottom = dot(texture(colorTex, vec3(offset[1].zw, float(gl_ViewIndex))).rgb, weights);
    delta.zw = abs(L - vec2(Lright, Lbottom));


         vec2 maxDelta = max(delta.xy, delta.zw);


    float Lleftleft = dot(texture(colorTex, vec3(offset[2].xy, float(gl_ViewIndex))).rgb, weights);
    float Ltoptop = dot(texture(colorTex, vec3(offset[2].zw, float(gl_ViewIndex))).rgb, weights);
    delta.zw = abs(vec2(Lleft, Ltop) - vec2(Lleftleft, Ltoptop));


    maxDelta = max(maxDelta.xy, delta.zw);
    float finalDelta = max(maxDelta.x, maxDelta.y);


    edges.xy *= step(finalDelta, 2.0 * delta.xy);

    return edges;
}







     vec2 SMAAColorEdgeDetectionPS(vec2 texcoord,
                                     vec4 offset[3],
                                                      sampler2DArray colorTex



                                ) {




         vec2 threshold = vec2(_EdgeThreshold, _EdgeThreshold);



         vec4 delta;
         vec3 C = texture(colorTex, vec3(texcoord, float(gl_ViewIndex))).rgb;

         vec3 Cleft = texture(colorTex, vec3(offset[0].xy, float(gl_ViewIndex))).rgb;
         vec3 t = abs(C - Cleft);
    delta.x = max(max(t.r, t.g), t.b);

         vec3 Ctop = texture(colorTex, vec3(offset[0].zw, float(gl_ViewIndex))).rgb;
    t = abs(C - Ctop);
    delta.y = max(max(t.r, t.g), t.b);


         vec2 edges = step(threshold, delta.xy);


    if (dot(edges, vec2(1.0, 1.0)) == 0.0)
        discard;


         vec3 Cright = texture(colorTex, vec3(offset[1].xy, float(gl_ViewIndex))).rgb;
    t = abs(C - Cright);
    delta.z = max(max(t.r, t.g), t.b);

         vec3 Cbottom = texture(colorTex, vec3(offset[1].zw, float(gl_ViewIndex))).rgb;
    t = abs(C - Cbottom);
    delta.w = max(max(t.r, t.g), t.b);


         vec2 maxDelta = max(delta.xy, delta.zw);


         vec3 Cleftleft = texture(colorTex, vec3(offset[2].xy, float(gl_ViewIndex))).rgb;
    t = abs(C - Cleftleft);
    delta.z = max(max(t.r, t.g), t.b);

         vec3 Ctoptop = texture(colorTex, vec3(offset[2].zw, float(gl_ViewIndex))).rgb;
    t = abs(C - Ctoptop);
    delta.w = max(max(t.r, t.g), t.b);


    maxDelta = max(maxDelta.xy, delta.zw);
    float finalDelta = max(maxDelta.x, maxDelta.y);


    edges.xy *= step(finalDelta, 2.0 * delta.xy);

    return edges;
}




     vec2 SMAADepthEdgeDetectionPS(vec2 texcoord,
                                     vec4 offset[3],
                                                      sampler2DArray depthTex) {
         vec3 neighbours = SMAAGatherNeighbours(texcoord, offset, depthTex);
         vec2 delta = abs(neighbours.xx - vec2(neighbours.y, neighbours.z));
         vec2 edges = step( (0.1 * _EdgeThreshold), delta);

    if (dot(edges, vec2(1.0, 1.0)) == 0.0)
        discard;

    return edges;
}









     vec2 SMAADecodeDiagBilinearAccess(vec2 e) {













    e.r = e.r * abs(5.0 * e.r - 5.0 * 0.75);
    return round(e);
}

     vec4 SMAADecodeDiagBilinearAccess(vec4 e) {
    e.rb = e.rb * abs(5.0 * e.rb - 5.0 * 0.75);
    return round(e);
}




     vec2 SMAASearchDiag1(sampler2DArray edgesTex, vec2 texcoord, vec2 dir, out vec2 e) {
         vec4 coord = vec4(texcoord, - 1.0, 1.0);
         vec3 t = vec3(vec4(1.0 / _Resolution.x, 1.0 / _Resolution.y, _Resolution.x, _Resolution.y).xy, 1.0);
    while (coord.z < float(8 - 1) &&
           coord.w > 0.9) {
        coord.xyz = fma(t, vec3(dir, 1.0), coord.xyz);
        e = textureLod(edgesTex, vec3(coord.xy, float(gl_ViewIndex)), 0.0).rg;
        coord.w = dot(e, vec2(0.5, 0.5));
    }
    return coord.zw;
}

     vec2 SMAASearchDiag2(sampler2DArray edgesTex, vec2 texcoord, vec2 dir, out vec2 e) {
         vec4 coord = vec4(texcoord, - 1.0, 1.0);
    coord.x += 0.25 * vec4(1.0 / _Resolution.x, 1.0 / _Resolution.y, _Resolution.x, _Resolution.y).x;
         vec3 t = vec3(vec4(1.0 / _Resolution.x, 1.0 / _Resolution.y, _Resolution.x, _Resolution.y).xy, 1.0);
    while (coord.z < float(8 - 1) &&
           coord.w > 0.9) {
        coord.xyz = fma(t, vec3(dir, 1.0), coord.xyz);



        e = textureLod(edgesTex, vec3(coord.xy, float(gl_ViewIndex)), 0.0).rg;
        e = SMAADecodeDiagBilinearAccess(e);





        coord.w = dot(e, vec2(0.5, 0.5));
    }
    return coord.zw;
}





     vec2 SMAAAreaDiag(sampler2DArray areaTex, vec2 dist, vec2 e, float offset) {
         vec2 texcoord = fma(vec2(20, 20), e, dist);


    texcoord = fma( (1.0 / vec2(160.0, 560.0)), texcoord, 0.5 * (1.0 / vec2(160.0, 560.0)));


    texcoord.x += 0.5;


    texcoord.y += (1.0 / 7.0) * offset;


    return textureLod(areaTex, vec3(texcoord, float(gl_ViewIndex)), 0.0).rg;
}




     vec2 SMAACalculateDiagWeights(sampler2DArray edgesTex, sampler2DArray areaTex, vec2 texcoord, vec2 e, vec4 subsampleIndices) {
         vec2 weights = vec2(0.0, 0.0);


         vec4 d;
         vec2 end;
    if (e.r > 0.0) {
        d.xz = SMAASearchDiag1(edgesTex, texcoord, vec2(- 1.0, 1.0), end);
        d.x += float(end.y > 0.9);
    } else
        d.xz = vec2(0.0, 0.0);
    d.yw = SMAASearchDiag1(edgesTex, texcoord, vec2(1.0, - 1.0), end);


    if (d.x + d.y > 2.0) {

             vec4 coords = fma(vec4(- d.x + 0.25, d.x, d.y, - d.y - 0.25), vec4(1.0 / _Resolution.x, 1.0 / _Resolution.y, _Resolution.x, _Resolution.y).xyxy, texcoord.xyxy);
             vec4 c;
        c.xy = textureLodOffset(edgesTex, vec3(coords.xy, float(gl_ViewIndex)), 0.0, ivec2(- 1, 0)).rg;
        c.zw = textureLodOffset(edgesTex, vec3(coords.zw, float(gl_ViewIndex)), 0.0, ivec2(1, 0)).rg;
        c.yxwz = SMAADecodeDiagBilinearAccess(c.xyzw);










             vec2 cc = fma(vec2(2.0, 2.0), c.xz, c.yw);


        SMAAMovc(bvec2(step(0.9, d.zw)), cc, vec2(0.0, 0.0));


        weights += SMAAAreaDiag(areaTex, d.xy, cc, subsampleIndices.z);
    }


    d.xz = SMAASearchDiag2(edgesTex, texcoord, vec2(- 1.0, - 1.0), end);
    if (textureLodOffset(edgesTex, vec3(texcoord, float(gl_ViewIndex)), 0.0, ivec2(1, 0)).r > 0.0) {
        d.yw = SMAASearchDiag2(edgesTex, texcoord, vec2(1.0, 1.0), end);
        d.y += float(end.y > 0.9);
    } else
        d.yw = vec2(0.0, 0.0);


    if (d.x + d.y > 2.0) {

             vec4 coords = fma(vec4(- d.x, - d.x, d.y, d.y), vec4(1.0 / _Resolution.x, 1.0 / _Resolution.y, _Resolution.x, _Resolution.y).xyxy, texcoord.xyxy);
             vec4 c;
        c.x = textureLodOffset(edgesTex, vec3(coords.xy, float(gl_ViewIndex)), 0.0, ivec2(- 1, 0)).g;
        c.y = textureLodOffset(edgesTex, vec3(coords.xy, float(gl_ViewIndex)), 0.0, ivec2(0, - 1)).r;
        c.zw = textureLodOffset(edgesTex, vec3(coords.zw, float(gl_ViewIndex)), 0.0, ivec2(1, 0)).gr;
             vec2 cc = fma(vec2(2.0, 2.0), c.xz, c.yw);


        SMAAMovc(bvec2(step(0.9, d.zw)), cc, vec2(0.0, 0.0));


        weights += SMAAAreaDiag(areaTex, d.xy, cc, subsampleIndices.w).gr;
    }

    return weights;
}











float SMAASearchLength(sampler2DArray searchTex, vec2 e, float offset) {


         vec2 scale = vec2(66.0, 33.0) * vec2(0.5, - 1.0);
         vec2 bias = vec2(66.0, 33.0) * vec2(offset, 1.0);


    scale += vec2(- 1.0, 1.0);
    bias += vec2(0.5, - 0.5);



    scale *= 1.0 / vec2(64.0, 16.0);
    bias *= 1.0 / vec2(64.0, 16.0);


    return textureLod(searchTex, vec3(fma(scale, e, bias), float(gl_ViewIndex)), 0.0).r;
}




float SMAASearchXLeft(sampler2DArray edgesTex, sampler2DArray searchTex, vec2 texcoord, float end) {







         vec2 e = vec2(0.0, 1.0);
    while (texcoord.x > end &&
           e.g > 0.8281 &&
           e.r == 0.0) {
        e = textureLod(edgesTex, vec3(texcoord, float(gl_ViewIndex)), 0.0).rg;
        texcoord = fma(- vec2(2.0, 0.0), vec4(1.0 / _Resolution.x, 1.0 / _Resolution.y, _Resolution.x, _Resolution.y).xy, texcoord);
    }

    float offset = fma(- (255.0 / 127.0), SMAASearchLength(searchTex, e, 0.0), 3.25);
    return fma(vec4(1.0 / _Resolution.x, 1.0 / _Resolution.y, _Resolution.x, _Resolution.y).x, offset, texcoord.x);












}

float SMAASearchXRight(sampler2DArray edgesTex, sampler2DArray searchTex, vec2 texcoord, float end) {
         vec2 e = vec2(0.0, 1.0);
    while (texcoord.x < end &&
           e.g > 0.8281 &&
           e.r == 0.0) {
        e = textureLod(edgesTex, vec3(texcoord, float(gl_ViewIndex)), 0.0).rg;
        texcoord = fma(vec2(2.0, 0.0), vec4(1.0 / _Resolution.x, 1.0 / _Resolution.y, _Resolution.x, _Resolution.y).xy, texcoord);
    }
    float offset = fma(- (255.0 / 127.0), SMAASearchLength(searchTex, e, 0.5), 3.25);
    return fma(- vec4(1.0 / _Resolution.x, 1.0 / _Resolution.y, _Resolution.x, _Resolution.y).x, offset, texcoord.x);
}

float SMAASearchYUp(sampler2DArray edgesTex, sampler2DArray searchTex, vec2 texcoord, float end) {
         vec2 e = vec2(1.0, 0.0);
    while (texcoord.y > end &&
           e.r > 0.8281 &&
           e.g == 0.0) {
        e = textureLod(edgesTex, vec3(texcoord, float(gl_ViewIndex)), 0.0).rg;
        texcoord = fma(- vec2(0.0, 2.0), vec4(1.0 / _Resolution.x, 1.0 / _Resolution.y, _Resolution.x, _Resolution.y).xy, texcoord);
    }
    float offset = fma(- (255.0 / 127.0), SMAASearchLength(searchTex, e.gr, 0.0), 3.25);
    return fma(vec4(1.0 / _Resolution.x, 1.0 / _Resolution.y, _Resolution.x, _Resolution.y).y, offset, texcoord.y);
}

float SMAASearchYDown(sampler2DArray edgesTex, sampler2DArray searchTex, vec2 texcoord, float end) {
         vec2 e = vec2(1.0, 0.0);
    while (texcoord.y < end &&
           e.r > 0.8281 &&
           e.g == 0.0) {
        e = textureLod(edgesTex, vec3(texcoord, float(gl_ViewIndex)), 0.0).rg;
        texcoord = fma(vec2(0.0, 2.0), vec4(1.0 / _Resolution.x, 1.0 / _Resolution.y, _Resolution.x, _Resolution.y).xy, texcoord);
    }
    float offset = fma(- (255.0 / 127.0), SMAASearchLength(searchTex, e.gr, 0.5), 3.25);
    return fma(- vec4(1.0 / _Resolution.x, 1.0 / _Resolution.y, _Resolution.x, _Resolution.y).y, offset, texcoord.y);
}





     vec2 SMAAArea(sampler2DArray areaTex, vec2 dist, float e1, float e2, float offset) {

         vec2 texcoord = fma(vec2(16, 16), round(4.0 * vec2(e1, e2)), dist);


    texcoord = fma( (1.0 / vec2(160.0, 560.0)), texcoord, 0.5 * (1.0 / vec2(160.0, 560.0)));


    texcoord.y = fma( (1.0 / 7.0), offset, texcoord.y);


    return textureLod(areaTex, vec3(texcoord, float(gl_ViewIndex)), 0.0).rg;
}




void SMAADetectHorizontalCornerPattern(sampler2DArray edgesTex, inout vec2 weights, vec4 texcoord, vec2 d) {

         vec2 leftRight = step(d.xy, d.yx);
         vec2 rounding = (1.0 - (float(25) / 100.0)) * leftRight;

    rounding /= leftRight.x + leftRight.y;

         vec2 factor = vec2(1.0, 1.0);
    factor.x -= rounding.x * textureLodOffset(edgesTex, vec3(texcoord.xy, float(gl_ViewIndex)), 0.0, ivec2(0, 1)).r;
    factor.x -= rounding.y * textureLodOffset(edgesTex, vec3(texcoord.zw, float(gl_ViewIndex)), 0.0, ivec2(1, 1)).r;
    factor.y -= rounding.x * textureLodOffset(edgesTex, vec3(texcoord.xy, float(gl_ViewIndex)), 0.0, ivec2(0, - 2)).r;
    factor.y -= rounding.y * textureLodOffset(edgesTex, vec3(texcoord.zw, float(gl_ViewIndex)), 0.0, ivec2(1, - 2)).r;

    weights *= clamp(factor, 0.0, 1.0);

}

void SMAADetectVerticalCornerPattern(sampler2DArray edgesTex, inout vec2 weights, vec4 texcoord, vec2 d) {

         vec2 leftRight = step(d.xy, d.yx);
         vec2 rounding = (1.0 - (float(25) / 100.0)) * leftRight;

    rounding /= leftRight.x + leftRight.y;

         vec2 factor = vec2(1.0, 1.0);
    factor.x -= rounding.x * textureLodOffset(edgesTex, vec3(texcoord.xy, float(gl_ViewIndex)), 0.0, ivec2(1, 0)).g;
    factor.x -= rounding.y * textureLodOffset(edgesTex, vec3(texcoord.zw, float(gl_ViewIndex)), 0.0, ivec2(1, 1)).g;
    factor.y -= rounding.x * textureLodOffset(edgesTex, vec3(texcoord.xy, float(gl_ViewIndex)), 0.0, ivec2(- 2, 0)).g;
    factor.y -= rounding.y * textureLodOffset(edgesTex, vec3(texcoord.zw, float(gl_ViewIndex)), 0.0, ivec2(- 2, 1)).g;

    weights *= clamp(factor, 0.0, 1.0);

}




     vec4 SMAABlendingWeightCalculationPS(vec2 texcoord,
                                            vec2 pixcoord,
                                            vec4 offset[3],
                                                             sampler2DArray edgesTex,
                                                            sampler2DArray areaTex,
                                                              sampler2DArray searchTex,
                                            vec4 subsampleIndices) {
         vec4 weights = vec4(0.0, 0.0, 0.0, 0.0);

         vec2 e = texture(edgesTex, vec3(texcoord, float(gl_ViewIndex))).rg;


    if (e.g > 0.0) {



        weights.rg = SMAACalculateDiagWeights(edgesTex, areaTex, texcoord, e, subsampleIndices);




        if (weights.r == - weights.g) {


             vec2 d;


             vec3 coords;
        coords.x = SMAASearchXLeft(edgesTex, searchTex, offset[0].xy, offset[2].x);
        coords.y = offset[1].y;
        d.x = coords.x;




        float e1 = textureLod(edgesTex, vec3(coords.xy, float(gl_ViewIndex)), 0.0).r;


        coords.z = SMAASearchXRight(edgesTex, searchTex, offset[0].zw, offset[2].y);
        d.y = coords.z;



        d = abs(round(fma(vec4(1.0 / _Resolution.x, 1.0 / _Resolution.y, _Resolution.x, _Resolution.y).zz, d, - pixcoord.xx)));



             vec2 sqrt_d = sqrt(d);


        float e2 = textureLodOffset(edgesTex, vec3(coords.zy, float(gl_ViewIndex)), 0.0, ivec2(1, 0)).r;



        weights.rg = SMAAArea(areaTex, sqrt_d, e1, e2, subsampleIndices.y);


        coords.y = texcoord.y;
        SMAADetectHorizontalCornerPattern(edgesTex, weights.rg, coords.xyzy, d);


        } else
            e.r = 0.0;

    }


    if (e.r > 0.0) {
             vec2 d;


             vec3 coords;
        coords.y = SMAASearchYUp(edgesTex, searchTex, offset[1].xy, offset[2].z);
        coords.x = offset[0].x;
        d.x = coords.y;


        float e1 = textureLod(edgesTex, vec3(coords.xy, float(gl_ViewIndex)), 0.0).g;


        coords.z = SMAASearchYDown(edgesTex, searchTex, offset[1].zw, offset[2].w);
        d.y = coords.z;


        d = abs(round(fma(vec4(1.0 / _Resolution.x, 1.0 / _Resolution.y, _Resolution.x, _Resolution.y).ww, d, - pixcoord.yy)));



             vec2 sqrt_d = sqrt(d);


        float e2 = textureLodOffset(edgesTex, vec3(coords.xz, float(gl_ViewIndex)), 0.0, ivec2(0, 1)).g;


        weights.ba = SMAAArea(areaTex, sqrt_d, e1, e2, subsampleIndices.x);


        coords.x = texcoord.x;
        SMAADetectVerticalCornerPattern(edgesTex, weights.ba, coords.xyxz, d);
    }

    return weights;
}




     vec4 SMAANeighborhoodBlendingPS(vec2 texcoord,
                                       vec4 offset,
                                                        sampler2DArray colorTex,
                                                        sampler2DArray blendTex



                                  ) {

         vec4 a;
    a.x = texture(blendTex, vec3(offset.xy, float(gl_ViewIndex))).a;
    a.y = texture(blendTex, vec3(offset.zw, float(gl_ViewIndex))).g;
    a.wz = texture(blendTex, vec3(texcoord, float(gl_ViewIndex))).xz;



    if (dot(a, vec4(1.0, 1.0, 1.0, 1.0)) < 1e-5) {
             vec4 color = textureLod(colorTex, vec3(texcoord, float(gl_ViewIndex)), 0.0);








        return color;
    } else {
        bool h = max(a.x, a.z) > max(a.y, a.w);


             vec4 blendingOffset = vec4(0.0, a.y, 0.0, a.w);
             vec2 blendingWeight = a.yw;
        SMAAMovc(bvec4(h, h, h, h), blendingOffset, vec4(a.x, 0.0, a.z, 0.0));
        SMAAMovc(bvec2(h, h), blendingWeight, a.xz);
        blendingWeight /= dot(blendingWeight, vec2(1.0, 1.0));


             vec4 blendingCoord = fma(blendingOffset, vec4(vec4(1.0 / _Resolution.x, 1.0 / _Resolution.y, _Resolution.x, _Resolution.y).xy, - vec4(1.0 / _Resolution.x, 1.0 / _Resolution.y, _Resolution.x, _Resolution.y).xy), texcoord.xyxy);



             vec4 color = blendingWeight.x * textureLod(colorTex, vec3(blendingCoord.xy, float(gl_ViewIndex)), 0.0);
        color += blendingWeight.y * textureLod(colorTex, vec3(blendingCoord.zw, float(gl_ViewIndex)), 0.0);










        return color;
    }
}




     vec4 SMAAResolvePS(vec2 texcoord,
                                                  sampler2DArray currentColorTex,
                                                   sampler2DArray previousColorTex



                     ) {



















         vec4 current = texture(currentColorTex, vec3(texcoord, float(gl_ViewIndex)));
         vec4 previous = texture(previousColorTex, vec3(texcoord, float(gl_ViewIndex)));
    return mix(current, previous, 0.5);

}


















#line 48 "Graphite/Internal/Smaa.glsl"























layout(set = 0, binding = 2) uniform sampler2DArray _BlendTex;

void main()
{
    vec4 offset;
    SMAANeighborhoodBlendingVS(vUV, offset);
    outColor = SMAANeighborhoodBlendingPS(vUV, offset, Graphite_Source, _BlendTex);
}

#line 10 "Graphite/Smaa"

