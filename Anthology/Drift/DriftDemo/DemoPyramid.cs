using Prowl.Drift;

namespace DriftDemo
{
    public class DemoPyramid : IDemo
    {
        public string Name => "Pyramid";

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
            space.AddBody(staticBody);

            // Create pyramid of boxes
            for (int i = 0; i < 9; i++)
            {
                for (int j = 0; j <= i; j++)
                {
                    var pos = new Vec2((j - i * 0.5f) * 0.84f, 10 - i * 0.84f);
                    var body = new Body(Body.BodyType.Dynamic, pos);
                    var shape = ShapePoly.CreateBox(0, 0, 0.72f, 0.72f);
                    shape.Elasticity = 0.25f;
                    shape.Friction = 1.0f;
                    shape.Density = 1;
                    body.AddShape(shape);
                    space.AddBody(body);
                }
            }

            // Optional: Add a bowling ball (uncomment if desired)
            /*
            var ballBody = new Body(Body.BodyType.Dynamic, new Vec2(0, 1));
            var ballShape = new ShapeCircle(0, 0, 0.4f);
            ballShape.Elasticity = 0.1f;
            ballShape.Friction = 1.0f;
            ballShape.Density = 2;
            ballBody.AddShape(ballShape);
            space.AddBody(ballBody);
            */
        }

        public void RunFrame()
        {
            // Nothing special needed per frame for this demo
        }

        public void KeyDown(char key)
        {
            // Add a bowling ball when spacebar is pressed
            if (key == ' ' && _space != null)
            {
                var ballBody = new Body(Body.BodyType.Dynamic, new Vec2(0, 12));
                var ballShape = new ShapeCircle(0, 0, 0.4f);
                ballShape.Elasticity = 0.1f;
                ballShape.Friction = 1.0f;
                ballShape.Density = 2;
                ballBody.AddShape(ballShape);
                _space.AddBody(ballBody);
            }
        }
    }
}