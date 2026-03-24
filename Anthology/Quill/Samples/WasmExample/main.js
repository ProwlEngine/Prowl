// Prowl.Quill WasmExample — No Blazor, pure .NET WASM + WebGL2

import { dotnet } from './_framework/dotnet.js';

let gl = null;
let program = null;
let vao = null;
let vbo = null;
let ebo = null;
let textures = new Map();
let whiteTexture = null;

let uViewportLoc, uTextureLoc, uScissorMatLoc, uScissorExtLoc, uDpiScaleLoc;
let uBrushMatLoc, uBrushTypeLoc, uBrushColor1Loc, uBrushColor2Loc;
let uBrushParamsLoc, uBrushParams2Loc, uBrushTextureMatLoc;
let uSlugCurveTexLoc, uSlugBandTexLoc, uSlugCurveTexSizeLoc, uSlugBandTexSizeLoc;

const VERTEX_SIZE = 44; // 20 core + 24 slug

// Pre-allocated buffer for matrix uniforms (avoids per-draw-call Float32Array allocations)
const _mat32 = new Float32Array(16);


const VS_SOURCE = `#version 300 es
precision highp float;
layout(location = 0) in vec2 aPos;
layout(location = 1) in vec2 aUV;
layout(location = 2) in vec4 aColor;
layout(location = 3) in vec4 aSlugBand;
layout(location = 4) in vec2 aSlugGlyph;
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
    vSlugGlyph = aSlugGlyph;
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
uniform float uDpiScale;
uniform mat4 uBrushMat;
uniform int uBrushType;
uniform vec4 uBrushColor1;
uniform vec4 uBrushColor2;
uniform vec4 uBrushParams;
uniform vec2 uBrushParams2;
uniform mat4 uBrushTextureMat;
uniform sampler2D uSlugCurveTex;
uniform sampler2D uSlugBandTex;
uniform vec2 uSlugCurveTexSize;
uniform vec2 uSlugBandTexSize;
out vec4 fragColor;

// ============== Slug functions ==============
vec2 SlugFetchBand(float idx) {
    float tx = mod(idx, uSlugBandTexSize.x);
    float ty = floor(idx / uSlugBandTexSize.x);
    return texture(uSlugBandTex, (vec2(tx, ty) + 0.5) / uSlugBandTexSize).rg;
}
vec4 SlugFetchCurve(vec2 loc) {
    return texture(uSlugCurveTex, (loc + 0.5) / uSlugCurveTexSize);
}
vec2 SlugRootElig(float y1, float y2, float y3) {
    float s0 = y1 > 0.0 ? 1.0 : 0.0, s1 = y2 > 0.0 ? 1.0 : 0.0, s2 = y3 > 0.0 ? 1.0 : 0.0;
    float n0 = 1.0-s0, n1 = 1.0-s1, n2 = 1.0-s2;
    return vec2(clamp(s0*n1*n2+n0*s1*n2+s0*s1*n2+s0*n1*s2, 0.0, 1.0),
                clamp(n0*s1*n2+n0*n1*s2+s0*n1*s2+n0*s1*s2, 0.0, 1.0));
}
vec2 SlugSolveH(vec4 p12, vec2 p3) {
    vec2 a = p12.xy - p12.zw*2.0 + p3, b = p12.xy - p12.zw;
    float ra = 1.0/a.y, rb = 0.5/b.y;
    float d = sqrt(max(b.y*b.y - a.y*p12.y, 0.0));
    float t1 = (b.y-d)*ra, t2 = (b.y+d)*ra;
    if (abs(a.y) < 0.0001) { t1 = p12.y*rb; t2 = t1; }
    return vec2((a.x*t1-b.x*2.0)*t1+p12.x, (a.x*t2-b.x*2.0)*t2+p12.x);
}
vec2 SlugSolveV(vec4 p12, vec2 p3) {
    vec2 a = p12.xy - p12.zw*2.0 + p3, b = p12.xy - p12.zw;
    float ra = 1.0/a.x, rb = 0.5/b.x;
    float d = sqrt(max(b.x*b.x - a.x*p12.x, 0.0));
    float t1 = (b.x-d)*ra, t2 = (b.x+d)*ra;
    if (abs(a.x) < 0.0001) { t1 = p12.x*rb; t2 = t1; }
    return vec2((a.y*t1-b.y*2.0)*t1+p12.y, (a.y*t2-b.y*2.0)*t2+p12.y);
}
float SlugRender(vec2 rc, vec4 bt, vec2 gi) {
    float bx = mod(gi.x, uSlugBandTexSize.x), by = floor(gi.x / uSlugBandTexSize.x);
    float bc = gi.y, bm = bc - 1.0;
    vec2 epp = fwidth(rc), ppe = 1.0/max(epp, vec2(0.0001));
    vec2 bp = rc*bt.xy + bt.zw;
    float biY = clamp(floor(bp.y), 0.0, bm), biX = clamp(floor(bp.x), 0.0, bm);
    float base = by*uSlugBandTexSize.x + bx;
    float xc=0.0, xw=0.0;
    vec2 hh = SlugFetchBand(base+biY);
    for (float ci=0.0; ci<hh.r; ci+=1.0) {
        vec2 cl = SlugFetchBand(hh.g+ci);
        vec4 p12 = SlugFetchCurve(cl)-vec4(rc,rc);
        vec2 p3 = SlugFetchCurve(vec2(cl.x+1.0,cl.y)).xy-rc;
        if (max(max(p12.x,p12.z),p3.x)*ppe.x < -0.5) break;
        vec2 e = SlugRootElig(p12.y,p12.w,p3.y);
        if (e.x+e.y>0.0) { vec2 r=SlugSolveH(p12,p3)*ppe.x;
            if(e.x>0.5){xc+=clamp(r.x+0.5,0.0,1.0);xw=max(xw,clamp(1.0-abs(r.x)*2.0,0.0,1.0));}
            if(e.y>0.5){xc-=clamp(r.y+0.5,0.0,1.0);xw=max(xw,clamp(1.0-abs(r.y)*2.0,0.0,1.0));}
        }
    }
    float yc=0.0, yw=0.0;
    vec2 vh = SlugFetchBand(base+bc+biX);
    for (float vi=0.0; vi<vh.r; vi+=1.0) {
        vec2 cl = SlugFetchBand(vh.g+vi);
        vec4 p12 = SlugFetchCurve(cl)-vec4(rc,rc);
        vec2 p3 = SlugFetchCurve(vec2(cl.x+1.0,cl.y)).xy-rc;
        if (max(max(p12.y,p12.w),p3.y)*ppe.y < -0.5) break;
        vec2 e = SlugRootElig(p12.x,p12.z,p3.x);
        if (e.x+e.y>0.0) { vec2 r=SlugSolveV(p12,p3)*ppe.y;
            if(e.x>0.5){yc-=clamp(r.x+0.5,0.0,1.0);yw=max(yw,clamp(1.0-abs(r.x)*2.0,0.0,1.0));}
            if(e.y>0.5){yc+=clamp(r.y+0.5,0.0,1.0);yw=max(yw,clamp(1.0-abs(r.y)*2.0,0.0,1.0));}
        }
    }
    return clamp(max(abs(xc*xw+yc*yw)/max(xw+yw,0.0001), min(abs(xc),abs(yc))), 0.0, 1.0);
}

// ============== Canvas functions ==============
float calculateBrushFactor() {
    if (uBrushType == 0) return 0.0;
    vec2 lp = vPos / uDpiScale, tp = (uBrushMat * vec4(lp, 0.0, 1.0)).xy;
    if (uBrushType == 1) { vec2 s=uBrushParams.xy,e=uBrushParams.zw,l=e-s; float len=length(l); if(len<0.001)return 0.0; return clamp(dot(tp-s,l)/(len*len),0.0,1.0); }
    if (uBrushType == 2) { vec2 c=uBrushParams.xy; if(uBrushParams.w<0.001)return 0.0; return clamp(smoothstep(uBrushParams.z,uBrushParams.w,length(tp-c)),0.0,1.0); }
    if (uBrushType == 3) { vec2 c=uBrushParams.xy,hs=uBrushParams.zw; float r=uBrushParams2.x,f=uBrushParams2.y; if(hs.x<0.001||hs.y<0.001)return 0.0; vec2 q=abs(tp-c)-(hs-vec2(r)); float d=min(max(q.x,q.y),0.0)+length(max(q,0.0))-r; return clamp((d+f*0.5)/f,0.0,1.0); }
    return 0.0;
}
float scissorMask(vec2 p) {
    if (uScissorExt.x<0.0||uScissorExt.y<0.0) return 1.0;
    vec2 lp=p/uDpiScale, tp=(uScissorMat*vec4(lp,0.0,1.0)).xy, le=uScissorExt/uDpiScale;
    vec2 d=abs(tp)-le; float hp=0.5/uDpiScale; vec2 se=vec2(hp)-d;
    return clamp(se.x,0.0,1.0)*clamp(se.y,0.0,1.0);
}

void main() {
    // Slug mode
    if (vSlugGlyph.y > 0.0) {
        float cov = SlugRender(vUV, vSlugBand, vSlugGlyph);
        fragColor = vec4(vColor.rgb * cov, cov * vColor.a);
        return;
    }
    float mask = scissorMask(vPos);
    vec4 color = vColor;
    if (uBrushType > 0) color = mix(uBrushColor1, uBrushColor2, calculateBrushFactor());
    if (vUV.x >= 2.0) { fragColor = color * texture(uTexture, vUV - vec2(2.0, 0.0)) * mask; return; }
    vec2 ps = fwidth(vUV), ed = min(vUV, 1.0-vUV);
    float ea = clamp((ps.x>0.0?smoothstep(0.0,ps.x,ed.x):1.0)*(ps.y>0.0?smoothstep(0.0,ps.y,ed.y):1.0), 0.0, 1.0);
    vec2 lp = vPos / uDpiScale;
    fragColor = color * texture(uTexture, (uBrushTextureMat * vec4(lp, 0.0, 1.0)).xy) * ea * mask;
}
`;

