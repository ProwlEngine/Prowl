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

const VERTEX_SIZE = 20;

// Pre-allocated buffer for matrix uniforms (avoids per-draw-call Float32Array allocations)
const _mat32 = new Float32Array(16);


const VS_SOURCE = `#version 300 es
precision highp float;
layout(location = 0) in vec2 aPos;
layout(location = 1) in vec2 aUV;
layout(location = 2) in vec4 aColor;
uniform vec2 uViewport;
out vec2 vUV;
out vec4 vColor;
out vec2 vPos;
void main() {
    vec2 pos = (aPos / uViewport) * 2.0 - 1.0;
    pos.y = -pos.y;
    gl_Position = vec4(pos, 0.0, 1.0);
    vUV = aUV;
    vColor = aColor;
    vPos = aPos;
}
`;

const FS_SOURCE = `#version 300 es
precision highp float;
in vec2 vUV;
in vec4 vColor;
in vec2 vPos;
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
out vec4 fragColor;

float calculateBrushFactor() {
    if (uBrushType == 0) return 0.0;
    vec2 logicalPos = vPos / uDpiScale;
    vec2 tp = (uBrushMat * vec4(logicalPos, 0.0, 1.0)).xy;
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
    vec2 lp = p / uDpiScale;
    vec2 tp = (uScissorMat * vec4(lp, 0.0, 1.0)).xy;
    vec2 le = uScissorExt / uDpiScale;
    vec2 d = abs(tp) - le;
    float hp = 0.5 / uDpiScale;
    vec2 se = vec2(hp) - d;
    return clamp(se.x, 0.0, 1.0) * clamp(se.y, 0.0, 1.0);
}

void main() {
    float mask = scissorMask(vPos);
    vec4 color = vColor;
    if (uBrushType > 0) {
        color = mix(uBrushColor1, uBrushColor2, calculateBrushFactor());
    }
    if (vUV.x >= 2.0) {
        fragColor = color * texture(uTexture, vUV - vec2(2.0, 0.0)) * mask;
        return;
    }
    vec2 ps = fwidth(vUV);
    vec2 ed = min(vUV, 1.0 - vUV);
    float eaX = ps.x > 0.0 ? smoothstep(0.0, ps.x, ed.x) : 1.0;
    float eaY = ps.y > 0.0 ? smoothstep(0.0, ps.y, ed.y) : 1.0;
    float ea = clamp(eaX * eaY, 0.0, 1.0);
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

        vao = gl.createVertexArray();
        gl.bindVertexArray(vao);
        vbo = gl.createBuffer();
        gl.bindBuffer(gl.ARRAY_BUFFER, vbo);
        ebo = gl.createBuffer();
        gl.bindBuffer(gl.ELEMENT_ARRAY_BUFFER, ebo);

        gl.enableVertexAttribArray(0);
        gl.vertexAttribPointer(0, 2, gl.FLOAT, false, VERTEX_SIZE, 0);
        gl.enableVertexAttribArray(1);
        gl.vertexAttribPointer(1, 2, gl.FLOAT, false, VERTEX_SIZE, 8);
        gl.enableVertexAttribArray(2);
        gl.vertexAttribPointer(2, 4, gl.UNSIGNED_BYTE, true, VERTEX_SIZE, 16);
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
        const dcCount = drawCallInfoI32.length;

        for (let i = 0; i < dcCount; i += 2) {
            const texId = drawCallInfoI32[i];
            const elemCount = drawCallInfoI32[i + 1];
            const dcIndex = i >> 1;

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
