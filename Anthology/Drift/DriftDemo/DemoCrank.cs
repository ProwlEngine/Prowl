using Prowl.Drift;
using Drift.Joints;
using System.Numerics;

namespace DriftDemo
{
    public class DemoCrank : IDemo
    {
        public string Name => "Crank";

        private Space? _space;

        public void Init(Space space)
        {
            _space = space;

            // Create boundaries
            var staticBody = new Body(Body.BodyType.Static, Vector2.Zero);
            staticBody.AddShape(ShapePoly.CreateBox(0, 0.2f, 20.48f, 0.4f));  // Floor
            staticBody.AddShape(ShapePoly.CreateBox(0, 15.16f, 20.48f, 0.4f)); // Ceiling
            staticBody.AddShape(ShapePoly.CreateBox(-10.04f, 7.68f, 0.4f, 14.56f)); // Left wall
            staticBody.AddShape(ShapePoly.CreateBox(10.04f, 7.68f, 0.4f, 14.56f));  // Right wall
            space.AddBody(staticBody);

            // Create crank mechanism parts

            // Body1 - Short vertical rod (crank arm)
            var body1 = new Body(Body.BodyType.Dynamic, new Vector2(0, 2));
            var shape1 = new ShapeSegment(new Vector2(0, 0), new Vector2(0, 1), 0.2f);
            shape1.Elasticity = 0.4f;
            shape1.Friction = 1.0f;
            shape1.Density = 10;
            body1.AddShape(shape1);
            space.AddBody(body1);

            // Body2 - Connecting rod
            var body2 = new Body(Body.BodyType.Dynamic, new Vector2(0, 3));
            var shape2 = new ShapeSegment(new Vector2(0, 0), new Vector2(0, 2), 0.2f);
            shape2.Elasticity = 0.4f;
            shape2.Friction = 1.0f;
            shape2.Density = 10;
            body2.AddShape(shape2);
            space.AddBody(body2);

            // Body3 - Horizontal beam (piston)
            var body3 = new Body(Body.BodyType.Dynamic, new Vector2(0, 5));
            var shape3 = ShapePoly.CreateBox(0, 0.2f, 4, 0.4f);
            shape3.Elasticity = 0.4f;
            shape3.Friction = 1.0f;
            shape3.Density = 10;
            body3.AddShape(shape3);
            space.AddBody(body3);

            // Create joints for the crank mechanism

            // Motor joint - rotates the crank
            var motorJoint = new RevoluteJoint(staticBody, body1, new Vector2(0, 2));
            motorJoint.CollideConnected = false;
            motorJoint.EnableMotor(true);
            motorJoint.SetMotorSpeed(225 * MathUtil.Deg2Rad); // Rotational speed
            motorJoint.SetMaxMotorTorque(400000000); // High torque
            space.AddJoint(motorJoint);

            // Connect crank arm to connecting rod
            var crankJoint = new RevoluteJoint(body1, body2, new Vector2(0, 3));
            crankJoint.CollideConnected = false;
            space.AddJoint(crankJoint);

            // Connect connecting rod to piston
            var pistonJoint = new RevoluteJoint(body2, body3, new Vector2(0, 5));
            pistonJoint.CollideConnected = false;
            space.AddJoint(pistonJoint);

            // Prismatic joint - constrains piston to horizontal movement
            var prismaticJoint = new PrismaticJoint(staticBody, body3, new Vector2(0, 2), new Vector2(0, 5));
            prismaticJoint.CollideConnected = false;
            space.AddJoint(prismaticJoint);

            // Add some boxes on top of the piston to show the mechanism working
            var box1 = new Body(Body.BodyType.Dynamic, new Vector2(-0.64f, 6));
            var boxShape1 = ShapePoly.CreateBox(0, 0, 0.6f, 0.6f);
            boxShape1.Elasticity = 0.0f;
            boxShape1.Friction = 1.0f;
            boxShape1.Density = 1;
            box1.AddShape(boxShape1);
            space.AddBody(box1);

            var box2 = new Body(Body.BodyType.Dynamic, new Vector2(0, 6));
            var boxShape2 = ShapePoly.CreateBox(0, 0, 0.6f, 0.6f);
            boxShape2.Elasticity = 0.0f;
            boxShape2.Friction = 1.0f;
            boxShape2.Density = 1;
            box2.AddShape(boxShape2);
            space.AddBody(box2);

            var box3 = new Body(Body.BodyType.Dynamic, new Vector2(0.64f, 6));
            var boxShape3 = ShapePoly.CreateBox(0, 0, 0.6f, 0.6f);
            boxShape3.Elasticity = 0.0f;
            boxShape3.Friction = 1.0f;
            boxShape3.Density = 1;
            box3.AddShape(boxShape3);
            space.AddBody(box3);
        }

        public void RunFrame()
        {
            // The motor joint automatically drives the crank mechanism
        }

        public void KeyDown(char key)
        {
            // No special key handling for this demo
        }
    }
}