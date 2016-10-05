using SharpVk;
using System.Linq;

namespace GBJam5.Vulkan
{
    internal class SwapchainRenderPipeline
    {
        private RenderPass renderPass;
        private DescriptorSetLayout descriptorSetLayout;
        private ShaderModule vertexShader;
        private ShaderModule fragmentShader;
        private PipelineLayout pipelineLayout;
        private Pipeline pipeline;
        private ImageView[] swapchainImageViews;
        private Framebuffer[] swapchainFrameBuffers;
        private Buffer vertexBuffer;
        private DeviceMemory vertexBufferMemory;
        private Buffer indexBuffer;
        private DeviceMemory indexBufferMemory;
        private Buffer uniformBuffer;
        private DeviceMemory uniformBufferMemory;
        private DescriptorSet descriptorSet;

        public SwapchainRenderPipeline(IVulkanInstance instance,
                                        CommandPool commandPool,
                                        ClearColorValue clearColour,
                                        DescriptorPool descriptorPool,
                                        Swapchain swapchain,
                                        Extent2D swapchainExtent,
                                        Format swapchainFormat,
                                        ImageView offScreenImageView,
                                        Sampler textureSampler)
        {
            this.renderPass = instance.Device.CreateRenderPass(new RenderPassCreateInfo
            {
                Attachments = new[]
                {
                    new AttachmentDescription
                    {
                        Format = swapchainFormat,
                        Samples = SampleCountFlags.SampleCount1,
                        LoadOp = AttachmentLoadOp.DontCare,
                        StoreOp = AttachmentStoreOp.Store,
                        StencilLoadOp = AttachmentLoadOp.DontCare,
                        StencilStoreOp = AttachmentStoreOp.DontCare,
                        InitialLayout = ImageLayout.Undefined,
                        FinalLayout = ImageLayout.PresentSource
                    },
                },
                Subpasses = new[]
                {
                    new SubpassDescription
                    {
                        DepthStencilAttachment = new AttachmentReference
                        {
                            Attachment = Constants.AttachmentUnused
                        },
                        PipelineBindPoint = PipelineBindPoint.Graphics,
                        ColorAttachments = new []
                        {
                            new AttachmentReference
                            {
                                Attachment = 0,
                                Layout = ImageLayout.ColorAttachmentOptimal
                            }
                        }
                    }
                },
                Dependencies = new[]
                {
                    new SubpassDependency
                    {
                        SourceSubpass = Constants.SubpassExternal,
                        DestinationSubpass = 0,
                        SourceStageMask = PipelineStageFlags.BottomOfPipe,
                        SourceAccessMask = AccessFlags.MemoryRead,
                        DestinationStageMask = PipelineStageFlags.ColorAttachmentOutput,
                        DestinationAccessMask = AccessFlags.ColorAttachmentRead | AccessFlags.ColorAttachmentWrite
                    },
                    new SubpassDependency
                    {
                        SourceSubpass = 0,
                        DestinationSubpass = Constants.SubpassExternal,
                        SourceStageMask = PipelineStageFlags.ColorAttachmentOutput,
                        SourceAccessMask = AccessFlags.ColorAttachmentRead | AccessFlags.ColorAttachmentWrite,
                        DestinationStageMask = PipelineStageFlags.BottomOfPipe,
                        DestinationAccessMask = AccessFlags.MemoryRead
                    }
                }
            });

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

            int codeSize;
            var vertShaderData = instance.LoadShaderData(@".\Shaders\vert.spv", out codeSize);

            this.vertexShader = instance.Device.CreateShaderModule(new ShaderModuleCreateInfo
            {
                Code = vertShaderData,
                CodeSize = codeSize
            });

            var fragShaderData = instance.LoadShaderData(@".\Shaders\frag.spv", out codeSize);

            this.fragmentShader = instance.Device.CreateShaderModule(new ShaderModuleCreateInfo
            {
                Code = fragShaderData,
                CodeSize = codeSize
            });

            var bindingDescription = Vertex.GetBindingDescription();
            var attributeDescriptions = Vertex.GetAttributeDescriptions();

            this.pipelineLayout = instance.Device.CreatePipelineLayout(new PipelineLayoutCreateInfo()
            {
                SetLayouts = new[]
                {
                    this.descriptorSetLayout
                }
            });

            this.pipeline = instance.Device.CreateGraphicsPipelines(null, new[]
            {
                new GraphicsPipelineCreateInfo
                {
                    Layout = this.pipelineLayout,
                    RenderPass = this.renderPass,
                    Subpass = 0,
                    VertexInputState = new PipelineVertexInputStateCreateInfo()
                    {
                        VertexBindingDescriptions = new [] { bindingDescription },
                        VertexAttributeDescriptions = attributeDescriptions
                    },
                    InputAssemblyState = new PipelineInputAssemblyStateCreateInfo
                    {
                        PrimitiveRestartEnable = false,
                        Topology = PrimitiveTopology.TriangleList
                    },
                    ViewportState = new PipelineViewportStateCreateInfo
                    {
                        Viewports = new[]
                        {
                            new Viewport
                            {
                                X = 0f,
                                Y = 0f,
                                Width = swapchainExtent.Width,
                                Height = swapchainExtent.Height,
                                MaxDepth = 1,
                                MinDepth = 0
                            }
                        },
                        Scissors = new[]
                        {
                            new Rect2D
                            {
                                Offset = new Offset2D(),
                                Extent= swapchainExtent
                            }
                        }
                    },
                    RasterizationState = new PipelineRasterizationStateCreateInfo
                    {
                        DepthClampEnable = false,
                        RasterizerDiscardEnable = false,
                        PolygonMode = PolygonMode.Fill,
                        LineWidth = 1,
                        CullMode = CullModeFlags.None,
                        FrontFace = FrontFace.CounterClockwise,
                        DepthBiasEnable = false
                    },
                    MultisampleState = new PipelineMultisampleStateCreateInfo
                    {
                        SampleShadingEnable = false,
                        RasterizationSamples = SampleCountFlags.SampleCount1,
                        MinSampleShading = 1
                    },
                    ColorBlendState = new PipelineColorBlendStateCreateInfo
                    {
                        Attachments = new[]
                        {
                            new PipelineColorBlendAttachmentState
                            {
                                ColorWriteMask = ColorComponentFlags.R
                                                    | ColorComponentFlags.G
                                                    | ColorComponentFlags.B
                                                    | ColorComponentFlags.A,
                                BlendEnable = false,
                                SourceColorBlendFactor = BlendFactor.One,
                                DestinationColorBlendFactor = BlendFactor.Zero,
                                ColorBlendOp = BlendOp.Add,
                                SourceAlphaBlendFactor = BlendFactor.One,
                                DestinationAlphaBlendFactor = BlendFactor.Zero,
                                AlphaBlendOp = BlendOp.Add
                            }
                        },
                        LogicOpEnable = false,
                        LogicOp = LogicOp.Copy,
                        BlendConstants = new float[] {0,0,0,0}
                    },
                    Stages = new[]
                    {
                        new PipelineShaderStageCreateInfo
                        {
                            Stage = ShaderStageFlags.Vertex,
                            Module = this.vertexShader,
                            Name = "main"
                        },
                        new PipelineShaderStageCreateInfo
                        {
                            Stage = ShaderStageFlags.Fragment,
                            Module = this.fragmentShader,
                            Name = "main"
                        }
                    }
                }
            }).Single();

            var swapchainImages = swapchain.GetImages();

            this.swapchainImageViews = swapchainImages.Select(image => instance.CreateImageView(image, swapchainFormat)).ToArray();

            this.swapchainFrameBuffers = this.swapchainImageViews.Select(imageView => instance.Device.CreateFramebuffer(new FramebufferCreateInfo
            {
                RenderPass = renderPass,
                Attachments = new[] { imageView },
                Layers = 1,
                Height = swapchainExtent.Height,
                Width = swapchainExtent.Width
            })).ToArray();

            instance.CreateBuffer(MemUtil.SizeOf<Vertex>() * (uint)QuadData.Vertices.Length, BufferUsageFlags.TransferDestination | BufferUsageFlags.VertexBuffer, MemoryPropertyFlags.DeviceLocal, out this.vertexBuffer, out this.vertexBufferMemory);

            instance.UpdateBuffer(this.vertexBuffer, QuadData.Vertices);

            instance.CreateBuffer(MemUtil.SizeOf<short>() * (uint)QuadData.Indices.Length, BufferUsageFlags.TransferDestination | BufferUsageFlags.IndexBuffer, MemoryPropertyFlags.DeviceLocal, out this.indexBuffer, out this.indexBufferMemory);

            instance.UpdateBuffer(this.indexBuffer, QuadData.Indices);

            instance.CreateBuffer(MemUtil.SizeOf<Services.VulkanDeviceService.UniformBufferObject>(), BufferUsageFlags.TransferDestination | BufferUsageFlags.UniformBuffer, MemoryPropertyFlags.DeviceLocal, out this.uniformBuffer, out this.uniformBufferMemory);

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
                            ImageView = offScreenImageView,
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
                CommandBufferCount = (uint)this.swapchainFrameBuffers.Length,
                CommandPool = commandPool,
                Level = CommandBufferLevel.Primary
            });

            for (int bufferIndex = 0; bufferIndex < this.swapchainFrameBuffers.Length; bufferIndex++)
            {
                var commandBuffer = this.CommandBuffers[bufferIndex];
                var frameBuffer = this.swapchainFrameBuffers[bufferIndex];

                commandBuffer.Begin(new CommandBufferBeginInfo
                {
                    Flags = CommandBufferUsageFlags.SimultaneousUse
                });

                commandBuffer.BeginRenderPass(new RenderPassBeginInfo
                {
                    RenderPass = renderPass,
                    Framebuffer = frameBuffer,
                    RenderArea = new Rect2D
                    {
                        Offset = new Offset2D(),
                        Extent = swapchainExtent
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
                            Extent = swapchainExtent
                        }
                    }
                });

                commandBuffer.BindPipeline(PipelineBindPoint.Graphics, this.pipeline);

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
