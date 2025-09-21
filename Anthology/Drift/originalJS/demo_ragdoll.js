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

DemoRagDoll = function() {
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
			
		// Head
		var bodyHead = new Body(Body.DYNAMIC, new vec2(0, 7.4));
		var shape = new ShapeCircle(0, 0, 0.45);
		shape.e = 0.1;
		shape.u = 0.8;
		shape.density = 5;
		bodyHead.addShape(shape);
		bodyHead.resetMassData();
		space.addBody(bodyHead);

		// Spine1        
		var bodySpine1 = new Body(Body.DYNAMIC, new vec2(0, 6.4));
		//var shape = new ShapeBox(0, 0, 1.5, 0.7);
		var shape = new ShapePoly([new vec2(-0.5, 0.35), new vec2(-0.9, 0.2), new vec2(-0.4, -0.8), new vec2(0.4, -0.8), new vec2(0.9, 0.2), new vec2(0.5, 0.35)]);
		shape.e = 0.1;
		shape.u = 0.8;
		shape.density = 4;
		bodySpine1.addShape(shape);
		bodySpine1.resetMassData();
		space.addBody(bodySpine1);

		// Spine2        
		var bodySpine2 = new Body(Body.DYNAMIC, new vec2(0, 5.6));
		var shape = new ShapePoly([new vec2(-0.6, 0.4), new vec2(-0.55, -0.6), new vec2(0.55, -0.6), new vec2(0.6, 0.4)]);
		shape.e = 0.1;
		shape.u = 0.8;
		shape.density = 4;
		bodySpine2.addShape(shape);
		bodySpine2.resetMassData();
		space.addBody(bodySpine2);	

		// Pelvis        
		var bodyPelvis = new Body(Body.DYNAMIC, new vec2(0, 4.85));
		var shape = new ShapePoly([new vec2(-0.55, 0.4), new vec2(-0.69, -0.4), new vec2(-0.4, -0.6), new vec2(0.4, -0.6), new vec2(0.69, -0.4), new vec2(0.55, 0.4)]);
		shape.e = 0.1;
		shape.u = 0.8;
		shape.density = 4;
		bodyPelvis.addShape(shape);
		bodyPelvis.resetMassData();
		space.addBody(bodyPelvis);

		// Left Arm1        
		var bodyLArm1 = new Body(Body.DYNAMIC, new vec2(-1.25, 6.5));
		var shape = new ShapeBox(0, 0, 1.3, 0.35);
		shape.e = 0.1;
		shape.u = 0.8;
		shape.density = 3;
		bodyLArm1.addShape(shape);
		bodyLArm1.resetMassData();
		space.addBody(bodyLArm1);

		// Left Arm2        
		var bodyLArm2 = new Body(Body.DYNAMIC, new vec2(-2.4, 6.5));
		var shape = new ShapeBox(0, 0, 1.4, 0.35);
		shape.e = 0.1;
		shape.u = 0.8;
		shape.density = 3;
		bodyLArm2.addShape(shape);
		bodyLArm2.resetMassData();
		space.addBody(bodyLArm2);

		// Right Arm1        
		var bodyRArm1 = new Body(Body.DYNAMIC, new vec2(1.25, 6.5));
		var shape = new ShapeBox(0, 0, 1.3, 0.35);
		shape.e = 0.1;
		shape.u = 0.8;
		shape.density = 3;
		bodyRArm1.addShape(shape);
		bodyRArm1.resetMassData();
		space.addBody(bodyRArm1);

		// Right Arm2       
		var bodyRArm2 = new Body(Body.DYNAMIC, new vec2(2.4, 6.5));
		var shape = new ShapeBox(0, 0, 1.4, 0.35);
		shape.e = 0.1;
		shape.u = 0.8;
		shape.density = 3;
		bodyRArm2.addShape(shape);
		bodyRArm2.resetMassData();
		space.addBody(bodyRArm2);

		// Left Leg1       
		var bodyLLeg1 = new Body(Body.DYNAMIC, new vec2(-0.42, 3.6));
		var shape = new ShapeBox(0, 0, 0.5, 2);
		shape.e = 0.1;
		shape.u = 0.8;
		shape.density = 3;
		bodyLLeg1.addShape(shape);
		bodyLLeg1.resetMassData();
		space.addBody(bodyLLeg1);

		// Left Leg2        
		var bodyLLeg2 = new Body(Body.DYNAMIC, new vec2(-0.42, 1.9));
		var shape = new ShapeBox(0, 0, 0.5, 2);
		shape.e = 0.1;
		shape.u = 0.8;
		shape.density = 3;
		bodyLLeg2.addShape(shape);
		bodyLLeg2.resetMassData();
		space.addBody(bodyLLeg2);

		// Right Leg1        
		var bodyRLeg1 = new Body(Body.DYNAMIC, new vec2(0.42, 3.6));
		var shape = new ShapeBox(0, 0, 0.5, 2);
		shape.e = 0.1;
		shape.u = 0.8;
		shape.density = 3;
		bodyRLeg1.addShape(shape);
		bodyRLeg1.resetMassData();
		space.addBody(bodyRLeg1);

		// Right Leg2        
		var bodyRLeg2 = new Body(Body.DYNAMIC, new vec2(0.42, 1.9));
		var shape = new ShapeBox(0, 0, 0.5, 2);
		shape.e = 0.1;
		shape.u = 0.8;
		shape.density = 3;
		bodyRLeg2.addShape(shape);
		bodyRLeg2.resetMassData();
		space.addBody(bodyRLeg2);

		var joint = new RevoluteJoint(bodyHead, bodySpine1, new vec2(0, 6.8));
		joint.enableLimit(true);
		joint.setLimits(deg2rad(-40), deg2rad(40));
		space.addJoint(joint);

		var joint = new RevoluteJoint(bodySpine1, bodySpine2, new vec2(0, 5.8));
		joint.collideConnected = false;
		joint.enableLimit(true);
		joint.setLimits(deg2rad(-5), deg2rad(5));
		space.addJoint(joint);		

		var joint = new RevoluteJoint(bodySpine2, bodyPelvis, new vec2(0, 5.1));
		joint.collideConnected = false;
		joint.enableLimit(true);
		joint.setLimits(deg2rad(-20), deg2rad(20));
		space.addJoint(joint);

		var joint = new RevoluteJoint(bodySpine1, bodyLArm1, new vec2(-0.75, 6.5));
		joint.enableLimit(true);
		joint.setLimits(deg2rad(-100), deg2rad(100));
		space.addJoint(joint);

		var joint = new RevoluteJoint(bodyLArm1, bodyLArm2, new vec2(-1.8, 6.5));
		joint.collideConnected = false;
		joint.enableLimit(true);
		joint.setLimits(deg2rad(-170), deg2rad(10));
		space.addJoint(joint);

		var joint = new RevoluteJoint(bodySpine1, bodyRArm1, new vec2(0.75, 6.5));
		joint.enableLimit(true);
		joint.setLimits(deg2rad(-100), deg2rad(100));
		space.addJoint(joint);

		var joint = new RevoluteJoint(bodyRArm1, bodyRArm2, new vec2(1.8, 6.5));
		joint.collideConnected = false;
		joint.enableLimit(true);
		joint.setLimits(deg2rad(-10), deg2rad(170));
		space.addJoint(joint);

		var joint = new RevoluteJoint(bodyPelvis, bodyLLeg1, new vec2(-0.42, 4.4));
		joint.collideConnected = false;
		joint.enableLimit(true);
		joint.setLimits(deg2rad(-100), deg2rad(50));
		space.addJoint(joint);

		var joint = new RevoluteJoint(bodyLLeg1, bodyLLeg2, new vec2(-0.42, 2.75));
		joint.collideConnected = false;
		joint.enableLimit(true);
		joint.setLimits(deg2rad(-15), deg2rad(150));
		space.addJoint(joint);

		var joint = new RevoluteJoint(bodyPelvis, bodyRLeg1, new vec2(0.42, 4.4));
		joint.collideConnected = false;
		joint.enableLimit(true);        
		joint.setLimits(deg2rad(-50), deg2rad(100));
		space.addJoint(joint);

		var joint = new RevoluteJoint(bodyRLeg1, bodyRLeg2, new vec2(0.42, 2.75));
		joint.collideConnected = false;
		joint.enableLimit(true);
		joint.setLimits(deg2rad(-150), deg2rad(15));
		space.addJoint(joint);

		bodyHead.applyLinearImpulse(new vec2(120, 0), new vec2(0, 7.34));
	}

	function runFrame() {
	}

	function keyDown(ev) {
	}

	function name() {
		return "Rag-doll";
	}

	return {
		init: init,
		runFrame: runFrame,
		keyDown: keyDown,
		name: name
	};
}();