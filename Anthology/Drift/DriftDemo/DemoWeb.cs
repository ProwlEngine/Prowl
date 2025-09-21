using Prowl.Drift;
using Drift.Joints;

namespace DriftDemo
{
    public class DemoWeb : IDemo
    {
        public string Name => "Web";

        private Space? _space;

        public void Init(Space space)
        {
            _space = space;

            // Create static body (no visible boundaries in this demo)
            var staticBody = new Body(Body.BodyType.Static, Vec2.Zero);
            space.AddBody(staticBody);

            // Create four corner bodies for the web
            var body1 = new Body(Body.BodyType.Dynamic, new Vec2(-1.4f, 9));
            var shape1 = ShapePoly.CreateBox(0, 0, 0.4f, 0.4f);
            shape1.Elasticity = 0.0f;
            shape1.Friction = 1.0f;
            shape1.Density = 1;
            body1.AddShape(shape1);
            space.AddBody(body1);

            var body2 = new Body(Body.BodyType.Dynamic, new Vec2(-1.4f, 6.2f));
            var shape2 = ShapePoly.CreateBox(0, 0, 0.4f, 0.4f);
            shape2.Elasticity = 0.0f;
            shape2.Friction = 1.0f;
            shape2.Density = 1;
            body2.AddShape(shape2);
            space.AddBody(body2);

            var body3 = new Body(Body.BodyType.Dynamic, new Vec2(1.4f, 9));
            var shape3 = ShapePoly.CreateBox(0, 0, 0.4f, 0.4f);
            shape3.Elasticity = 0.0f;
            shape3.Friction = 1.0f;
            shape3.Density = 1;
            body3.AddShape(shape3);
            space.AddBody(body3);

            var body4 = new Body(Body.BodyType.Dynamic, new Vec2(1.4f, 6.2f));
            var shape4 = ShapePoly.CreateBox(0, 0, 0.4f, 0.4f);
            shape4.Elasticity = 0.0f;
            shape4.Friction = 1.0f;
            shape4.Density = 1;
            body4.AddShape(shape4);
            space.AddBody(body4);

            // Create web structure using distance joints (springs)

            // Connect each corner to static anchor points (simulating web attachment points)
            var joint1 = new DistanceJoint(staticBody, body1, new Vec2(-4, 11.6f), new Vec2(-1.6f, 9.2f));
            joint1.SetSpringFrequencyHz(2);
            joint1.SetSpringDampingRatio(0.1f);
            space.AddJoint(joint1);

            var joint2 = new DistanceJoint(staticBody, body2, new Vec2(-4, 4.2f), new Vec2(-1.6f, 6));
            joint2.SetSpringFrequencyHz(2);
            joint2.SetSpringDampingRatio(0.1f);
            space.AddJoint(joint2);

            var joint3 = new DistanceJoint(staticBody, body3, new Vec2(4, 11.6f), new Vec2(1.6f, 9.2f));
            joint3.SetSpringFrequencyHz(2);
            joint3.SetSpringDampingRatio(0.1f);
            space.AddJoint(joint3);

            var joint4 = new DistanceJoint(staticBody, body4, new Vec2(4, 4.2f), new Vec2(1.6f, 6));
            joint4.SetSpringFrequencyHz(2);
            joint4.SetSpringDampingRatio(0.1f);
            space.AddJoint(joint4);

            // Connect vertical sides of the web
            var joint5 = new DistanceJoint(body1, body2, new Vec2(-1.4f, 8.8f), new Vec2(-1.4f, 6.4f));
            joint5.SetSpringFrequencyHz(2);
            joint5.SetSpringDampingRatio(0.1f);
            space.AddJoint(joint5);

            var joint6 = new DistanceJoint(body3, body4, new Vec2(1.4f, 8.8f), new Vec2(1.4f, 6.4f));
            joint6.SetSpringFrequencyHz(2);
            joint6.SetSpringDampingRatio(0.1f);
            space.AddJoint(joint6);

            // Connect horizontal sides of the web
            var joint7 = new DistanceJoint(body1, body3, new Vec2(-1.2f, 9), new Vec2(1.2f, 9));
            joint7.SetSpringFrequencyHz(2);
            joint7.SetSpringDampingRatio(0.1f);
            space.AddJoint(joint7);

            var joint8 = new DistanceJoint(body2, body4, new Vec2(-1.2f, 6.2f), new Vec2(1.2f, 6.2f));
            joint8.SetSpringFrequencyHz(2);
            joint8.SetSpringDampingRatio(0.1f);
            space.AddJoint(joint8);
        }

        public void RunFrame()
        {
            // Nothing special needed per frame for this demo
        }

        public void KeyDown(char key)
        {
            // Could add functionality to disturb the web or add objects to it
        }
    }
}