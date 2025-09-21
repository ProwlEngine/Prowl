using Physics2D;
using SFML.Graphics;
using SFML.System;

namespace DriftDemo
{
    public class Renderer
    {
        private RenderWindow _window;
        private readonly float _pixelsPerMeter = 50f;
        private readonly Vector2f _center;

        public Renderer(RenderWindow window)
        {
            _window = window;
            _center = new Vector2f(window.Size.X / 2f, window.Size.Y / 2f);
        }

        private Vector2f WorldToScreen(Vec2 worldPos)
        {
            return new Vector2f(
                _center.X + worldPos.X * _pixelsPerMeter,
                _center.Y + _window.Size.Y * 0.5f - worldPos.Y * _pixelsPerMeter  // Flip Y axis and offset down
            );
        }

        public void DrawSpace(Space space)
        {
            foreach (var body in space.Bodies)
            {
                DrawBody(body);
            }

            // draw contacts
            foreach (var contactSolver in space.Contacts)
            {
                foreach (var contact in contactSolver.Contacts)
                {
                    var pos = WorldToScreen(contact.Position);
                    var nor = WorldToScreen(contact.NormalTowardTwo);
                    var depth = contact.Depth * _pixelsPerMeter;

                    var circle = new CircleShape(3)
                    {
                        Position = new Vector2f(pos.X - 3, pos.Y - 3),
                        FillColor = Color.Red
                    };

                    _window.Draw(circle);

                    var line = new Vertex[]
                    {
                        new Vertex(pos, Color.Yellow),
                        new Vertex(new Vector2f(pos.X + contact.NormalTowardTwo.X * depth, pos.Y - contact.NormalTowardTwo.Y * depth), Color.Yellow)
                    };
                    _window.Draw(line, PrimitiveType.Lines);
                }
            }

            // Draw Bounds
            foreach (var body in space.Bodies)
            {
                if (body == null) continue;
                var b = body.Bounds;
                var topLeft = WorldToScreen(new Vec2(b.Mins.X, b.Maxs.Y));
                var size = new Vector2f((b.Maxs.X - b.Mins.X) * _pixelsPerMeter, (b.Maxs.Y - b.Mins.Y) * _pixelsPerMeter);
                var rect = new RectangleShape(size)
                {
                    Position = topLeft,
                    FillColor = Color.Transparent,
                    OutlineColor = Color.Blue,
                    OutlineThickness = 1
                };
                _window.Draw(rect);
            }

            // Draw joints with just lines
            foreach (var joint in space.Joints)
            {
                if (joint == null) continue;
                var a = WorldToScreen(joint.GetWorldAnchor1());
                var b = WorldToScreen(joint.GetWorldAnchor2());
                var line = new Vertex[]
                {
                    new Vertex(a, Color.Magenta),
                    new Vertex(b, Color.Magenta)
                };
                _window.Draw(line, PrimitiveType.Lines);
            }
        }

        private void DrawBody(Body body)
        {
            var color = body.Type switch
            {
                Body.BodyType.Static => new Color(128, 128, 128),
                Body.BodyType.Dynamic => Color.White,
                Body.BodyType.Kinetic => Color.Green,
                _ => Color.Red
            };

            foreach (var shape in body.Shapes)
            {
                DrawShape(shape, color, true);
            }
        }

        private void DrawShape(Physics2D.Shape shape, Color color, bool isAwake)
        {
            switch (shape)
            {
                case ShapeCircle circle:
                    DrawCircle(circle, color, isAwake);
                    break;
                case ShapePoly poly:
                    DrawPoly(poly, color, isAwake);
                    break;
                case ShapeSegment segment:
                    DrawSegment(segment, color, isAwake);
                    break;
            }
        }

        private void DrawCircle(ShapeCircle circle, Color color, bool isAwake)
        {
            var center = WorldToScreen(circle.TransformedCenter);
            var radius = circle.Radius * _pixelsPerMeter;

            var shape = new CircleShape(radius)
            {
                Position = new Vector2f(center.X - radius, center.Y - radius),
                FillColor = isAwake ? Color.Transparent : new Color(255, 255, 255, 25),
                OutlineColor = color,
                OutlineThickness = 1
            };

            _window.Draw(shape);
        }

        private void DrawPoly(ShapePoly poly, Color color, bool isAwake)
        {
            if (poly.TransformedVerts.Count < 3) return;

            var shape = new ConvexShape((uint)poly.TransformedVerts.Count);
            for (int i = 0; i < poly.TransformedVerts.Count; i++)
            {
                var screenPos = WorldToScreen(poly.TransformedVerts[i]);
                shape.SetPoint((uint)i, screenPos);
            }

            shape.FillColor = isAwake ? Color.Transparent : new Color(255, 255, 255, 25);
            shape.OutlineColor = color;
            shape.OutlineThickness = 1;

            _window.Draw(shape);

            // Draw Plane Normals
            for (int i = 0; i < poly.TransformedVerts.Count; i++)
            {
                var a = poly.TransformedVerts[i];
                var b = poly.TransformedVerts[(i + 1) % poly.TransformedVerts.Count];
                var mid = (a + b) * 0.5f;
                var normalEnd = mid + poly.TransformedPlanes[i].Normal * 0.25f; // Scale normal for visibility
                DrawLine(mid, normalEnd, Color.Red);
            }
        }

        private void DrawLine(Vec2 start, Vec2 end, Color color)
        {
            var a = WorldToScreen(start);
            var b = WorldToScreen(end);
            var line = new Vertex[]
            {
                new Vertex(a, color),
                new Vertex(b, color)
            };
            _window.Draw(line, PrimitiveType.Lines);
        }

        private void DrawSegment(ShapeSegment segment, Color color, bool isAwake)
        {
            var a = WorldToScreen(segment.TransformedA);
            var b = WorldToScreen(segment.TransformedB);

            var line = new Vertex[]
            {
                new Vertex(a, color),
                new Vertex(b, color)
            };

            _window.Draw(line, PrimitiveType.Lines);

            // Draw circles at endpoints to show radius
            if (segment.Radius > 0)
            {
                var radius = segment.Radius * _pixelsPerMeter;
                var circleA = new CircleShape(radius)
                {
                    Position = new Vector2f(a.X - radius, a.Y - radius),
                    FillColor = isAwake ? Color.Transparent : new Color(255, 255, 255, 25),
                    OutlineColor = color,
                    OutlineThickness = 1
                };
                var circleB = new CircleShape(radius)
                {
                    Position = new Vector2f(b.X - radius, b.Y - radius),
                    FillColor = isAwake ? Color.Transparent : new Color(255, 255, 255, 25),
                    OutlineColor = color,
                    OutlineThickness = 1
                };
                _window.Draw(circleA);
                _window.Draw(circleB);
            }
        }
    }
}