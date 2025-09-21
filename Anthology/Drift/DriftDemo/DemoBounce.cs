using Prowl.Drift;

namespace DriftDemo
{
    public class DemoBounce : IDemo
    {
        public string Name => "Bounce-test";

        private Space? _space;

        public void Init(Space space)
        {
            _space = space;

            // Create static body for boundaries
            var staticBody = new Body(Body.BodyType.Static, Vec2.Zero);
            staticBody.AddShape(ShapePoly.CreateBox(0, 0.2f, 20.48f, 0.4f));
            staticBody.AddShape(ShapePoly.CreateBox(0, 15.16f, 20.48f, 0.4f));
            staticBody.AddShape(ShapePoly.CreateBox(-10.04f, 7.68f, 0.4f, 14.56f));
            staticBody.AddShape(ShapePoly.CreateBox(10.04f, 7.68f, 0.4f, 14.56f));
            staticBody.ResetMassData();
            space.AddBody(staticBody);

            // Create bouncing balls with different restitution values
            for (int i = 0; i <= 10; i++)
            {
                var body = new Body(Body.BodyType.Dynamic, new Vec2(-6 + i * 1.2f, 8));
                var shape = new ShapeCircle(0, 0, 0.4f);
                shape.Elasticity = i / 10f;  // Restitution varies from 0 to 1
                shape.Friction = 1.0f;     // Friction
                shape.Density = 1;
                body.AddShape(shape);
                body.ResetMassData();
                space.AddBody(body);
            }
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