// ─── WebGL helpers ───

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

// ─── WebGL API exposed to C# via [JSImport] ───

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
        uDpiScaleLoc = gl.getUniformLocation(program, 'uDpiScale');
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

        // 44-byte vertex: pos(8) + uv(8) + color(4) + slugBand(16) + slugGlyph(8)
        gl.enableVertexAttribArray(0);
        gl.vertexAttribPointer(0, 2, gl.FLOAT, false, VERTEX_SIZE, 0);
        gl.enableVertexAttribArray(1);
        gl.vertexAttribPointer(1, 2, gl.FLOAT, false, VERTEX_SIZE, 8);
        gl.enableVertexAttribArray(2);
        gl.vertexAttribPointer(2, 4, gl.UNSIGNED_BYTE, true, VERTEX_SIZE, 16);
        gl.enableVertexAttribArray(3);
        gl.vertexAttribPointer(3, 4, gl.FLOAT, false, VERTEX_SIZE, 20);
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

    setTextureData(texId, x, y, w, h, data) {
        const info = textures.get(texId);
        if (!info) return;
        gl.bindTexture(gl.TEXTURE_2D, info.glTex);
        gl.texSubImage2D(gl.TEXTURE_2D, 0, x, y, w, h, gl.RGBA, gl.UNSIGNED_BYTE, new Uint8Array(data));
    },

    createFloatTexture(texId, width, height, components, data) {
        const tex = gl.createTexture();
        gl.bindTexture(gl.TEXTURE_2D, tex);
        gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MIN_FILTER, gl.NEAREST);
        gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MAG_FILTER, gl.NEAREST);
        gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_S, gl.CLAMP_TO_EDGE);
        gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_T, gl.CLAMP_TO_EDGE);

        const floatData = new Float32Array(data);
        let uploadData;
        if (components === 2) {
            uploadData = new Float32Array(width * height * 4);
            for (let i = 0; i < width * height; i++) {
                uploadData[i*4] = floatData[i*2];
                uploadData[i*4+1] = floatData[i*2+1];
            }
        } else {
            uploadData = floatData;
        }
        gl.texImage2D(gl.TEXTURE_2D, 0, gl.RGBA32F, width, height, 0, gl.RGBA, gl.FLOAT, uploadData);
        textures.set(texId, { glTex: tex, width, height });
    },

    render(vertexBytes, indexDataI32, drawCallInfoI32, scissorDataF64, brushDataF64, canvasScale) {
        const canvas = gl.canvas;
        const dpr = window.devicePixelRatio || 1;
        const displayW = Math.floor(canvas.clientWidth * dpr);
        const displayH = Math.floor(canvas.clientHeight * dpr);
        if (canvas.width !== displayW || canvas.height !== displayH) {
            canvas.width = displayW;
            canvas.height = displayH;
        }

        gl.viewport(0, 0, canvas.width, canvas.height);
        gl.clearColor(0, 0, 0, 1);
        gl.clear(gl.COLOR_BUFFER_BIT);

        if (vertexBytes.length === 0 || indexDataI32.length === 0) return;

        gl.useProgram(program);
        gl.uniform2f(uViewportLoc, canvas.clientWidth, canvas.clientHeight);
        gl.uniform1f(uDpiScaleLoc, canvasScale || 1.0);
        gl.enable(gl.BLEND);
        gl.blendFunc(gl.ONE, gl.ONE_MINUS_SRC_ALPHA);
        gl.disable(gl.DEPTH_TEST);
        gl.disable(gl.CULL_FACE);

        gl.bindVertexArray(vao);
        gl.bindBuffer(gl.ARRAY_BUFFER, vbo);
        gl.bufferData(gl.ARRAY_BUFFER, vertexBytes, gl.DYNAMIC_DRAW);
        gl.bindBuffer(gl.ELEMENT_ARRAY_BUFFER, ebo);
        // Reinterpret Int32Array as Uint32Array (same memory layout for values < 2^31)
        gl.bufferData(gl.ELEMENT_ARRAY_BUFFER, new Uint32Array(indexDataI32.buffer, indexDataI32.byteOffset, indexDataI32.length), gl.DYNAMIC_DRAW);

        gl.activeTexture(gl.TEXTURE0);
        gl.uniform1i(uTextureLoc, 0);

        let indexOffset = 0;
        // drawCallInfo: [texId, elemCount, slugCurveTexId, slugBandTexId, slugCurveW, slugCurveH, slugBandW, slugBandH] per draw call = 8 ints each
        const dcStride = 8;
        const dcCount = drawCallInfoI32.length;

        for (let i = 0; i < dcCount; i += dcStride) {
            const texId = drawCallInfoI32[i];
            const elemCount = drawCallInfoI32[i + 1];
            const slugCurveTexId = drawCallInfoI32[i + 2];
            const slugBandTexId = drawCallInfoI32[i + 3];
            const slugCurveW = drawCallInfoI32[i + 4];
            const slugCurveH = drawCallInfoI32[i + 5];
            const slugBandW = drawCallInfoI32[i + 6];
            const slugBandH = drawCallInfoI32[i + 7];
            const dcIndex = (i / dcStride) | 0;

            // Texture
            if (texId > 0 && textures.has(texId)) {
                gl.bindTexture(gl.TEXTURE_2D, textures.get(texId).glTex);
            } else {
                gl.bindTexture(gl.TEXTURE_2D, whiteTexture);
            }

            // Scissor
            const sb = dcIndex * 18;
            for (let j = 0; j < 16; j++) _mat32[j] = scissorDataF64[sb + j];
            gl.uniformMatrix4fv(uScissorMatLoc, false, _mat32);
            gl.uniform2f(uScissorExtLoc, scissorDataF64[sb + 16], scissorDataF64[sb + 17]);

            // Brush
            const bb = dcIndex * 47;
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

            // Bind slug textures if present
            if (slugCurveTexId > 0 && textures.has(slugCurveTexId) && textures.has(slugBandTexId)) {
                gl.activeTexture(gl.TEXTURE1);
                gl.bindTexture(gl.TEXTURE_2D, textures.get(slugCurveTexId).glTex);
                gl.uniform1i(uSlugCurveTexLoc, 1);
                gl.activeTexture(gl.TEXTURE2);
                gl.bindTexture(gl.TEXTURE_2D, textures.get(slugBandTexId).glTex);
                gl.uniform1i(uSlugBandTexLoc, 2);
                gl.uniform2f(uSlugCurveTexSizeLoc, slugCurveW, slugCurveH);
                gl.uniform2f(uSlugBandTexSizeLoc, slugBandW, slugBandH);
                gl.activeTexture(gl.TEXTURE0);
            }

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
canvas.addEventListener('mousedown', () => exports.WasmExample.App.OnMouseDown());
canvas.addEventListener('mouseup', () => exports.WasmExample.App.OnMouseUp());
canvas.addEventListener('wheel', (e) => {
    e.preventDefault();
    exports.WasmExample.App.OnWheel(e.deltaY);
}, { passive: false });
document.addEventListener('keydown', (e) => {
    exports.WasmExample.App.OnKeyDown(e.key);
});
document.addEventListener('keyup', (e) => {
    exports.WasmExample.App.OnKeyUp(e.key);
});

// ─── Render loop ───

let lastTime = performance.now();

function frame(now) {
    const dt = (now - lastTime) / 1000.0;
    lastTime = now;
    exports.WasmExample.App.OnFrame(dt);
    requestAnimationFrame(frame);
}

requestAnimationFrame(frame);
