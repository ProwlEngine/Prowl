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

var ARROW_TYPE_NONE = 0;
var ARROW_TYPE_NORMAL = 1;
var ARROW_TYPE_CIRCLE = 2
var ARROW_TYPE_BOX = 3;

RendererCanvas = function() {
	function scissorRect(ctx, x, y, width, height) {
		ctx.beginPath();
  		ctx.rect(x, y, width, height);
 		ctx.closePath();
  		ctx.clip();
  	}

	function drawLine(ctx, p1, p2, lineWidth, strokeStyle) {
		ctx.beginPath();

		ctx.moveTo(p1.x, p1.y);
		ctx.lineTo(p2.x, p2.y);

		ctx.lineWidth = lineWidth;
		ctx.strokeStyle = strokeStyle;
		ctx.stroke();
	}

	function drawDashLine(ctx, p1, p2, lineWidth, dashSize, strokeStyle) {
		var dashSize2 = dashSize * 0.5;
		var dsq = vec2.distsq(p1, p2);

		var d = vec2.truncate(vec2.sub(p2, p1), dashSize);
		var s1 = p1;
		var s2 = vec2.add(p1, d);
		
		ctx.beginPath();

		while (d.lengthsq() > 0) {
			var s3 = vec2.add(s1, vec2.truncate(vec2.sub(s2, s1), dashSize2));

			ctx.moveTo(s1.x, s1.y);
			ctx.lineTo(s3.x, s3.y);

			d = vec2.truncate(vec2.sub(p2, s2), dashSize);
			s1 = s2;
			s2 = vec2.add(s2, d);
		}

		ctx.lineWidth = lineWidth;
		ctx.strokeStyle = strokeStyle;
		ctx.stroke();
	}

	function drawArrow(ctx, p1, p2, type1, type2, headSize, lineWidth, strokeStyle, fillStyle) {		
		if (strokeStyle) {
			ctx.beginPath();

			ctx.moveTo(p1.x, p1.y);
			ctx.lineTo(p2.x, p2.y);

			ctx.lineWidth = lineWidth;
			ctx.strokeStyle = strokeStyle;
			ctx.stroke();
		}

		ctx.beginPath();

		if (type1 != ARROW_TYPE_NONE || type2 != ARROW_TYPE_NONE) {		
			if (type1 == ARROW_TYPE_NORMAL) {
				var sdir = vec2.scale(vec2.normalize(vec2.sub(p1, p2)), headSize);

				var pl = vec2.add(p1, vec2.rotate(sdir, Math.PI * 0.9));
				var pr = vec2.add(p1, vec2.rotate(sdir, -Math.PI * 0.9));

				ctx.moveTo(pl.x, pl.y);
				ctx.lineTo(p1.x, p1.y);
				ctx.lineTo(pr.x, pr.y);
				ctx.lineTo(pl.x, pl.y);
			}
			else if (type1 == ARROW_TYPE_CIRCLE) {
				ctx.moveTo(p1.x, p1.y);
				ctx.arc(p1.x, p1.y, headSize, 0, Math.PI*2, true);
			}
			else if (type1 == ARROW_TYPE_BOX) {
				var rvec = vec2.scale(vec2.normalize(vec2.sub(p1, p2)), headSize);
				var uvec = vec2.perp(rvec);

				var l = vec2.sub(p1, rvec);
				var r = vec2.add(p1, rvec);
				var lb = vec2.sub(l, uvec);
				var lt = vec2.add(l, uvec);
				var rb = vec2.sub(r, uvec);
				var rt = vec2.add(r, uvec);

				ctx.moveTo(lb.x, lb.y);
				ctx.lineTo(rb.x, rb.y);
				ctx.lineTo(rt.x, rt.y);
				ctx.lineTo(lt.x, lt.y);
				ctx.lineTo(lb.x, lb.y);
			}

			if (type2 == ARROW_TYPE_NORMAL) {
				var sdir = vec2.scale(vec2.normalize(vec2.sub(p2, p1)), headSize);

				var pl = vec2.add(p2, vec2.rotate(sdir, Math.PI * 0.9));
				var pr = vec2.add(p2, vec2.rotate(sdir, -Math.PI * 0.9));

				ctx.moveTo(pl.x, pl.y);
				ctx.lineTo(p2.x, p2.y);
				ctx.lineTo(pr.x, pr.y);
				ctx.lineTo(pl.x, pl.y);
			}
			else if (type2 == ARROW_TYPE_CIRCLE) {
				ctx.moveTo(p2.x, p2.y);
				ctx.arc(p2.x, p2.y, headSize, 0, Math.PI*2, true);
			}
			else if (type2 == ARROW_TYPE_BOX) {
				var rvec = vec2.scale(vec2.normalize(vec2.sub(p2, p1)), headSize);
				var uvec = vec2.perp(rvec);

				var l = vec2.sub(p2, rvec);
				var r = vec2.add(p2, rvec);
				var lb = vec2.sub(l, uvec);
				var lt = vec2.add(l, uvec);
				var rb = vec2.sub(r, uvec);
				var rt = vec2.add(r, uvec);

				ctx.moveTo(lb.x, lb.y);
				ctx.lineTo(rb.x, rb.y);
				ctx.lineTo(rt.x, rt.y);
				ctx.lineTo(lt.x, lt.y);
				ctx.lineTo(lb.x, lb.y);
			}
		}

		ctx.closePath();

		if (fillStyle) {			
			ctx.fillStyle = fillStyle;
			ctx.fill();
		}

		if (strokeStyle) {
			ctx.lineWidth = lineWidth;		
			ctx.strokeStyle = strokeStyle;
			ctx.stroke();
		}
	}

	function drawRect(ctx, mins, maxs, lineWidth, strokeStyle, fillStyle) {
		ctx.beginPath();

		ctx.rect(mins.x, mins.y, maxs.x - mins.x, maxs.y - mins.y);

		ctx.closePath();

		if (fillStyle) {			
			ctx.fillStyle = fillStyle;
			ctx.fill();
		}

		if (strokeStyle) {
			ctx.lineWidth = lineWidth;	
			ctx.strokeStyle = strokeStyle;
			ctx.stroke();			
		}
	}

	function drawBox(ctx, center, rvec, uvec, lineWidth, strokeStyle, fillStyle) {
		ctx.beginPath();

		var l = vec2.sub(center, rvec);
		var r = vec2.add(center, rvec);
		var lb = vec2.sub(l, uvec);
		var lt = vec2.add(l, uvec);
		var rb = vec2.sub(r, uvec);
		var rt = vec2.add(r, uvec);

		ctx.moveTo(lb.x, lb.y);
		ctx.lineTo(rb.x, rb.y);
		ctx.lineTo(rt.x, rt.y);
		ctx.lineTo(lt.x, lt.y);
		ctx.lineTo(lb.x, lb.y);

		ctx.closePath();

		if (fillStyle) {			
			ctx.fillStyle = fillStyle;
			ctx.fill();
		}

		if (strokeStyle) {		
			ctx.lineWidth = lineWidth;	
			ctx.strokeStyle = strokeStyle;
			ctx.stroke();			
		}
	}

	function drawCircle(ctx, center, radius, angle, lineWidth, strokeStyle, fillStyle) {
		ctx.beginPath();

		ctx.arc(center.x, center.y, radius, 0, Math.PI*2, false);

		if (fillStyle) {			
			ctx.fillStyle = fillStyle;
			ctx.fill();
		}

		if (strokeStyle) {
			if (typeof angle == "number") {
				ctx.moveTo(center.x, center.y);
				var rt = vec2.add(center, vec2.scale(vec2.rotation(angle), radius));
				ctx.lineTo(rt.x, rt.y);
			}

			ctx.lineWidth = lineWidth;
			ctx.strokeStyle = strokeStyle;
			ctx.stroke();
		}	
	}

	function drawArc(ctx, center, radius, startAngle, endAngle, lineWidth, strokeStyle, fillStyle) {
		ctx.beginPath();

		ctx.moveTo(center.x, center.y);

		var p = vec2.add(center, vec2.scale(vec2.rotation(startAngle), radius));
		ctx.arc(center.x, center.y, radius, startAngle, endAngle, false);
		ctx.lineTo(center.x, center.y);

		ctx.closePath();

		if (fillStyle) {			
			ctx.fillStyle = fillStyle;
			ctx.fill();
		}

		if (strokeStyle) {
			ctx.lineWidth = lineWidth;
			ctx.strokeStyle = strokeStyle;
			ctx.stroke();
		}	
	}

	function drawSegment(ctx, a, b, radius, lineWidth, strokeStyle, fillStyle) {
		ctx.beginPath();

		var dn = vec2.normalize(vec2.perp(vec2.sub(b, a)));
		var start_angle = dn.toAngle(); 
		ctx.arc(a.x, a.y, radius, start_angle, start_angle + Math.PI, false);

		var ds = vec2.scale(dn, -radius);
		var bp = vec2.add(b, ds);
		ctx.lineTo(bp.x, bp.y);

		start_angle += Math.PI;
		ctx.arc(b.x, b.y, radius, start_angle, start_angle + Math.PI, false);

		ds = vec2.scale(dn, radius);
		var ap = vec2.add(a, ds);
		ctx.lineTo(ap.x, ap.y);		

		ctx.closePath();

		if (fillStyle) {			
			ctx.fillStyle = fillStyle;
			ctx.fill();
		}

		if (strokeStyle) {
			ctx.lineWidth = lineWidth;
			ctx.strokeStyle = strokeStyle;
			ctx.stroke();			
		}
	}

	function drawPolygon(ctx, verts, lineWidth, strokeStyle, fillStyle) {
		ctx.beginPath();
		ctx.moveTo(verts[0].x, verts[0].y);

		for (var i = 0; i < verts.length; i++) {
			ctx.lineTo(verts[i].x, verts[i].y);
		}

		ctx.lineTo(verts[verts.length - 1].x, verts[verts.length - 1].y);

		ctx.closePath();

		if (fillStyle) {			
			ctx.fillStyle = fillStyle;
			ctx.fill();
		}

		if (strokeStyle) {
			ctx.lineWidth = lineWidth;
			ctx.strokeStyle = strokeStyle;
			ctx.stroke();
		}
	}

	return {
		scissorRect: scissorRect,
		drawLine: drawLine,
		drawDashLine: drawDashLine,
		drawArrow: drawArrow,
		drawRect: drawRect,
		drawBox: drawBox,
		drawCircle: drawCircle,
		drawArc: drawArc,
		drawSegment: drawSegment,
		drawPolygon: drawPolygon
	}
}();
