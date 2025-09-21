using Physics2D;

namespace DriftDemo
{
    public interface IDemo
    {
        string Name { get; }
        void Init(Space space);
        void RunFrame();
        void KeyDown(char key);
    }
}