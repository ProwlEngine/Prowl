import { dotnet } from './_framework/dotnet.js';

let gl = null;
let program = null;
let vao = null;
let vbo = null;
let ebo = null;
let textures = new Map();
let whiteTexture = null;

let uViewportLoc, uTextureLoc, uScissorMatLoc, uScissorExtLoc;
let uBrushMatLoc, uBrushTypeLoc, uBrushColor1Loc, uBrushColor2Loc;
let uBrushParamsLoc, uBrushParams2Loc, uBrushTextureMatLoc;
let uSlugCurveTexLoc, uSlugBandTexLoc, uSlugCurveTexSizeLoc, uSlugBandTexSizeLoc;

const VERTEX_SIZE = 44; // 20 core + 24 slug
const DC_INFO_STRIDE = 8;
const _mat32 = new Float32Array(16);

const VS_SOURCE = `#version 300 es
precision highp float;
layout(location = 0) in vec2 aPos;
layout(location = 1) in vec2 aUV;
layout(location = 2) in vec4 aColor;
layout(location = 3) in vec4 aSlugBand;
layout(location = 4) in vec2 aSlugGlyphInfo;
uniform vec2 uViewport;
out vec2 vUV;
out vec4 vColor;
out vec2 vPos;
out vec4 vSlugBand;
flat out vec2 vSlugGlyph;
void main() {
    vec2 pos = (aPos / uViewport) * 2.0 - 1.0;
    pos.y = -pos.y;
    gl_Position = vec4(pos, 0.0, 1.0);
    vUV = aUV;
    vColor = aColor;
    vPos = aPos;
    vSlugBand = aSlugBand;
    vSlugGlyph = aSlugGlyphInfo;
}
`;

