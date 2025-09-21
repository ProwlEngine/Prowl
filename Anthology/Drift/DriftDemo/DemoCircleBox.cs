using Prowl.Drift;

namespace DriftDemo
{
    public class DemoCircleBox : IDemo
    {
        public string Name => "CircleBox";

        private Space? _space;

        public void Init(Space space)
        {
            _space = space;

            // Create static body for boundaries
            var staticBody = new Body(Body.BodyType.Static, Vec2.Zero);
            staticBody.AddShape(ShapePoly.CreateBox(0, 0.2f, 20.48f, 0.4f));  // Ground
            staticBody.AddShape(ShapePoly.CreateBox(-10.04f, 7.68f, 0.4f, 14.56f));  // Left wall
            staticBody.AddShape(ShapePoly.CreateBox(10.04f, 7.68f, 0.4f, 14.56f));   // Right wall
            space.AddBody(staticBody);

            var pos = new Vec2(0, 7.5f);
            var body = new Body(Body.BodyType.Kinetic, pos);
            var shape = ShapePoly.CreateBox(0, 0, 2f, 2f);
            shape.Elasticity = 0.1f;
            shape.Friction = 1.0f;
            shape.Density = 1;
            body.AddShape(shape);
            space.AddBody(body);
        }

        public void RunFrame()
        {
            // Nothing special needed per frame for this demo
        }

        public void KeyDown(char key)
        {
            for (int i = 0; i < 10; i++)
            {
                // Add a bowling ball when spacebar is pressed
                if (key == ' ' && _space != null)
                {
                    var ballBody = new Body(Body.BodyType.Dynamic, new Vec2(0, 12));
                    float xRand = ((float)Random.Shared.NextDouble() - 0.5f) * 2;
                    float yRand = ((float)Random.Shared.NextDouble() - 0.5f) * 2;
                    var ballShape = new ShapeCircle(xRand, yRand, 0.25f);
                    ballShape.Elasticity = 0.1f;
                    ballShape.Friction = 1.0f;
                    ballShape.Density = 2;
                    ballBody.AddShape(ballShape);
                    _space.AddBody(ballBody);
                }
            }
        }
    }
}