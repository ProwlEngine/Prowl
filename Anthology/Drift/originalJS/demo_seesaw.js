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

DemoSeeSaw = function() {
	var space;
	function init(s) {
		space = s;
		var staticBody = new Body(Body.STATIC);
		staticBody.addShape(new ShapeBox(0, 0.2, 20.48, 0.4));		
		staticBody.addShape(new ShapeBox(-10.04, 7.68, 0.4, 14.56));
		staticBody.addShape(new ShapeBox(10.04, 7.68, 0.4, 14.56));
		staticBody.resetMassData();
		space.addBody(staticBody);

		var body = new Body(Body.DYNAMIC, new vec2(-3, 1.6));
		var shape = new ShapeBox(0, 0, 2.8, 1.6);
		shape.e = 0.1;
		shape.u = 1.0;
		shape.density = 2;
		body.addShape(shape);
		body.resetMassData();        
		space.addBody(body);

		var body = new Body(Body.DYNAMIC, new vec2(0, 2.8));
		var shape = new ShapeBox(0, 0, 12, 0.2);
		shape.e = 0.4;
		shape.u = 0.7;
		shape.density = 1.2;
		body.addShape(shape);
		body.resetMassData();
		space.addBody(body);

		for (var i = 0; i < 5; i++) {
			for (var j = 0; j <= i; j++) {                
				var body = new Body(Body.DYNAMIC, new vec2((j - i * 0.5) * 0.88 - 3, 7 - i * 0.88));
				var shape = new ShapeBox(0, 0, 0.8, 0.8);
				shape.e = 0.3;
				shape.u = 0.8;
				shape.density = 1;
				body.addShape(shape);
				body.resetMassData();
				space.addBody(body);
			}
		}        

		var body = new Body(Body.DYNAMIC, new vec2(5, 30));
		var shape = new ShapePoly([new vec2(-0.96, 0.7), new vec2(-0.76, 0), new vec2(0.76, 0), new vec2(0.96, 0.7), new vec2(0, 1.48)]);
		shape.e = 0.4;
		shape.u = 1.0;
		shape.density = 2;
		body.addShape(shape);
		body.resetMassData();
		space.addBody(body);
	}

	function runFrame() {
	}

	function keyDown(ev) {
	}

	function name() {
		return "See-saw";
	}

	return {
		init: init,
		runFrame: runFrame,
		keyDown: keyDown,
		name: name
	};
}();