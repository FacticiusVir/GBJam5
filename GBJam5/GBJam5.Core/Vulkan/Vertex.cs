using GlmSharp;
using SharpVk;
using System.Runtime.InteropServices;

namespace GBJam5.Vulkan
{
    public struct Vertex
    {
        public Vertex(vec2 position, vec2 uv)
        {
            this.Position = position;
            this.Uv = uv;
        }

        public vec2 Position;

        public vec2 Uv;

        public static VertexInputBindingDescription GetBindingDescription()
        {
            return new VertexInputBindingDescription()
            {
                Binding = 0,
                Stride = MemUtil.SizeOf<Vertex>(),
                InputRate = VertexInputRate.Vertex
            };
        }

        public static VertexInputAttributeDescription[] GetAttributeDescriptions()
        {
            return new VertexInputAttributeDescription[]
            {
                    new VertexInputAttributeDescription
                    {
                        Binding = 0,
                        Location = 0,
                        Format = Format.R32G32SFloat,
                        Offset = (uint)Marshal.OffsetOf<Vertex>("Position")
                    },
                    new VertexInputAttributeDescription
                    {
                        Binding = 0,
                        Location = 1,
                        Format = Format.R32G32SFloat,
                        Offset = (uint)Marshal.OffsetOf<Vertex>("Uv")
                    }
            };
        }
    }
}