const FS_SOURCE = `#version 300 es
precision highp float;
in vec2 vUV;
in vec4 vColor;
in vec2 vPos;
in vec4 vSlugBand;
flat in vec2 vSlugGlyph;
uniform sampler2D uTexture;
uniform mat4 uScissorMat;
uniform vec2 uScissorExt;
uniform mat4 uBrushMat;
uniform int uBrushType;
uniform vec4 uBrushColor1;
uniform vec4 uBrushColor2;
uniform vec4 uBrushParams;
uniform vec2 uBrushParams2;
uniform mat4 uBrushTextureMat;

// Slug textures
uniform sampler2D uSlugCurveTex;
uniform sampler2D uSlugBandTex;
uniform vec2 uSlugCurveTexSize;
uniform vec2 uSlugBandTexSize;

out vec4 fragColor;

// ============== Slug functions ==============

vec2 SlugFetchBand(float absTexelIndex) {
    float tx = mod(absTexelIndex, uSlugBandTexSize.x);
    float ty = floor(absTexelIndex / uSlugBandTexSize.x);
    vec2 uv = (vec2(tx, ty) + 0.5) / uSlugBandTexSize;
    return texture(uSlugBandTex, uv).rg;
}

vec4 SlugFetchCurve(vec2 curveLoc) {
    vec2 uv = (curveLoc + 0.5) / uSlugCurveTexSize;
    return texture(uSlugCurveTex, uv);
}

vec2 SlugCalcRootEligibility(float y1, float y2, float y3) {
    float s0 = (y1 > 0.0) ? 1.0 : 0.0;
    float s1 = (y2 > 0.0) ? 1.0 : 0.0;
    float s2 = (y3 > 0.0) ? 1.0 : 0.0;
    float ns0 = 1.0 - s0, ns1 = 1.0 - s1, ns2 = 1.0 - s2;
    float root1 = clamp(s0*ns1*ns2 + ns0*s1*ns2 + s0*s1*ns2 + s0*ns1*s2, 0.0, 1.0);
    float root2 = clamp(ns0*s1*ns2 + ns0*ns1*s2 + s0*ns1*s2 + ns0*s1*s2, 0.0, 1.0);
    return vec2(root1, root2);
}

vec2 SlugSolveHorizPoly(vec4 p12, vec2 p3) {
    vec2 a = p12.xy - p12.zw * 2.0 + p3;
    vec2 b = p12.xy - p12.zw;
    float ra = 1.0 / a.y;
    float rb = 0.5 / b.y;
    float d = sqrt(max(b.y * b.y - a.y * p12.y, 0.0));
    float t1 = (b.y - d) * ra;
    float t2 = (b.y + d) * ra;
    if (abs(a.y) < 0.0001) { t1 = p12.y * rb; t2 = t1; }
    return vec2(
        (a.x * t1 - b.x * 2.0) * t1 + p12.x,
        (a.x * t2 - b.x * 2.0) * t2 + p12.x);
}

vec2 SlugSolveVertPoly(vec4 p12, vec2 p3) {
    vec2 a = p12.xy - p12.zw * 2.0 + p3;
    vec2 b = p12.xy - p12.zw;
    float ra = 1.0 / a.x;
    float rb = 0.5 / b.x;
    float d = sqrt(max(b.x * b.x - a.x * p12.x, 0.0));
    float t1 = (b.x - d) * ra;
    float t2 = (b.x + d) * ra;
    if (abs(a.x) < 0.0001) { t1 = p12.x * rb; t2 = t1; }
    return vec2(
        (a.y * t1 - b.y * 2.0) * t1 + p12.y,
        (a.y * t2 - b.y * 2.0) * t2 + p12.y);
}

float SlugRender(vec2 renderCoord, vec4 bandTransform, vec2 glyphTexInfo) {
    float glyTexPackedXY = glyphTexInfo.x;
    float glyphBandTexX  = mod(glyTexPackedXY, uSlugBandTexSize.x);
    float glyphBandTexY  = floor(glyTexPackedXY / uSlugBandTexSize.x);
    float bandCount      = glyphTexInfo.y;
    float bandMax        = bandCount - 1.0;

    vec2 emsPerPixel = fwidth(renderCoord);
    vec2 pixelsPerEm = 1.0 / max(emsPerPixel, vec2(0.0001, 0.0001));

    vec2 bandPos   = renderCoord * bandTransform.xy + bandTransform.zw;
    float bandIndexY = clamp(floor(bandPos.y), 0.0, bandMax);
    float bandIndexX = clamp(floor(bandPos.x), 0.0, bandMax);

    float glyphBaseTexel = glyphBandTexY * uSlugBandTexSize.x + glyphBandTexX;

    // Horizontal bands
    float xcov = 0.0, xwgt = 0.0;
    vec2 hBH = SlugFetchBand(glyphBaseTexel + bandIndexY);
    for (float ci = 0.0; ci < hBH.r; ci += 1.0) {
        vec2 cLoc = SlugFetchBand(hBH.g + ci);
        vec4 p12 = SlugFetchCurve(cLoc) - vec4(renderCoord, renderCoord);
        vec2 p3  = SlugFetchCurve(vec2(cLoc.x + 1.0, cLoc.y)).xy - renderCoord;
        if (max(max(p12.x, p12.z), p3.x) * pixelsPerEm.x < -0.5) break;
        vec2 elig = SlugCalcRootEligibility(p12.y, p12.w, p3.y);
        if (elig.x + elig.y > 0.0) {
            vec2 r = SlugSolveHorizPoly(p12, p3) * pixelsPerEm.x;
            if (elig.x > 0.5) { xcov += clamp(r.x+0.5, 0.0, 1.0); xwgt = max(xwgt, clamp(1.0-abs(r.x)*2.0, 0.0, 1.0)); }
            if (elig.y > 0.5) { xcov -= clamp(r.y+0.5, 0.0, 1.0); xwgt = max(xwgt, clamp(1.0-abs(r.y)*2.0, 0.0, 1.0)); }
        }
    }

    // Vertical bands
    float ycov = 0.0, ywgt = 0.0;
    vec2 vBH = SlugFetchBand(glyphBaseTexel + bandCount + bandIndexX);
    for (float vi = 0.0; vi < vBH.r; vi += 1.0) {
        vec2 cLoc = SlugFetchBand(vBH.g + vi);
        vec4 p12 = SlugFetchCurve(cLoc) - vec4(renderCoord, renderCoord);
        vec2 p3  = SlugFetchCurve(vec2(cLoc.x + 1.0, cLoc.y)).xy - renderCoord;
        if (max(max(p12.y, p12.w), p3.y) * pixelsPerEm.y < -0.5) break;
        vec2 elig = SlugCalcRootEligibility(p12.x, p12.z, p3.x);
        if (elig.x + elig.y > 0.0) {
            vec2 r = SlugSolveVertPoly(p12, p3) * pixelsPerEm.y;
            if (elig.x > 0.5) { ycov -= clamp(r.x+0.5, 0.0, 1.0); ywgt = max(ywgt, clamp(1.0-abs(r.x)*2.0, 0.0, 1.0)); }
            if (elig.y > 0.5) { ycov += clamp(r.y+0.5, 0.0, 1.0); ywgt = max(ywgt, clamp(1.0-abs(r.y)*2.0, 0.0, 1.0)); }
        }
    }

    float coverage = max(
        abs(xcov * xwgt + ycov * ywgt) / max(xwgt + ywgt, 0.0001),
        min(abs(xcov), abs(ycov)));
    return clamp(coverage, 0.0, 1.0);
}

// ============== Canvas functions ==============

float calculateBrushFactor() {
    if (uBrushType == 0) return 0.0;
    vec2 tp = (uBrushMat * vec4(vPos, 0.0, 1.0)).xy;
    if (uBrushType == 1) {
        vec2 s = uBrushParams.xy, e = uBrushParams.zw;
        vec2 line = e - s;
        float len = length(line);
        if (len < 0.001) return 0.0;
        return clamp(dot(tp - s, line) / (len * len), 0.0, 1.0);
    }
    if (uBrushType == 2) {
        vec2 c = uBrushParams.xy;
        float inner = uBrushParams.z, outer = uBrushParams.w;
        if (outer < 0.001) return 0.0;
        return clamp(smoothstep(inner, outer, length(tp - c)), 0.0, 1.0);
    }
    if (uBrushType == 3) {
        vec2 c = uBrushParams.xy, hs = uBrushParams.zw;
        float r = uBrushParams2.x, f = uBrushParams2.y;
        if (hs.x < 0.001 || hs.y < 0.001) return 0.0;
        vec2 q = abs(tp - c) - (hs - vec2(r));
        float d = min(max(q.x, q.y), 0.0) + length(max(q, 0.0)) - r;
        return clamp((d + f * 0.5) / f, 0.0, 1.0);
    }
    return 0.0;
}

float scissorMask(vec2 p) {
    if (uScissorExt.x < 0.0 || uScissorExt.y < 0.0) return 1.0;
    vec2 tp = (uScissorMat * vec4(p, 0.0, 1.0)).xy;
    vec2 d = abs(tp) - uScissorExt;
    vec2 se = vec2(0.5) - d;
    return clamp(se.x, 0.0, 1.0) * clamp(se.y, 0.0, 1.0);
}

void main() {
    float mask = scissorMask(vPos);
    vec4 color = vColor;
    if (uBrushType > 0) {
        color = mix(uBrushColor1, uBrushColor2, calculateBrushFactor());
    }

    // Slug mode: bandCount > 0 means this is a Slug glyph
    if (vSlugGlyph.y > 0.0) {
        float coverage = SlugRender(vUV, vSlugBand, vSlugGlyph);
        fragColor = vec4(color.rgb * coverage, coverage * color.a) * mask;
        return;
    }

    // Text mode: UV >= 2.0
    if (vUV.x >= 2.0) {
        fragColor = color * texture(uTexture, vUV - vec2(2.0)) * mask;
        return;
    }

    // Edge anti-aliasing
    vec2 ps = fwidth(vUV);
    vec2 ed = min(vUV, 1.0 - vUV);
    float ea = smoothstep(0.0, ps.x, ed.x) * smoothstep(0.0, ps.y, ed.y);
    ea = clamp(ea, 0.0, 1.0);

    fragColor = color * texture(uTexture, (uBrushTextureMat * vec4(vPos, 0.0, 1.0)).xy) * ea * mask;
}
`;

