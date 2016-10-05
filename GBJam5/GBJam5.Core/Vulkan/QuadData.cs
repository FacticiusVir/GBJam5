using GlmSharp;

namespace GBJam5.Vulkan
{
    public static class QuadData
    {

        public static readonly Vertex[] Vertices =
        {
            new Vertex(new vec2(0, 0), new vec2(0, 0)),
            new Vertex(new vec2(1, 0), new vec2(1, 0)),
            new Vertex(new vec2(1, 1), new vec2(1, 1)),
            new Vertex(new vec2(0, 1), new vec2(0, 1))
        };

        public static readonly ushort[] Indices = { 0, 1, 2, 2, 3, 0 };
    }
}
