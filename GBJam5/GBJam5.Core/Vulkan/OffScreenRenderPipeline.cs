using GlmSharp;
using SharpVk;
using System.Linq;

namespace GBJam5.Vulkan
{
    internal class OffScreenRenderPipeline
    {
        private const uint gbTextureWidth = 160;
        private const uint gbTextureHeight = 144;

        private RenderPass renderPass;
        private DescriptorSetLayout descriptorSetLayout;
        private ShaderModule vertexShader;
        private ShaderModule fragmentShader;
        private PipelineLayout pipelineLayout;
        private Pipeline pipeline;
        private Image offScreenImage;
        private DeviceMemory offScreenImageMemory;
        private ImageView offScreenImageView;
        private Framebuffer offScreenFrameBuffer;
        private Buffer vertexBuffer;
        private DeviceMemory vertexBufferMemory;
        private Buffer instanceBuffer;
        private DeviceMemory instanceBufferMemory;
        private Buffer drawCommandBuffer;
        private DeviceMemory drawCommandBufferMemory;
        private Buffer indexBuffer;
        private DeviceMemory indexBufferMemory;
        private Buffer uniformBuffer;
        private DeviceMemory uniformBufferMemory;
        private DescriptorSet descriptorSet;
        private readonly vec2[] instanceData;
        private uint instanceCount;
        private int[] instanceReferences;
        private int firstFreeReference;
        private IVulkanInstance instance;

