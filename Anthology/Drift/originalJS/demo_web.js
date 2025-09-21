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

DemoWeb = function() {
	var space;
	function init(s) { 
		space = s;
		var staticBody = new Body(Body.STATIC);        
		staticBody.resetMassData();
		space.addBody(staticBody);

		var body1 = new Body(Body.DYNAMIC, new vec2(-1.4, 9));
		var shape = new ShapeBox(0, 0, 0.4, 0.4);
		shape.e = 0.0;
		shape.u = 1.0;
		shape.density = 1;
		body1.addShape(shape);
		body1.resetMassData();
		space.addBody(body1);

		var body2 = new Body(Body.DYNAMIC, new vec2(-1.4, 6.2));
		var shape = new ShapeBox(0, 0, 0.4, 0.4);
		shape.e = 0.0;
		shape.u = 1.0;
		shape.density = 1;
		body2.addShape(shape);
		body2.resetMassData();
		space.addBody(body2);

		var body3 = new Body(Body.DYNAMIC, new vec2(1.4, 9));
		var shape = new ShapeBox(0, 0, 0.4, 0.4);
		shape.e = 0.0;
		shape.u = 1.0;
		shape.density = 1;
		body3.addShape(shape);
		body3.resetMassData();
		space.addBody(body3);

		var body4 = new Body(Body.DYNAMIC, new vec2(1.4, 6.2));
		var shape = new ShapeBox(0, 0, 0.4, 0.4);
		shape.e = 0.0;
		shape.u = 1.0;
		shape.density = 1;
		body4.addShape(shape);
		body4.resetMassData();
		space.addBody(body4);

		var joint1 = new DistanceJoint(staticBody, body1, new vec2(-4, 11.6), new vec2(-1.6, 9.2));
		joint1.setSpringFrequencyHz(2);
		joint1.setSpringDampingRatio(0.1);
		space.addJoint(joint1);

		var joint2 = new DistanceJoint(staticBody, body2, new vec2(-4, 4.2), new vec2(-1.6, 6));
		joint2.setSpringFrequencyHz(2);
		joint2.setSpringDampingRatio(0.1);
		space.addJoint(joint2);

		var joint3 = new DistanceJoint(staticBody, body3, new vec2(4, 11.6), new vec2(1.6, 9.2));
		joint3.setSpringFrequencyHz(2);
		joint3.setSpringDampingRatio(0.1);
		space.addJoint(joint3);

		var joint4 = new DistanceJoint(staticBody, body4, new vec2(4, 4.2), new vec2(1.6, 6));
		joint4.setSpringFrequencyHz(2);
		joint4.setSpringDampingRatio(0.1);
		space.addJoint(joint4);

		var joint5 = new DistanceJoint(body1, body2, new vec2(-1.4, 8.8), new vec2(-1.4, 6.4));
		joint5.setSpringFrequencyHz(2);
		joint5.setSpringDampingRatio(0.1);
		space.addJoint(joint5);

		var joint6 = new DistanceJoint(body3, body4, new vec2(1.4, 8.8), new vec2(1.4, 6.4));
		joint6.setSpringFrequencyHz(2);
		joint6.setSpringDampingRatio(0.1);
		space.addJoint(joint6);

		var joint7 = new DistanceJoint(body1, body3, new vec2(-1.2, 9), new vec2(1.2, 9));
		joint7.setSpringFrequencyHz(2);
		joint7.setSpringDampingRatio(0.1);
		space.addJoint(joint7);

		var joint8 = new DistanceJoint(body2, body4, new vec2(-1.2, 6.2), new vec2(1.2, 6.2));
		joint8.setSpringFrequencyHz(2);
		joint8.setSpringDampingRatio(0.1);
		space.addJoint(joint8);
	}

	function runFrame() {
	}

	function keyDown(ev) {
	}

	function name() {
		return "Web";
	}

	return {
		init: init,
		runFrame: runFrame,
		keyDown: keyDown,
		name: name
	};
}();