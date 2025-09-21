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

DemoCrank = function() {
	var space;

	function init(s) {
		space = s;
		var staticBody = new Body(Body.STATIC);
		staticBody.addShape(new ShapeBox(0, 0.2, 20.48, 0.4));
		staticBody.addShape(new ShapeBox(0, 15.16, 20.48, 0.4));
		staticBody.addShape(new ShapeBox(-10.04, 7.68, 0.4, 14.56));
		staticBody.addShape(new ShapeBox(10.04, 7.68, 0.4, 14.56));
		staticBody.resetMassData();
		space.addBody(staticBody);

		var body1 = new Body(Body.DYNAMIC, new vec2(0, 2));
		var shape = new ShapeSegment(new vec2(0, 0), new vec2(0, 1), 0.2);
		shape.e = 0.4;
		shape.u = 1.0;
		shape.density = 10;
		body1.addShape(shape);
		body1.resetMassData();        
		space.addBody(body1);

		var body2 = new Body(Body.DYNAMIC, new vec2(0, 3));
		var shape = new ShapeSegment(new vec2(0, 0), new vec2(0, 2), 0.2);
		shape.e = 0.4;
		shape.u = 1.0;
		shape.density = 10;
		body2.addShape(shape);
		body2.resetMassData();
		space.addBody(body2);

		var body3 = new Body(Body.DYNAMIC, new vec2(0, 5));
		var shape = new ShapeBox(0, 0.2, 4, 0.4);
		shape.e = 0.4;
		shape.u = 1.0;
		shape.density = 10;
		body3.addShape(shape);
		body3.resetMassData();
		space.addBody(body3);

		var joint = new RevoluteJoint(staticBody, body1, new vec2(0, 2));
		joint.collideConnected = false;
		joint.enableMotor(true);
		joint.setMotorSpeed(deg2rad(225));
		joint.setMaxMotorTorque(400000000);
		space.addJoint(joint);

		var joint = new RevoluteJoint(body1, body2, new vec2(0, 3));
		joint.collideConnected = false;
		space.addJoint(joint);

		var joint = new RevoluteJoint(body2, body3, new vec2(0, 5));
		joint.collideConnected = false;
		space.addJoint(joint);

		var joint = new PrismaticJoint(staticBody, body3, new vec2(0, 2), new vec2(0, 5));
		joint.collideConnected = false;
		space.addJoint(joint);      

		var body = new Body(Body.DYNAMIC, new vec2(-0.64, 6));
		var shape = new ShapeBox(0, 0, 0.6, 0.6);
		shape.e = 0.0;
		shape.u = 1.0;
		shape.density = 1;
		body.addShape(shape);
		body.resetMassData();
		space.addBody(body);

		var body = new Body(Body.DYNAMIC, new vec2(0, 6));
		var shape = new ShapeBox(0, 0, 0.6, 0.6);
		shape.e = 0.0;
		shape.u = 1.0;
		shape.density = 1;
		body.addShape(shape);
		body.resetMassData();
		space.addBody(body);

		var body = new Body(Body.DYNAMIC, new vec2(0.64, 6));
		var shape = new ShapeBox(0, 0, 0.6, 0.6);
		shape.e = 0.0;
		shape.u = 1.0;
		shape.density = 1;
		body.addShape(shape);
		body.resetMassData();        
		space.addBody(body);      
	}

	function runFrame() {		
	}

	function keyDown(ev) {
	}	

	function name() {
		return "Crank";
	}

	return {
		init: init,
		runFrame: runFrame,
		keyDown: keyDown,
		name: name
	};
}();	
