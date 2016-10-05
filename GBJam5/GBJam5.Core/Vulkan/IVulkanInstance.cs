using SharpVk;

namespace GBJam5.Vulkan
{
    internal interface IVulkanInstance
    {
        Device Device { get; }

        void CreateBuffer(ulong size, BufferUsageFlags usage, MemoryPropertyFlags properties, out Buffer buffer, out DeviceMemory bufferMemory);

        void UpdateBuffer<T>(Buffer buffer, T data, int offset = 0) where T : struct;

        void UpdateBuffer<T>(Buffer buffer, T[] data, int offset = 0) where T : struct;

        void CreateImage(uint width, uint height, Format format, ImageTiling imageTiling, ImageUsageFlags usage, MemoryPropertyFlags properties, out Image image, out DeviceMemory imageMemory);

        ImageView CreateImageView(Image image, Format format);

        uint[] LoadShaderData(string filePath, out int codeSize);
    }
}
