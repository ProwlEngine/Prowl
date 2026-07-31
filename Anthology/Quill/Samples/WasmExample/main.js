// Prowl.Quill WasmExample - No Blazor, pure .NET WASM + WebGL2

import { dotnet } from './_framework/dotnet.js';
import { CANVAS_VERTEX_SHADER, CANVAS_FRAGMENT_SHADER } from './canvasShaders.generated.js';

let gl = null;
let program = null;
let vao = null;
let vbo = null;
let ebo = null;
let textures = new Map();
let whiteTexture = null;

let uProjectionLoc, uTextureLoc, uFontTextureLoc, uScissorMatLoc, uScissorExtLoc, uDpiScaleLoc;
let uBrushMatLoc, uBrushTypeLoc, uBrushColor1Loc, uBrushColor2Loc;
let uBrushParamsLoc, uBrushParams2Loc, uBrushTextureMatLoc;
let uAtlasTexelSizeLoc, uViewportSizeLoc, uBackdropBlurAmountLoc, uBackdropFlipYLoc;

const VERTEX_SIZE = 20; // 8 position + 8 uv + 4 color

// Pre-allocated buffers for matrix uniforms (avoids per-draw-call Float32Array allocations)
const _mat32 = new Float32Array(16);
const _proj32 = new Float32Array(16);

// The generated shader comes from Slang, which emits mul(M, v) as v * M, so every matrix is
// uploaded transposed. The .NET side already hands over column-major data, which is what that needs.
const MATRIX_TRANSPOSE = true;

