/*
* Copyright (c) 2012 Ju Hyung Lee
*
* Permission is hereby granted, free of charge, to any person obtaining a copy of this software 
* and associated documentation files (the "Software"), to deal in the Software without 
* restriction, including without limitation the rights to use, copy, modify, merge, publish, 
* distribute, sublicense, and/or sell copies of the Software, and to permit persons to whom the 
* Software is furnished to do so, subject to the following conditions:
*
* The above copyright notice and this permission notice shall be included in all copies or 
* substantial portions of the Software.
*
* THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING 
* BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND 
* NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, 
* DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, 
* OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
*/

RendererWebGL = function() {
	var config = {};
	var pendingTextureLoads = 0;

	function init(canvas) {
		if (!window.WebGLRenderingContext) {
			console.error("Your browser doesn't support WebGL.");
			return;
		}

		var names = [ "webgl", "experimental-webgl", "moz-webgl", "webkit-3d" ];
		var ctx = null;
		for (var i = 0; i < names.length; i++) {
			try {
				ctx = canvas.getContext(names[i], { antialias: false, depth: false, stencil: false });
			} catch (e) {}

			if (ctx) {
				break;
			}
		}

		if (!ctx) {
			alert("Couldn't fetch WebGL rednering context for canvas.");
		}

		if (ctx.getSupportedExtensions) {
			config.extensions = ctx.getSupportedExtensions();
		}
		else {
			config.extensions = [];
		}
		
		config.maxTextureSize = ctx.getParameter(ctx.MAX_TEXTURE_SIZE);
		config.maxTextureImageUnits = ctx.getParameter(ctx.MAX_TEXTURE_IMAGE_UNITS);

		ctx.clearColor(1.0, 1.0, 1.0, 1.0);
		ctx.frontFace(ctx.CCW)
		ctx.cullFace(ctx.BACK);
		ctx.enable(ctx.CULL_FACE);		
	}

	function checkGLError(ctx, msg) {
		var err = ctx.getError();
		var str;

		if (err === ctx.NO_ERROR)
			return;

		switch (err) {
		case ctx.INVALID_ENUM:
			str = "INVALID_ENUM";
			break;
		case ctx.INVALID_VALUE:
			str = "INVALID_VALUE";
			break;
		case ctx.INVALID_OPERATION:
			str = "INVALID_OPERATION";
			break;
		case ctx.OUT_OF_MEMORY:
			str = "OUT_OF_MEMORY";
			break;
		default:
			str = "unknown " + err.toString();
			break;
		}

		console.warn("checkGLError: " + str + " on " + msg);
	}

	function isPowerOfTwo(x) {
		return (x & (x - 1)) == 0;
	}
	
	function nextPowerOfTwo(x) {
		--x;
		for (var i = 1; i < 32; i <<= 1) {
			x = x | x >> i;
		}
		return x + 1;
	}

	function loadTexture(ctx, src) {
		var texture = {};
		texture.image = new Image;

		pendingTextureLoads++;
		
		texture.image.onload = function () {    
			if (!isPowerOfTwo(texture.image.width) || !isPowerOfTwo(texture.image.height)) {
				var canvas = document.createElement("canvas");
				canvas.width = nextPowerOfTwo(texture.image.width);
				canvas.height = nextPowerOfTwo(texture.image.height);
				var ctx = canvas.getContext("2d");
				ctx.drawImage(texture.image, 0, 0, texture.image.width, texture.image.height, 0, 0, canvas.width, canvas.height);
				texture.image = canvas;
			}
			
			texture.texture = ctx.createTexture();
			ctx.bindTexture(ctx.TEXTURE_2D, texture.texture);
			ctx.texParameteri(ctx.TEXTURE_2D, ctx.TEXTURE_MIN_FILTER, ctx.LINEAR_MIPMAP_LINEAR);
			ctx.texParameteri(ctx.TEXTURE_2D, ctx.TEXTURE_MAG_FILTER, ctx.LINEAR);
			ctx.texParameteri(ctx.TEXTURE_2D, ctx.TEXTURE_WRAP_S, ctx.REPEAT);
			ctx.texParameteri(ctx.TEXTURE_2D, ctx.TEXTURE_WRAP_T, ctx.REPEAT);
			//ctx.pixelStorei(ctx.UNPACK_FLIP_Y_WEBGL, true);
			ctx.texImage2D(ctx.TEXTURE_2D, 0, ctx.RGB, ctx.RGB, ctx.UNSIGNED_BYTE, texture.image);
			ctx.generateMipmap(ctx.TEXTURE_2D);

			//checkGLError("error loading texture: " + src);

			pendingTextureLoads--;
		}

		texture.image.onerror = function () {
			//alert(texture.image.src);

			pendingTextureLoads--;
		}

		texture.texture = null;//defaultTexture;
		texture.image.src = src;
		
		return texture;
	}

	function clearRect(ctx, x, y, width, height) {		
		ctx.viewport(x, y, width, height);
        ctx.colorMask(true, true, true, true);
        ctx.clear(ctx.COLOR_BUFFER_BIT);
	}

	function scissorRect(ctx, x, y, width, height) {
		
	}

	function drawLine(ctx, p1, p2, strokeStyle) {
		ctx.drawArrays(ctx.LINES, 0, triangleVertexPositionBuffer.numItems);
	}

	function drawArrow(ctx, p1, p2, strokeStyle) {
		ctx.drawArrays(ctx.LINES, 0, triangleVertexPositionBuffer.numItems);
	}    

	function drawBox(ctx, mins, maxs, strokeStyle, fillStyle) {
		ctx.drawArrays(ctx.LINES, 0, triangleVertexPositionBuffer.numItems);
	}

	function drawCircle(ctx, center, radius, angle, strokeStyle, fillStyle) {
		ctx.drawArrays(ctx.LINES, 0, triangleVertexPositionBuffer.numItems);
	}

	function drawSegment(ctx, a, b, radius, strokeStyle, fillStyle) {
		ctx.drawArrays(ctx.LINES, 0, triangleVertexPositionBuffer.numItems);
	}

	function drawPolygon(ctx, verts, strokeStyle, fillStyle) {
		var buffer = ctx.createBuffer();
		ctx.bindBuffer(ctx.ARRAY_BUFFER, buffer);

		var data = new Float32Array(verts.length * 2);
		for (var i = 0; i < verts.length;) {
			data[i++] = verts.x;
			data[i++] = verts.y;
		}

		ctx.bufferData(ctx.ARRAY_BUFFER, data, ctx.STREAM_DRAW);
		ctx.drawArrays(ctx.LINES, 0, verts.length);

		ctx.deleteBuffer(buffer);
	}

	return {
		init: init,
		clearRect: clearRect,
		scissorRect: scissorRect,
		drawLine: drawLine,
		drawArrow: drawArrow,
		drawBox: drawBox,
		drawCircle: drawCircle,
		drawSegment: drawSegment,
		drawPolygon: drawPolygon,
	}
}();
