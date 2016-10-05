using SharpVk;
using System.Linq;

namespace GBJam5.Vulkan
{
    internal class OffScreenRenderPipeline
    {
        private Buffer vertexBuffer;
        private DeviceMemory vertexBufferMemory;
        private Buffer indexBuffer;
        private DeviceMemory indexBufferMemory;
        private Buffer uniformBuffer;
        private DeviceMemory uniformBufferMemory;
        private DescriptorSetLayout descriptorSetLayout;
        private DescriptorSet descriptorSet;

        public OffScreenRenderPipeline(IVulkanInstance instance,
                                        CommandPool commandPool,
                                        Framebuffer[] frameBuffers,
                                        Extent2D frameExtent,
                                        ClearColorValue clearColour,
                                        Pipeline pipeline,
                                        PipelineLayout pipelineLayout,
                                        RenderPass renderPass,
                                        DescriptorPool descriptorPool,
                                        ImageView textureView,
                                        Sampler textureSampler)
        {
            instance.CreateBuffer(MemUtil.SizeOf<Vertex>() * (uint)QuadData.Vertices.Length, BufferUsageFlags.TransferDestination | BufferUsageFlags.VertexBuffer, MemoryPropertyFlags.DeviceLocal, out this.vertexBuffer, out this.vertexBufferMemory);

            instance.UpdateBuffer(this.vertexBuffer, QuadData.Vertices);

            instance.CreateBuffer(MemUtil.SizeOf<short>() * (uint)QuadData.Indices.Length, BufferUsageFlags.TransferDestination | BufferUsageFlags.IndexBuffer, MemoryPropertyFlags.DeviceLocal, out this.indexBuffer, out this.indexBufferMemory);

            instance.UpdateBuffer(this.indexBuffer, QuadData.Indices);

            instance.CreateBuffer(MemUtil.SizeOf<Services.VulkanDeviceService.UniformBufferObject>() * 40, BufferUsageFlags.TransferDestination | BufferUsageFlags.UniformBuffer, MemoryPropertyFlags.DeviceLocal, out this.uniformBuffer, out this.uniformBufferMemory);

            this.descriptorSetLayout = instance.Device.CreateDescriptorSetLayout(new DescriptorSetLayoutCreateInfo
            {
                Bindings = new[]
                {
                    new DescriptorSetLayoutBinding
                    {
                        Binding = 0,
                        DescriptorType = DescriptorType.UniformBuffer,
                        StageFlags = ShaderStageFlags.Vertex,
                        DescriptorCount = 1
                    },
                    new DescriptorSetLayoutBinding
                    {
                        Binding = 1,
                        DescriptorCount = 1,
                        DescriptorType = DescriptorType.CombinedImageSampler,
                        StageFlags = ShaderStageFlags.Fragment
                    }
                }
            });

            this.descriptorSet = instance.Device.AllocateDescriptorSets(new DescriptorSetAllocateInfo
            {
                DescriptorPool = descriptorPool,
                SetLayouts = new[]
                {
                    this.descriptorSetLayout
                }
            }).Single();

            instance.Device.UpdateDescriptorSets(new[]
            {
                new WriteDescriptorSet
                {
                    BufferInfo = new []
                    {
                        new DescriptorBufferInfo
                        {
                            Buffer = uniformBuffer,
                            Offset = 0,
                            Range = MemUtil.SizeOf<Services.VulkanDeviceService.UniformBufferObject>()
                        }
                    },
                    DestinationSet = descriptorSet,
                    DestinationBinding = 0,
                    DestinationArrayElement = 0,
                    DescriptorType = DescriptorType.UniformBuffer
                },
                new WriteDescriptorSet
                {
                    ImageInfo = new []
                    {
                        new DescriptorImageInfo
                        {
                            ImageView = textureView,
                            Sampler = textureSampler,
                            ImageLayout = ImageLayout.ShaderReadOnlyOptimal
                        }
                    },
                    DestinationSet = descriptorSet,
                    DestinationBinding = 1,
                    DestinationArrayElement = 0,
                    DescriptorType = DescriptorType.CombinedImageSampler
                }
            }, null);

            this.CommandBuffers = instance.Device.AllocateCommandBuffers(new CommandBufferAllocateInfo
            {
                CommandBufferCount = (uint)frameBuffers.Length,
                CommandPool = commandPool,
                Level = CommandBufferLevel.Primary
            });

            for(int bufferIndex = 0; bufferIndex<frameBuffers.Length;bufferIndex++)
            {
                var commandBuffer = this.CommandBuffers[bufferIndex];

                commandBuffer.Begin(new CommandBufferBeginInfo
                {
                    Flags = CommandBufferUsageFlags.SimultaneousUse
                });

                commandBuffer.BeginRenderPass(new RenderPassBeginInfo
                {
                    RenderPass = renderPass,
                    Framebuffer = frameBuffers[bufferIndex],
                    RenderArea = new Rect2D
                    {
                        Offset = new Offset2D(),
                        Extent = frameExtent
                    }
                }, SubpassContents.Inline);

                commandBuffer.ClearAttachments(new[]
                {
                    new ClearAttachment
                    {
                        AspectMask = ImageAspectFlags.Color,
                        ClearValue = clearColour,
                        ColorAttachment = 0
                    }
                },
                new[]
                {
                    new ClearRect
                    {
                        BaseArrayLayer = 0,
                        LayerCount = 1,
                        Rect = new Rect2D
                        {
                            Extent = frameExtent
                        }
                    }
                });

                commandBuffer.BindPipeline(PipelineBindPoint.Graphics, pipeline);

                commandBuffer.BindVertexBuffers(0, new[] { this.vertexBuffer }, new DeviceSize[] { 0 });

                commandBuffer.BindIndexBuffer(this.indexBuffer, 0, IndexType.UInt16);

                commandBuffer.BindDescriptorSets(PipelineBindPoint.Graphics, pipelineLayout, 0, new[] { descriptorSet }, null);

                commandBuffer.DrawIndexed((uint)QuadData.Indices.Length, 1, 0, 0, 0);

                commandBuffer.EndRenderPass();

                commandBuffer.End();
            }
        }

        public CommandBuffer[] CommandBuffers
        {
            get;
            private set;
        }

        public Buffer UniformBuffer => this.uniformBuffer;
    }
}