function createShader(type, source) {
    const s = gl.createShader(type);
    gl.shaderSource(s, source);
    gl.compileShader(s);
    if (!gl.getShaderParameter(s, gl.COMPILE_STATUS)) {
        console.error('Shader error:', gl.getShaderInfoLog(s));
        gl.deleteShader(s);
        return null;
    }
    return s;
}

function createProgram(vs, fs) {
    const p = gl.createProgram();
    gl.attachShader(p, vs);
    gl.attachShader(p, fs);
    gl.linkProgram(p);
    if (!gl.getProgramParameter(p, gl.LINK_STATUS)) {
        console.error('Link error:', gl.getProgramInfoLog(p));
        return null;
    }
    return p;
}

const webgl = {
    initWebGL(canvasId) {
        const canvas = document.getElementById(canvasId);
        gl = canvas.getContext('webgl2', { alpha: false, antialias: true, premultipliedAlpha: true });
        if (!gl) { console.error('WebGL2 not supported'); return; }

        const vs = createShader(gl.VERTEX_SHADER, VS_SOURCE);
        const fs = createShader(gl.FRAGMENT_SHADER, FS_SOURCE);
        program = createProgram(vs, fs);
        gl.deleteShader(vs);
        gl.deleteShader(fs);

        uViewportLoc = gl.getUniformLocation(program, 'uViewport');
        uTextureLoc = gl.getUniformLocation(program, 'uTexture');
        uScissorMatLoc = gl.getUniformLocation(program, 'uScissorMat');
        uScissorExtLoc = gl.getUniformLocation(program, 'uScissorExt');
        uBrushMatLoc = gl.getUniformLocation(program, 'uBrushMat');
        uBrushTypeLoc = gl.getUniformLocation(program, 'uBrushType');
        uBrushColor1Loc = gl.getUniformLocation(program, 'uBrushColor1');
        uBrushColor2Loc = gl.getUniformLocation(program, 'uBrushColor2');
        uBrushParamsLoc = gl.getUniformLocation(program, 'uBrushParams');
        uBrushParams2Loc = gl.getUniformLocation(program, 'uBrushParams2');
        uBrushTextureMatLoc = gl.getUniformLocation(program, 'uBrushTextureMat');
        uSlugCurveTexLoc = gl.getUniformLocation(program, 'uSlugCurveTex');
        uSlugBandTexLoc = gl.getUniformLocation(program, 'uSlugBandTex');
        uSlugCurveTexSizeLoc = gl.getUniformLocation(program, 'uSlugCurveTexSize');
        uSlugBandTexSizeLoc = gl.getUniformLocation(program, 'uSlugBandTexSize');

        vao = gl.createVertexArray();
        gl.bindVertexArray(vao);
        vbo = gl.createBuffer();
        gl.bindBuffer(gl.ARRAY_BUFFER, vbo);
        ebo = gl.createBuffer();
        gl.bindBuffer(gl.ELEMENT_ARRAY_BUFFER, ebo);

        // Position (location 0): vec2 at offset 0
        gl.enableVertexAttribArray(0);
        gl.vertexAttribPointer(0, 2, gl.FLOAT, false, VERTEX_SIZE, 0);
        // TexCoord / EmCoord (location 1): vec2 at offset 8
        gl.enableVertexAttribArray(1);
        gl.vertexAttribPointer(1, 2, gl.FLOAT, false, VERTEX_SIZE, 8);
        // Color (location 2): vec4 ubyte normalized at offset 16
        gl.enableVertexAttribArray(2);
        gl.vertexAttribPointer(2, 4, gl.UNSIGNED_BYTE, true, VERTEX_SIZE, 16);
        // Slug band transform (location 3): vec4 at offset 20
        gl.enableVertexAttribArray(3);
        gl.vertexAttribPointer(3, 4, gl.FLOAT, false, VERTEX_SIZE, 20);
        // Slug glyph info (location 4): vec2 at offset 36
        gl.enableVertexAttribArray(4);
        gl.vertexAttribPointer(4, 2, gl.FLOAT, false, VERTEX_SIZE, 36);

        gl.bindVertexArray(null);

        whiteTexture = gl.createTexture();
        gl.bindTexture(gl.TEXTURE_2D, whiteTexture);
        gl.texImage2D(gl.TEXTURE_2D, 0, gl.RGBA, 1, 1, 0, gl.RGBA, gl.UNSIGNED_BYTE,
            new Uint8Array([255, 255, 255, 255]));
    },

    getCanvasWidth() {
        return gl ? gl.canvas.clientWidth : 800;
    },

    getCanvasHeight() {
        return gl ? gl.canvas.clientHeight : 600;
    },

    createTexture(texId, width, height) {
        const tex = gl.createTexture();
        gl.bindTexture(gl.TEXTURE_2D, tex);
        gl.texImage2D(gl.TEXTURE_2D, 0, gl.RGBA, width, height, 0, gl.RGBA, gl.UNSIGNED_BYTE, null);
        gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MIN_FILTER, gl.LINEAR);
        gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MAG_FILTER, gl.LINEAR);
        gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_S, gl.REPEAT);
        gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_T, gl.REPEAT);
        textures.set(texId, { glTex: tex, width, height });
    },

    createFloatTexture(texId, width, height, components, doubleData) {
        const tex = gl.createTexture();
        gl.bindTexture(gl.TEXTURE_2D, tex);
        gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MIN_FILTER, gl.NEAREST);
        gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MAG_FILTER, gl.NEAREST);
        gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_S, gl.CLAMP_TO_EDGE);
        gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_T, gl.CLAMP_TO_EDGE);

        // Convert double[] to Float32Array
        let floatData;
        if (components === 4) {
            floatData = new Float32Array(doubleData.length);
            for (let i = 0; i < doubleData.length; i++) floatData[i] = doubleData[i];
        } else if (components === 2) {
            // Pack RG into RGBA (ZW = 0)
            floatData = new Float32Array(width * height * 4);
            for (let i = 0; i < width * height; i++) {
                floatData[i * 4 + 0] = doubleData[i * 2 + 0];
                floatData[i * 4 + 1] = doubleData[i * 2 + 1];
                floatData[i * 4 + 2] = 0;
                floatData[i * 4 + 3] = 0;
            }
        }

        gl.texImage2D(gl.TEXTURE_2D, 0, gl.RGBA32F, width, height, 0, gl.RGBA, gl.FLOAT, floatData);
        textures.set(texId, { glTex: tex, width, height });
    },

    setTextureData(texId, x, y, w, h, data) {
        const info = textures.get(texId);
        if (!info) return;
        gl.bindTexture(gl.TEXTURE_2D, info.glTex);
        gl.texSubImage2D(gl.TEXTURE_2D, 0, x, y, w, h, gl.RGBA, gl.UNSIGNED_BYTE, new Uint8Array(data));
    },

    render(vertexBytes, indexDataI32, drawCallInfoI32, scissorDataF64, brushDataF64) {
        const canvas = gl.canvas;
        const dpr = window.devicePixelRatio || 1;
        const displayW = Math.floor(canvas.clientWidth * dpr);
        const displayH = Math.floor(canvas.clientHeight * dpr);
        if (canvas.width !== displayW || canvas.height !== displayH) {
            canvas.width = displayW;
            canvas.height = displayH;
        }

        gl.viewport(0, 0, canvas.width, canvas.height);
        gl.clearColor(0.19, 0.19, 0.2, 1);
        gl.clear(gl.COLOR_BUFFER_BIT);

        if (vertexBytes.length === 0 || indexDataI32.length === 0) return;

        gl.useProgram(program);
        gl.uniform2f(uViewportLoc, canvas.clientWidth, canvas.clientHeight);
        gl.enable(gl.BLEND);
        gl.blendFunc(gl.ONE, gl.ONE_MINUS_SRC_ALPHA);
        gl.disable(gl.DEPTH_TEST);
        gl.disable(gl.CULL_FACE);

        gl.bindVertexArray(vao);
        gl.bindBuffer(gl.ARRAY_BUFFER, vbo);
        gl.bufferData(gl.ARRAY_BUFFER, vertexBytes, gl.DYNAMIC_DRAW);
        gl.bindBuffer(gl.ELEMENT_ARRAY_BUFFER, ebo);
        gl.bufferData(gl.ELEMENT_ARRAY_BUFFER, new Uint32Array(indexDataI32.buffer, indexDataI32.byteOffset, indexDataI32.length), gl.DYNAMIC_DRAW);

        gl.activeTexture(gl.TEXTURE0);
        gl.uniform1i(uTextureLoc, 0);

        let indexOffset = 0;
        const dcCount = drawCallInfoI32.length / DC_INFO_STRIDE;

        for (let i = 0; i < dcCount; i++) {
            const di = i * DC_INFO_STRIDE;
            const texId = drawCallInfoI32[di];
            const elemCount = drawCallInfoI32[di + 1];
            const slugCurveTexId = drawCallInfoI32[di + 2];
            const slugBandTexId = drawCallInfoI32[di + 3];
            const slugCurveW = drawCallInfoI32[di + 4];
            const slugCurveH = drawCallInfoI32[di + 5];
            const slugBandW = drawCallInfoI32[di + 6];
            const slugBandH = drawCallInfoI32[di + 7];

            // Bind main texture
            gl.activeTexture(gl.TEXTURE0);
            if (texId > 0 && textures.has(texId)) {
                gl.bindTexture(gl.TEXTURE_2D, textures.get(texId).glTex);
            } else {
                gl.bindTexture(gl.TEXTURE_2D, whiteTexture);
            }

            // Bind Slug textures if present
            if (slugCurveTexId > 0 && textures.has(slugCurveTexId)) {
                gl.activeTexture(gl.TEXTURE1);
                gl.bindTexture(gl.TEXTURE_2D, textures.get(slugCurveTexId).glTex);
                gl.uniform1i(uSlugCurveTexLoc, 1);
                gl.uniform2f(uSlugCurveTexSizeLoc, slugCurveW, slugCurveH);
            }
            if (slugBandTexId > 0 && textures.has(slugBandTexId)) {
                gl.activeTexture(gl.TEXTURE2);
                gl.bindTexture(gl.TEXTURE_2D, textures.get(slugBandTexId).glTex);
                gl.uniform1i(uSlugBandTexLoc, 2);
                gl.uniform2f(uSlugBandTexSizeLoc, slugBandW, slugBandH);
            }

            // Scissor
            const sb = i * 18;
            for (let j = 0; j < 16; j++) _mat32[j] = scissorDataF64[sb + j];
            gl.uniformMatrix4fv(uScissorMatLoc, false, _mat32);
            gl.uniform2f(uScissorExtLoc, scissorDataF64[sb + 16], scissorDataF64[sb + 17]);

            // Brush
            const bb = i * 47;
            const brushType = brushDataF64[bb] | 0;
            gl.uniform1i(uBrushTypeLoc, brushType);
            for (let j = 0; j < 16; j++) _mat32[j] = brushDataF64[bb + 1 + j];
            gl.uniformMatrix4fv(uBrushMatLoc, false, _mat32);
            gl.uniform4f(uBrushColor1Loc, brushDataF64[bb+17], brushDataF64[bb+18], brushDataF64[bb+19], brushDataF64[bb+20]);
            gl.uniform4f(uBrushColor2Loc, brushDataF64[bb+21], brushDataF64[bb+22], brushDataF64[bb+23], brushDataF64[bb+24]);
            gl.uniform4f(uBrushParamsLoc, brushDataF64[bb+25], brushDataF64[bb+26], brushDataF64[bb+27], brushDataF64[bb+28]);
            gl.uniform2f(uBrushParams2Loc, brushDataF64[bb+29], brushDataF64[bb+30]);
            for (let j = 0; j < 16; j++) _mat32[j] = brushDataF64[bb + 31 + j];
            gl.uniformMatrix4fv(uBrushTextureMatLoc, false, _mat32);

            gl.drawElements(gl.TRIANGLES, elemCount, gl.UNSIGNED_INT, indexOffset * 4);
            indexOffset += elemCount;
        }

        gl.bindVertexArray(null);
    }
};

