using Prowl.Drift;
using System.Numerics;

namespace DriftDemo
{
    public class DemoRaycast : IDemo
    {
        public string Name => "Raycast Character Controller";

        private Space? _space;
        private Body? _characterBody;
        private readonly List<Body> _obstacles = new();
        private Vector2 _raycastDirection = Vector2.UnitX;

        public void Init(Space space)
        {
            _space = space;
            _obstacles.Clear();

            var staticBody = new Body(Body.BodyType.Static, Vector2.Zero);
            staticBody.AddShape(ShapePoly.CreateBox(0, 0.2f, 20.48f, 0.4f));        // Bottom wall
            staticBody.AddShape(ShapePoly.CreateBox(0, 15.16f, 20.48f, 0.4f));      // Top wall
            staticBody.AddShape(ShapePoly.CreateBox(-10.04f, 7.68f, 0.4f, 14.56f)); // Left wall
            staticBody.AddShape(ShapePoly.CreateBox(10.04f, 7.68f, 0.4f, 14.56f));  // Right wall
            space.AddBody(staticBody);

            _characterBody = new Body(Body.BodyType.Dynamic, new Vector2(0, 2));
            var characterShape = ShapePoly.CreateBox(0, 0, 0.8f, 1.6f);
            characterShape.Density = 2.0f;
            characterShape.Friction = 0.3f;
            characterShape.Elasticity = 0.1f;
            _characterBody.AddShape(characterShape);
            _characterBody.SetFixedRotation(true);
            space.AddBody(_characterBody);

            // Create various obstacles
            CreateObstacles(space);
        }

        private void CreateObstacles(Space space)
        {
            for (int i = 0; i < 6; i++)
            {
                var box = new Body(Body.BodyType.Dynamic, new Vector2(-7 + i * 2.5f, 3 + (i % 2) * 2));
                var boxShape = ShapePoly.CreateBox(0, 0, 0.6f, 0.6f);
                boxShape.Density = 1.0f;
                boxShape.Friction = 0.7f;
                boxShape.Elasticity = 0.3f;
                box.AddShape(boxShape);
                space.AddBody(box);
                _obstacles.Add(box);
            }

            for (int i = 0; i < 4; i++)
            {
                var circle = new Body(Body.BodyType.Dynamic, new Vector2(-6 + i * 4, 7 + (i % 2) * 1.5f));
                var circleShape = new ShapeCircle(0, 0, 0.4f);
                circleShape.Density = 0.8f;
                circleShape.Friction = 0.6f;
                circleShape.Elasticity = 0.5f;
                circle.AddShape(circleShape);
                space.AddBody(circle);
                _obstacles.Add(circle);
            }

            for (int i = 0; i < 3; i++)
            {
                var platform = new Body(Body.BodyType.Static, new Vector2(-4 + i * 4, 5 + i * 3));
                var platformShape = new ShapeSegment(new Vector2(-1.5f, 0), new Vector2(1.5f, 0), 0.1f);
                platformShape.Friction = 0.9f;
                platformShape.Elasticity = 0.0f;
                platform.AddShape(platformShape);
                space.AddBody(platform);
                _obstacles.Add(platform);
            }

            for (int i = 0; i < 3; i++)
            {
                var triangle = new Body(Body.BodyType.Dynamic, new Vector2(-4 + i * 4, 11 + i * 1.5f));
                var triangleVerts = new Vector2[]
                {
                    new Vector2(0, 0.8f),      // top
                    new Vector2(-0.6f, -0.4f), // bottom left
                    new Vector2(0.6f, -0.4f)   // bottom right
                };
                var triangleShape = new ShapePoly(triangleVerts);
                triangleShape.Density = 1.2f;
                triangleShape.Friction = 0.5f;
                triangleShape.Elasticity = 0.4f;
                triangle.AddShape(triangleShape);
                space.AddBody(triangle);
                _obstacles.Add(triangle);
            }
        }

        public void RunFrame()
        {
            if (_space == null || _characterBody == null) return;

            // Perform raycast from character center
            var mainRay = new Ray(_characterBody.Position, _raycastDirection, 8.0f);
            var hit = _space.Raycast(mainRay, _characterBody);

            // Register the main raycast for visualization
            Program.RegisterRaycast(mainRay, hit);

            // Also perform multiple raycasts in a fan pattern
            for (int i = -2; i <= 2; i++)
            {
                if (i == 0) continue; // Skip center ray as we already did it

                float angle = MathF.Atan2(_raycastDirection.Y, _raycastDirection.X) + i * MathF.PI / 8;
                var fanDirection = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
                var fanRay = new Ray(_characterBody.Position, fanDirection, 6.0f);
                var fanHit = _space.Raycast(fanRay, _characterBody);

                // Register each fan raycast for visualization
                Program.RegisterRaycast(fanRay, fanHit);
            }
        }

        public void KeyDown(char key)
        {
            if (_characterBody == null) return;

            const float moveForce = 800f;
            const float jumpForce = 1500f;

            switch (key)
            {
                case 'a': // Move left
                    _characterBody.ApplyForceToCenter(new Vector2(-moveForce, 0));
                    _raycastDirection = new Vector2(-1, 0);
                    break;

                case 'd': // Move right
                    _characterBody.ApplyForceToCenter(new Vector2(moveForce, 0));
                    _raycastDirection = new Vector2(1, 0);
                    break;

                case 'w': // Jump
                    if (IsOnGround())
                    {
                        _characterBody.ApplyForceToCenter(new Vector2(0, jumpForce));
                    }
                    _raycastDirection = new Vector2(0, 1);
                    break;

                case 's': // Look down
                    _raycastDirection = new Vector2(0, -1);
                    break;

                case ' ': // Shoot raycast forward and apply impulse to hit object
                    ShootRaycast();
                    break;

                case 'q': // Rotate raycast direction counter-clockwise
                    RotateRaycastDirection(-MathF.PI / 8);
                    break;

                case 'e': // Rotate raycast direction clockwise
                    RotateRaycastDirection(MathF.PI / 8);
                    break;
            }
        }

        private bool IsOnGround()
        {
            if (_characterBody == null || _space == null) return false;

            // Raycast downward to check if character is on ground
            var groundRay = new Ray(_characterBody.Position, new Vector2(0, -1), 1.0f);
            var hit = _space.Raycast(groundRay, _characterBody);

            // Register the ground check raycast for visualization
            Program.RegisterRaycast(groundRay, hit);

            return hit.Hit;
        }

        private void ShootRaycast()
        {
            if (_space == null || _characterBody == null) return;

            // Create a powerful raycast that can push objects
            var shootRay = new Ray(_characterBody.Position, _raycastDirection, 12.0f);
            var hit = _space.Raycast(shootRay, _characterBody);

            // Register the shoot raycast for visualization
            Program.RegisterRaycast(shootRay, hit);

            if (hit.Hit && hit.Body.Type == Body.BodyType.Dynamic)
            {
                // Apply impulse to the hit object
                var impulse = _raycastDirection * 10f;
                hit.Body.ApplyLinearImpulse(impulse, hit.Point);
            }
        }

        private void RotateRaycastDirection(float angle)
        {
            float currentAngle = MathF.Atan2(_raycastDirection.Y, _raycastDirection.X);
            float newAngle = currentAngle + angle;
            _raycastDirection = new Vector2(MathF.Cos(newAngle), MathF.Sin(newAngle));
        }
    }
}