using Physics2D;

namespace DriftDemo
{
    public class DemoCircles : IDemo
    {
        public string Name => "Circles";

        private Space? _space;
        private Random _random = new();

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

            // Create random circles
            for (int i = 0; i < 25; i++)
            {
                var pos = new Vec2(
                    (float)(_random.NextDouble() * 20 - 10),
                    (float)(0.5 + _random.NextDouble() * 14)
                );
                var body = new Body(Body.BodyType.Dynamic, pos);

                float radius = 1.4f * Math.Max((float)_random.NextDouble(), 0.2f);
                var shape = new ShapeCircle(0, 0, radius);
                shape.Elasticity = 0.6f;     // Restitution
                shape.Friction = 0.9f;     // Friction
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