        public OffScreenRenderPipeline(IVulkanInstance instance,
                                        CommandPool commandPool,
                                        ClearColorValue clearColour,
                                        DescriptorPool descriptorPool,
                                        ImageView textureImageView,
                                        Sampler textureSampler)
        {
            Extent2D imageExtent = new Extent2D
            {
                Width = gbTextureWidth,
                Height = gbTextureHeight
            };

            this.instance = instance;

            this.renderPass = instance.Device.CreateRenderPass(new RenderPassCreateInfo
            {
                Attachments = new[]
                {
                    new AttachmentDescription
                    {
                        Format = Format.R8G8B8A8UNorm,
                        Samples = SampleCountFlags.SampleCount1,
                        LoadOp = AttachmentLoadOp.DontCare,
                        StoreOp = AttachmentStoreOp.Store,
                        StencilLoadOp = AttachmentLoadOp.DontCare,
                        StencilStoreOp = AttachmentStoreOp.DontCare,
                        InitialLayout = ImageLayout.Undefined,
                        FinalLayout = ImageLayout.ColorAttachmentOptimal
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
            var vertShaderData = instance.LoadShaderData(@".\Shaders\offScreenVert.spv", out codeSize);

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
                        VertexBindingDescriptions = new [] { bindingDescription,
                                                                new VertexInputBindingDescription
                                                                {
                                                                    Binding = 1,
                                                                    InputRate = VertexInputRate.Instance,
                                                                    Stride = MemUtil.SizeOf<vec2>()
                                                                } },
                        VertexAttributeDescriptions = attributeDescriptions
                                                        .Concat(new[]
                                                        {
                                                            new VertexInputAttributeDescription
                                                            {
                                                                Binding = 1,
                                                                Location = 2,
                                                                Format = Format.R32G32SFloat,
                                                                Offset = 0
                                                            }
                                                        }).ToArray()
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
                                Width = gbTextureWidth,
                                Height = gbTextureHeight,
                                MaxDepth = 1,
                                MinDepth = 0
                            }
                        },
                        Scissors = new[]
                        {
                            new Rect2D
                            {
                                Offset = new Offset2D(),
                                Extent= new Extent2D
                                {
                                    Width = gbTextureWidth,
                                    Height = gbTextureHeight
                                }
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

            instance.CreateImage(gbTextureWidth, gbTextureHeight, Format.R8G8B8A8UNorm, ImageTiling.Optimal, ImageUsageFlags.ColorAttachment | ImageUsageFlags.Sampled, MemoryPropertyFlags.DeviceLocal, out this.offScreenImage, out this.offScreenImageMemory);

            this.offScreenImageView = instance.CreateImageView(this.offScreenImage, Format.R8G8B8A8UNorm);

            this.offScreenFrameBuffer = instance.Device.CreateFramebuffer(new FramebufferCreateInfo
            {
                RenderPass = renderPass,
                Attachments = new[] { this.offScreenImageView },
                Layers = 1,
                Height = gbTextureHeight,
                Width = gbTextureWidth
            });

            instance.CreateBuffer(MemUtil.SizeOf<Vertex>() * (uint)QuadData.Vertices.Length, BufferUsageFlags.TransferDestination | BufferUsageFlags.VertexBuffer, MemoryPropertyFlags.DeviceLocal, out this.vertexBuffer, out this.vertexBufferMemory);

            instance.UpdateBuffer(this.vertexBuffer, QuadData.Vertices);

            instance.CreateBuffer(MemUtil.SizeOf<short>() * (uint)QuadData.Indices.Length, BufferUsageFlags.TransferDestination | BufferUsageFlags.IndexBuffer, MemoryPropertyFlags.DeviceLocal, out this.indexBuffer, out this.indexBufferMemory);

            instance.UpdateBuffer(this.indexBuffer, QuadData.Indices);

            instance.CreateBuffer(MemUtil.SizeOf<vec2>() * 40, BufferUsageFlags.TransferDestination | BufferUsageFlags.VertexBuffer, MemoryPropertyFlags.DeviceLocal, out this.instanceBuffer, out this.instanceBufferMemory);

            instance.CreateBuffer(MemUtil.SizeOf<Services.VulkanDeviceService.UniformBufferObject>(), BufferUsageFlags.TransferDestination | BufferUsageFlags.UniformBuffer, MemoryPropertyFlags.DeviceLocal, out this.uniformBuffer, out this.uniformBufferMemory);

            instance.CreateBuffer(MemUtil.SizeOf<DrawIndexedIndirectCommand>(), BufferUsageFlags.TransferDestination | BufferUsageFlags.IndirectBuffer, MemoryPropertyFlags.DeviceLocal, out this.drawCommandBuffer, out this.drawCommandBufferMemory);

            this.instanceData = new vec2[40];
            this.instanceReferences = Enumerable.Range(1, this.instanceData.Length).ToArray();
            
            this.UpdateInstanceBuffers();

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
                            ImageView = textureImageView,
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
                CommandBufferCount = 1,
                CommandPool = commandPool,
                Level = CommandBufferLevel.Primary
            });

            var commandBuffer = this.CommandBuffers[0];

            commandBuffer.Begin(new CommandBufferBeginInfo
            {
                Flags = CommandBufferUsageFlags.SimultaneousUse
            });

            commandBuffer.BeginRenderPass(new RenderPassBeginInfo
            {
                RenderPass = this.renderPass,
                Framebuffer = this.offScreenFrameBuffer,
                RenderArea = new Rect2D
                {
                    Offset = new Offset2D(),
                    Extent = imageExtent
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
                            Extent = imageExtent
                        }
                    }
            });

            commandBuffer.BindPipeline(PipelineBindPoint.Graphics, this.pipeline);

            commandBuffer.BindVertexBuffers(0, new[] { this.vertexBuffer, this.instanceBuffer }, new DeviceSize[] { 0, 0 });

            commandBuffer.BindIndexBuffer(this.indexBuffer, 0, IndexType.UInt16);

            commandBuffer.BindDescriptorSets(PipelineBindPoint.Graphics, pipelineLayout, 0, new[] { descriptorSet }, null);

            commandBuffer.DrawIndexedIndirect(this.drawCommandBuffer, 0, 1, 0);

            commandBuffer.EndRenderPass();

            commandBuffer.End();
        }

        private int AddInstance(vec2 position)
        {
            int reference = this.firstFreeReference;

            if (reference >= this.instanceData.Length)
            {
                throw new System.Exception();
            }

            this.firstFreeReference = this.instanceReferences[reference];

            int dataIndex = (int)this.instanceCount;
            this.instanceCount++;

            this.instanceReferences[reference] = dataIndex;
            this.instanceData[dataIndex] = position;

            this.UpdateInstanceBuffers();

            return reference;
        }

        private void UpdateInstanceBuffers()
        {
            this.instance.UpdateBuffer(this.instanceBuffer, this.instanceData);

            this.instance.UpdateBuffer(this.drawCommandBuffer, new DrawIndexedIndirectCommand { FirstIndex = 0, FirstInstance = 0, IndexCount = (uint)QuadData.Indices.Length, InstanceCount = this.instanceCount, VertexOffset = 0 });
        }

        public CommandBuffer[] CommandBuffers
        {
            get;
            private set;
        }

        public Buffer UniformBuffer => this.uniformBuffer;

        public ImageView ImageView => this.offScreenImageView;
    }
}
