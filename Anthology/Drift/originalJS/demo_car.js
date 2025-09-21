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

DemoCar = function() {
	var space;
	function init(s) {
		space = s;
		var staticBody = new Body(Body.STATIC);
		staticBody.addShape(new ShapeBox(-9.84, 5, 0.8, 2));
		staticBody.addShape(new ShapeBox(9.84, 5, 0.8, 2));
		staticBody.addShape(new ShapePoly([new vec2(-10.24, 0), new vec2(-2, 0), new vec2(-2, 1), new vec2(-9.44, 4), new vec2(-10.24, 4)]));
		staticBody.addShape(new ShapePoly([new vec2(2, 0), new vec2(10.24, 0), new vec2(10.24, 4), new vec2(9.44, 4), new vec2(2, 1)]));
		staticBody.resetMassData();
		space.addBody(staticBody);

		// Bridge
		var body_prev;
		for (var i = 0; i < 10; i++) {
			var body = new Body(Body.DYNAMIC, new vec2(-1.8 + i * 0.4, 0.9));
			var shape = new ShapeBox(0, 0, 0.44, 0.2);
			shape.e = 0.1;
			shape.u = 0.8;
			shape.density = 20;
			body.addShape(shape);
			body.resetMassData();
			space.addBody(body);

			if (i == 0) {
				var joint = new RevoluteJoint(staticBody, body, new vec2(-2, 0.9));
				joint.collideConnected = false;
				space.addJoint(joint);
			}
			else {
				var joint = new RevoluteJoint(body_prev, body, new vec2(-2 + i * 0.4, 0.9));
				joint.collideConnected = false;
				space.addJoint(joint);
			}

			body_prev = body;
		}

		var joint = new RevoluteJoint(body, staticBody, new vec2(2, 0.9));
		joint.collideConnected = false;
		space.addJoint(joint);

		// Car body        
		var carBody = new Body(Body.DYNAMIC, new vec2(-8, 5));		
		var shape = new ShapePoly([new vec2(-0.8, 0.48), new vec2(-0.8, 0), new vec2(0.8, 0), new vec2(0.8, 0.32), new vec2(0, 0.84), new vec2(-0.56, 0.84)]);
		shape.e = 0.5;
		shape.u = 1.0;
		shape.density = 6;
		carBody.addShape(shape);
		carBody.resetMassData();
		space.addBody(carBody);

		// Wheel 1        
		var wheel1Body = new Body(Body.DYNAMIC, new vec2(-8.5, 4.9));
		var shape = new ShapeCircle(0, 0, 0.26);
		shape.e = 0.1;
		shape.u = 0.97;
		shape.density = 0.8;
		wheel1Body.addShape(shape);
		wheel1Body.resetMassData();
		space.addBody(wheel1Body);

		var joint = new WheelJoint(carBody, wheel1Body, new vec2(-8.5, 5), new vec2(-8.5, 4.9));
		joint.setSpringFrequencyHz(12);
		joint.setSpringDampingRatio(0.1);
		joint.collideConnected = false;
		/*joint.enableMotor(true);
		joint.setMotorSpeed(deg2rad(-2000));
		joint.setMaxMotorTorque(20000);*/
		space.addJoint(joint);

		// Wheel 2        
		var wheel2Body = new Body(Body.DYNAMIC, new vec2(-7.5, 4.9));
		var shape = new ShapeCircle(0, 0, 0.26);
		shape.e = 0.1;
		shape.u = 0.97;
		shape.density = 0.8;
		wheel2Body.addShape(shape);
		wheel2Body.resetMassData();
		space.addBody(wheel2Body);

		var joint = new WheelJoint(carBody, wheel2Body, new vec2(-7.5, 5), new vec2(-7.5, 4.9));
		joint.setSpringFrequencyHz(12)
		joint.setSpringDampingRatio(0.1);
		/*joint.enableMotor(true);
		joint.setMotorSpeed(deg2rad(-2000));
		joint.setMaxMotorTorque(30000);*/
		joint.collideConnected = false;
		space.addJoint(joint);		

		// Both wheels constrained to be same rotation        
		//space.addJoint(new AngleJoint(wheel1Body, wheel2Body));

		// Car antenna
		var antennaBodies = [];
		for (var i = 0; i < 3; i++) {
			antennaBodies[i] = new Body(Body.DYNAMIC, new vec2(-8.55, 5.94 + 0.2 * i));
			var shape = new ShapeBox(0, 0, 0.04, 0.2);
			shape.e = 0.5;
			shape.u = 1.0;
			shape.density = 0.5;
			antennaBodies[i].addShape(shape);
			antennaBodies[i].resetMassData();
			space.addBody(antennaBodies[i]);

			if (i == 0) {
				var joint = new WeldJoint(carBody, antennaBodies[0], new vec2(-8.55, 5.84 + 0.2 * i));
				joint.collideConnected = false;
				joint.setSpringFrequencyHz(30);
				joint.setSpringDampingRatio(0.1);
				space.addJoint(joint);
			}
			else {
				var joint = new WeldJoint(antennaBodies[i - 1], antennaBodies[i], new vec2(-8.55, 5.84 + 0.2 * i));
				joint.collideConnected = false;
				joint.setSpringFrequencyHz(30);
				joint.setSpringDampingRatio(0.1);
				space.addJoint(joint);
			}
		}
	}

	function runFrame() {
	}

	function keyDown(ev) {
	}

	function name() {
		return "Car";
	}

	return {
		init: init,
		runFrame: runFrame,
		keyDown: keyDown,
		name: name
	};
}();