using Prowl.Drift;
using Drift.Joints;

namespace DriftDemo
{
    public class DemoCar : IDemo
    {
        public string Name => "Car";

        private Space? _space;

        public void Init(Space space)
        {
            _space = space;

            // Create static environment
            var staticBody = new Body(Body.BodyType.Static, Vec2.Zero);

            // Ramps on both sides
            staticBody.AddShape(ShapePoly.CreateBox(-9.84f, 5, 0.8f, 2));
            staticBody.AddShape(ShapePoly.CreateBox(9.84f, 5, 0.8f, 2));

            // Left ramp polygon
            var leftRampVerts = new Vec2[]
            {
                new Vec2(-10.24f, 0),
                new Vec2(-2, 0),
                new Vec2(-2, 1),
                new Vec2(-9.44f, 4),
                new Vec2(-10.24f, 4)
            };
            staticBody.AddShape(new ShapePoly(leftRampVerts));

            // Right ramp polygon
            var rightRampVerts = new Vec2[]
            {
                new Vec2(2, 0),
                new Vec2(10.24f, 0),
                new Vec2(10.24f, 4),
                new Vec2(9.44f, 4),
                new Vec2(2, 1)
            };
            staticBody.AddShape(new ShapePoly(rightRampVerts));

            space.AddBody(staticBody);

            // Create bridge
            Body? prevBody = null;
            for (int i = 0; i < 10; i++)
            {
                var body = new Body(Body.BodyType.Dynamic, new Vec2(-1.8f + i * 0.4f, 0.9f));
                var shape = ShapePoly.CreateBox(0, 0, 0.44f, 0.2f);
                shape.Elasticity = 0.1f;
                shape.Friction = 0.8f;
                shape.Density = 20;
                body.AddShape(shape);
                space.AddBody(body);

                if (i == 0)
                {
                    // Connect first bridge segment to static body
                    var joint = new RevoluteJoint(staticBody, body, new Vec2(-2, 0.9f));
                    joint.CollideConnected = false;
                    space.AddJoint(joint);
                }
                else
                {
                    // Connect to previous segment
                    var joint = new RevoluteJoint(prevBody!, body, new Vec2(-2 + i * 0.4f, 0.9f));
                    joint.CollideConnected = false;
                    joint.Breakable = true;
                    joint.MaxForce = 1000;
                    space.AddJoint(joint);
                }

                prevBody = body;
            }

            // Connect last bridge segment to static body
            var lastJoint = new RevoluteJoint(prevBody!, staticBody, new Vec2(2, 0.9f));
            lastJoint.CollideConnected = false;
            space.AddJoint(lastJoint);

            // Create car body
            var carBody = new Body(Body.BodyType.Dynamic, new Vec2(-8, 5));
            var carVerts = new Vec2[]
            {
                new Vec2(-0.8f, 0.48f),
                new Vec2(-0.8f, 0),
                new Vec2(0.8f, 0),
                new Vec2(0.8f, 0.32f),
                new Vec2(0, 0.84f),
                new Vec2(-0.56f, 0.84f)
            };
            var carShape = new ShapePoly(carVerts);
            carShape.Elasticity = 0.5f;
            carShape.Friction = 1.0f;
            carShape.Density = 6;
            carBody.AddShape(carShape);
            space.AddBody(carBody);

            // Create wheel 1 (rear)
            var wheel1Body = new Body(Body.BodyType.Dynamic, new Vec2(-8.5f, 4.9f));
            var wheel1Shape = new ShapeCircle(0, 0, 0.26f);
            wheel1Shape.Elasticity = 0.1f;
            wheel1Shape.Friction = 0.97f;
            wheel1Shape.Density = 0.8f;
            wheel1Body.AddShape(wheel1Shape);
            space.AddBody(wheel1Body);

            var wheelJoint1 = new WheelJoint(carBody, wheel1Body, new Vec2(-8.5f, 5), new Vec2(-8.5f, 4.9f));
            wheelJoint1.SetSpringFrequencyHz(12);
            wheelJoint1.SetSpringDampingRatio(0.1f);
            wheelJoint1.CollideConnected = false;
            space.AddJoint(wheelJoint1);

            // Create wheel 2 (front)
            var wheel2Body = new Body(Body.BodyType.Dynamic, new Vec2(-7.5f, 4.9f));
            var wheel2Shape = new ShapeCircle(0, 0, 0.26f);
            wheel2Shape.Elasticity = 0.1f;
            wheel2Shape.Friction = 0.97f;
            wheel2Shape.Density = 0.8f;
            wheel2Body.AddShape(wheel2Shape);
            space.AddBody(wheel2Body);

            var wheelJoint2 = new WheelJoint(carBody, wheel2Body, new Vec2(-7.5f, 5), new Vec2(-7.5f, 4.9f));
            wheelJoint2.SetSpringFrequencyHz(12);
            wheelJoint2.SetSpringDampingRatio(0.1f);
            wheelJoint2.CollideConnected = false;
            space.AddJoint(wheelJoint2);

            // Create car antenna (flexible antenna made of connected segments)
            var antennaBodies = new Body[3];
            for (int i = 0; i < 3; i++)
            {
                antennaBodies[i] = new Body(Body.BodyType.Dynamic, new Vec2(-8.55f, 5.94f + 0.2f * i));
                var antennaShape = ShapePoly.CreateBox(0, 0, 0.04f, 0.2f);
                antennaShape.Elasticity = 0.5f;
                antennaShape.Friction = 1.0f;
                antennaShape.Density = 0.5f;
                antennaBodies[i].AddShape(antennaShape);
                space.AddBody(antennaBodies[i]);

                if (i == 0)
                {
                    // Connect first antenna segment to car body
                    var antennaJoint = new WeldJoint(carBody, antennaBodies[0], new Vec2(-8.55f, 5.84f + 0.2f * i));
                    antennaJoint.CollideConnected = false;
                    antennaJoint.SetSpringFrequencyHz(30);
                    antennaJoint.SetSpringDampingRatio(0.1f);
                    space.AddJoint(antennaJoint);
                }
                else
                {
                    // Connect to previous antenna segment
                    var antennaJoint = new WeldJoint(antennaBodies[i - 1], antennaBodies[i], new Vec2(-8.55f, 5.84f + 0.2f * i));
                    antennaJoint.CollideConnected = false;
                    antennaJoint.SetSpringFrequencyHz(30);
                    antennaJoint.SetSpringDampingRatio(0.1f);
                    space.AddJoint(antennaJoint);
                }
            }
        }

        public void RunFrame()
        {
            // Could add motor controls here in the future
        }

        public void KeyDown(char key)
        {
            // Could add car controls here (motor speed, etc.)
        }
    }
}