// ─── Bootstrap .NET WASM ───

const { setModuleImports, getAssemblyExports, getConfig } = await dotnet
    .withConfig({ disableIntegrityCheck: true })
    .create();

setModuleImports('main.js', { webgl });

const config = getConfig();
const exports = await getAssemblyExports(config.mainAssemblyName);
exports.WasmExample.App.Init();

// ─── Input ───

const canvas = document.getElementById('canvas');

canvas.addEventListener('mousemove', (e) => {
    exports.WasmExample.App.OnMouseMove(e.clientX, e.clientY);
});
canvas.addEventListener('mousedown', (e) => {
    exports.WasmExample.App.OnMouseDown(e.clientX, e.clientY, e.button);
});
canvas.addEventListener('mouseup', (e) => {
    exports.WasmExample.App.OnMouseUp(e.clientX, e.clientY, e.button);
});
canvas.addEventListener('wheel', (e) => {
    e.preventDefault();
    exports.WasmExample.App.OnWheel(e.deltaY);
}, { passive: false });
canvas.addEventListener('contextmenu', (e) => e.preventDefault());

document.addEventListener('keydown', (e) => {
    exports.WasmExample.App.OnKeyDown(e.code);
    // Send printable characters as text input
    if (e.key.length === 1 && !e.ctrlKey && !e.metaKey) {
        exports.WasmExample.App.OnTextInput(e.key);
    }
});
document.addEventListener('keyup', (e) => {
    exports.WasmExample.App.OnKeyUp(e.code);
});

// ─── Resize handling ───

let prevW = canvas.clientWidth, prevH = canvas.clientHeight;

function checkResize() {
    const w = canvas.clientWidth, h = canvas.clientHeight;
    if (w !== prevW || h !== prevH) {
        prevW = w; prevH = h;
        exports.WasmExample.App.OnResize(w, h);
    }
}

// ─── Render loop ───

let lastTime = performance.now();

function frame(now) {
    const dt = (now - lastTime) / 1000.0;
    lastTime = now;
    checkResize();
    exports.WasmExample.App.OnFrame(dt);
    requestAnimationFrame(frame);
}

requestAnimationFrame(frame);
