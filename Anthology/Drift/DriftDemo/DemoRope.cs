using Prowl.Drift;
using Drift.Joints;

namespace DriftDemo
{
    public class DemoRope : IDemo
    {
        public string Name => "Rope";

        private Space? _space;

        public void Init(Space space)
        {
            _space = space;

            // Create static body for ground
            var staticBody = new Body(Body.BodyType.Static, Vec2.Zero);
            staticBody.AddShape(ShapePoly.CreateBox(0, 0.2f, 20.48f, 0.4f));
            space.AddBody(staticBody);

            var bodies = new Body[10];

            // Create rope chain
            for (int i = 0; i < 10; i++)
            {
                if (i == 9)
                {
                    // Last segment is a heavy box
                    var shape = ShapePoly.CreateBox(0, 0, 1, 1);
                    shape.Elasticity = 0.0f;
                    shape.Friction = 0.5f;
                    shape.Density = 1;
                    bodies[i] = new Body(Body.BodyType.Dynamic, new Vec2(i * 0.8f, 10));
                    bodies[i].AddShape(shape);
                    // Set collision categories (simulate collision filtering)
                    // bodies[i].CategoryBits = 0x0002;
                }
                else
                {
                    // Rope segments
                    var shape = ShapePoly.CreateBox(0, 0, 0.8f, 0.2f);
                    shape.Elasticity = 0.0f;
                    shape.Friction = 0.5f;
                    shape.Density = 1;
                    bodies[i] = new Body(Body.BodyType.Dynamic, new Vec2(0.4f + i * 0.8f, 10));
                    bodies[i].AddShape(shape);
                    // Set collision categories (simulate collision filtering)
                    // bodies[i].CategoryBits = 0x0001;
                    // bodies[i].MaskBits = 0xFFFF & ~0x0002;
                }

                space.AddBody(bodies[i]);

                // Create joints to connect rope segments
                if (i == 0)
                {
                    // Connect first segment to static body
                    var joint = new RevoluteJoint(staticBody, bodies[i], new Vec2(0, 10));
                    joint.CollideConnected = false;
                    space.AddJoint(joint);
                }
                else
                {
                    // Connect to previous segment
                    var joint = new RevoluteJoint(bodies[i - 1], bodies[i], new Vec2(i * 0.8f, 10));
                    joint.CollideConnected = false;
                    space.AddJoint(joint);
                }
            }

            // Add rope joint as length constraint
            var ropeJoint = new RopeJoint(staticBody, bodies[9], new Vec2(0, 10), new Vec2(9 * 0.8f, 10));
            ropeJoint.CollideConnected = false;
            space.AddJoint(ropeJoint);
        }

        public void RunFrame()
        {
            // Nothing special needed per frame for this demo
        }

        public void KeyDown(char key)
        {
            // No special key handling for this demo
        }
    }
}