// Orthographic projection matching the other backends (0, w, h, 0, -1, 1), written column-major.
function setProjection(w, h) {
    _proj32.fill(0);
    _proj32[0] = 2 / w;
    _proj32[5] = -2 / h;
    _proj32[10] = -1;
    _proj32[12] = -1;
    _proj32[13] = 1;
    _proj32[15] = 1;
}



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

        const vs = createShader(gl.VERTEX_SHADER, CANVAS_VERTEX_SHADER);
        const fs = createShader(gl.FRAGMENT_SHADER, CANVAS_FRAGMENT_SHADER);
        program = createProgram(vs, fs);
        gl.deleteShader(vs);
        gl.deleteShader(fs);

        uProjectionLoc = gl.getUniformLocation(program, 'projection');
        uTextureLoc = gl.getUniformLocation(program, 'texture0');
        uFontTextureLoc = gl.getUniformLocation(program, 'fontTexture');
        uScissorMatLoc = gl.getUniformLocation(program, 'scissorMat');
        uScissorExtLoc = gl.getUniformLocation(program, 'scissorExt');
        uDpiScaleLoc = gl.getUniformLocation(program, 'dpiScale');
        uBrushMatLoc = gl.getUniformLocation(program, 'brushMat');
        uBrushTypeLoc = gl.getUniformLocation(program, 'brushType');
        uBrushColor1Loc = gl.getUniformLocation(program, 'brushColor1');
        uBrushColor2Loc = gl.getUniformLocation(program, 'brushColor2');
        uBrushParamsLoc = gl.getUniformLocation(program, 'brushParams');
        uBrushParams2Loc = gl.getUniformLocation(program, 'brushParams2');
        uBrushTextureMatLoc = gl.getUniformLocation(program, 'brushTextureMat');
        uAtlasTexelSizeLoc = gl.getUniformLocation(program, 'atlasTexelSize');
        uViewportSizeLoc = gl.getUniformLocation(program, 'viewportSize');
        uBackdropBlurAmountLoc = gl.getUniformLocation(program, 'backdropBlurAmount');
        uBackdropFlipYLoc = gl.getUniformLocation(program, 'backdropFlipY');

        vao = gl.createVertexArray();
        gl.bindVertexArray(vao);
        vbo = gl.createBuffer();
        gl.bindBuffer(gl.ARRAY_BUFFER, vbo);
        ebo = gl.createBuffer();
        gl.bindBuffer(gl.ELEMENT_ARRAY_BUFFER, ebo);

        // 20-byte vertex: pos(8) + uv(8) + color(4)
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
        // Framebuffer size, not CSS size, so the projection stays correct on HiDPI displays.
        setProjection(canvas.width, canvas.height);
        gl.uniformMatrix4fv(uProjectionLoc, MATRIX_TRANSPOSE, _proj32);
        gl.uniform1f(uDpiScaleLoc, canvasScale || 1.0);
        gl.uniform2f(uViewportSizeLoc, canvas.width, canvas.height);
        // This backend has no backdrop blur pass, so the composite branch stays off.
        gl.uniform1f(uBackdropBlurAmountLoc, 0);
        gl.uniform1i(uBackdropFlipYLoc, 0);
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

        gl.uniform1i(uTextureLoc, 0);     // brush/shape texture on unit 0
        gl.uniform1i(uFontTextureLoc, 1); // font atlas on unit 1

        let indexOffset = 0;
        // drawCallInfo: [texId, fontTexId, elemCount] per draw call = 3 ints each
        const dcStride = 3;
        const dcCount = drawCallInfoI32.length;

        for (let i = 0; i < dcCount; i += dcStride) {
            const texId = drawCallInfoI32[i];
            const fontTexId = drawCallInfoI32[i + 1];
            const elemCount = drawCallInfoI32[i + 2];
            const dcIndex = (i / dcStride) | 0;

            // Font atlas on unit 1 (for text). Its texel size feeds the distance-field range, which
            // the generated shader takes as a uniform rather than calling textureSize.
            gl.activeTexture(gl.TEXTURE1);
            const fontInfo = (fontTexId > 0 && textures.has(fontTexId)) ? textures.get(fontTexId) : null;
            gl.bindTexture(gl.TEXTURE_2D, fontInfo ? fontInfo.glTex : whiteTexture);
            gl.uniform2f(uAtlasTexelSizeLoc,
                fontInfo && fontInfo.width > 0 ? 1 / fontInfo.width : 0,
                fontInfo && fontInfo.height > 0 ? 1 / fontInfo.height : 0);

            // Shape/brush texture on unit 0
            gl.activeTexture(gl.TEXTURE0);
            if (texId > 0 && textures.has(texId)) {
                gl.bindTexture(gl.TEXTURE_2D, textures.get(texId).glTex);
            } else {
                gl.bindTexture(gl.TEXTURE_2D, whiteTexture);
            }

            // Scissor
            const sb = dcIndex * 18;
            for (let j = 0; j < 16; j++) _mat32[j] = scissorDataF64[sb + j];
            gl.uniformMatrix4fv(uScissorMatLoc, MATRIX_TRANSPOSE, _mat32);
            gl.uniform2f(uScissorExtLoc, scissorDataF64[sb + 16], scissorDataF64[sb + 17]);

            // Brush
            const bb = dcIndex * 47;
            const brushType = brushDataF64[bb] | 0;
            gl.uniform1i(uBrushTypeLoc, brushType);
            for (let j = 0; j < 16; j++) _mat32[j] = brushDataF64[bb + 1 + j];
            gl.uniformMatrix4fv(uBrushMatLoc, MATRIX_TRANSPOSE, _mat32);
            gl.uniform4f(uBrushColor1Loc, brushDataF64[bb+17], brushDataF64[bb+18], brushDataF64[bb+19], brushDataF64[bb+20]);
            gl.uniform4f(uBrushColor2Loc, brushDataF64[bb+21], brushDataF64[bb+22], brushDataF64[bb+23], brushDataF64[bb+24]);
            gl.uniform4f(uBrushParamsLoc, brushDataF64[bb+25], brushDataF64[bb+26], brushDataF64[bb+27], brushDataF64[bb+28]);
            gl.uniform2f(uBrushParams2Loc, brushDataF64[bb+29], brushDataF64[bb+30]);
            for (let j = 0; j < 16; j++) _mat32[j] = brushDataF64[bb + 31 + j];
            gl.uniformMatrix4fv(uBrushTextureMatLoc, MATRIX_TRANSPOSE, _mat32);

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
