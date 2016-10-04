using GlmSharp;
using SharpVk;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;

using Buffer = SharpVk.Buffer;
using Image = SharpVk.Image;
using Size = SharpVk.Size;

namespace GBJam5.Services
{
    public class VulkanDeviceService
        : GameService, IGraphicsDeviceService, IUpdatable
    {
        // A managed reference is held to prevent the delegate from being
        // garbage-collected while still in use by the unmanaged API.
        private readonly SharpVk.Interop.DebugReportCallbackDelegate debugReportDelegate;

        private const uint gbTextureWidth = 160;
        private const uint gbTextureHeight = 144;

        [DllImport("kernel32.dll", EntryPoint = "CopyMemory", SetLastError = false)]
        private static extern void CopyMemory(IntPtr dest, IntPtr src, uint count);

        private readonly Vertex[] quadVertices =
        {
            new Vertex(new vec2(0, 0), new vec2(0, 0)),
            new Vertex(new vec2(1, 0), new vec2(1, 0)),
            new Vertex(new vec2(1, 1), new vec2(1, 1)),
            new Vertex(new vec2(0, 1), new vec2(0, 1))
        };

        private readonly ushort[] quadIndices = { 0, 1, 2, 2, 3, 0 };

        private ILoggingService logging;
        private IUpdateLoopService updateLoop;
        private IWindowHostService windowHost;

        private Instance instance;
        private DebugReportCallback reportCallback;
        private Surface surface;
        private PhysicalDevice physicalDevice;
        private Device device;
        private Queue graphicsQueue;
        private Queue presentQueue;
        private Queue transferQueue;
        private Swapchain swapChain;
        private Image[] swapChainImages;
        private ImageView[] swapChainImageViews;
        private RenderPass renderPass;
        private RenderPass offScreenRenderPass;
        private DescriptorSetLayout descriptorSetLayout;
        private PipelineLayout pipelineLayout;
        private Pipeline offScreenPipeline;
        private Pipeline pipeline;
        private Framebuffer offScreenFrameBuffer;
        private Framebuffer[] frameBuffers;
        private CommandPool transientCommandPool;
        private CommandPool commandPool;
        private Buffer vertexBuffer;
        private DeviceMemory vertexBufferMemory;
        private Buffer indexBuffer;
        private DeviceMemory indexBufferMemory;
        private Buffer uniformStagingBuffer;
        private DeviceMemory uniformStagingBufferMemory;
        private Buffer uniformBuffer;
        private DeviceMemory uniformBufferMemory;
        private Image stagingImage;
        private DeviceMemory stagingImageMemory;
        private Image textureImage;
        private DeviceMemory textureImageMemory;
        private ImageView textureImageView;
        private Image offScreenImage;
        private DeviceMemory offScreenImageMemory;
        private ImageView offScreenImageView;
        private Sampler textureSampler;
        private DescriptorPool descriptorPool;
        private DescriptorSet offScreenDescriptorSet;
        private DescriptorSet descriptorSet;
        private CommandBuffer offScreenCommandBuffer;
        private CommandBuffer[] commandBuffers;

        private Semaphore screenRenderSemaphore;
        private Semaphore imageAvailableSemaphore;
        private Semaphore renderFinishedSemaphore;

        private Format swapChainFormat;
        private Extent2D swapChainExtent;

        public VulkanDeviceService()
        {
            this.debugReportDelegate = this.DebugReport;
        }

        public int PixelScale
        {
            get;
            set;
        } = 4;

        public override void Initialise(Game game)
        {
            this.logging = game.Services.GetService<ILoggingService>();
            this.updateLoop = game.Services.GetService<IUpdateLoopService>();
            this.windowHost = game.Services.GetService<IWindowHostService>();
        }

        public override void Start()
        {
            this.CreateInstance();
            this.CreateSurface();
            this.PickPhysicalDevice();
            this.CreateLogicalDevice();
            this.CreateSwapChain();
            this.CreateImageViews();
            this.CreateRenderPass();
            this.CreateOffScreenRenderPass();
            this.CreateDescriptorSetLayout();
            this.CreateGraphicsPipeline();
            this.CreateFrameBuffers();
            this.CreateCommandPools();
            this.CreateTextureImage();
            this.CreateTextureImageView();
            this.CreateOffScreenImage();
            this.CreateOffScreenImageView();
            this.CreateTextureSampler();
            this.CreateOffScreenFrameBuffer();
            this.CreateVertexBuffers();
            this.CreateIndexBuffer();
            this.CreateUniformBuffer();
            this.CreateDescriptorPool();
            this.CreateDescriptorSet();
            this.CreateCommandBuffers();
            this.CreateSemaphores();

            this.updateLoop.Register(this, UpdateStage.Render);
        }

        public void Update()
        {
            if (this.windowHost.IsResized)
            {
                this.RecreateSwapChain();
            }

            uint nextImage = this.swapChain.AcquireNextImage(uint.MaxValue, this.imageAvailableSemaphore, null);

            this.UpdateUbo(new UniformBufferObject
            {
                World = mat4.Translate(32, 32, 0) * mat4.Scale(32, 32, 1),
                View = mat4.Identity,
                Projection = mat4.Translate(-1, -1, 0)
                                * mat4.Scale(2)
                                * mat4.Scale(1 / (float)gbTextureWidth, 1 / (float)gbTextureHeight, 1)
            });

            this.graphicsQueue.Submit(new SubmitInfo[]
            {
                new SubmitInfo
                {
                    CommandBuffers = new [] { this.offScreenCommandBuffer },
                    SignalSemaphores = new [] { this.screenRenderSemaphore },
                    WaitDestinationStageMask = new [] { PipelineStageFlags.ColorAttachmentOutput },
                    WaitSemaphores = new [] { this.imageAvailableSemaphore }
                }
            }, null);
            
            this.UpdateUbo(new UniformBufferObject
            {
                World = mat4.Scale(160, 144, 1),
                View = mat4.Identity,
                Projection = mat4.Scale(this.PixelScale)
                                * mat4.Translate(-1, -1, 0)
                                * mat4.Scale(2)
                                * mat4.Scale(1 / (float)this.swapChainExtent.Width, 1 / (float)this.swapChainExtent.Height, 1)
                                * mat4.Translate((this.swapChainExtent.Width - gbTextureWidth) / 2f, (this.swapChainExtent.Height - gbTextureHeight) / 2f, 0)
            });

            this.graphicsQueue.Submit(new SubmitInfo[]
            {
                new SubmitInfo
                {
                    CommandBuffers = new [] { this.commandBuffers[nextImage] },
                    SignalSemaphores = new [] { this.renderFinishedSemaphore },
                    WaitDestinationStageMask = new [] { PipelineStageFlags.ColorAttachmentOutput },
                    WaitSemaphores = new [] { this.screenRenderSemaphore }
                }
            }, null);

            this.presentQueue.Present(new PresentInfo
            {
                ImageIndices = new uint[] { nextImage },
                Results = new Result[1],
                WaitSemaphores = new[] { this.renderFinishedSemaphore },
                Swapchains = new[] { this.swapChain }
            });
        }

        private void UpdateUbo(UniformBufferObject ubo)
        {
            uint uboSize = MemUtil.SizeOf<UniformBufferObject>();

            IntPtr memoryBuffer = IntPtr.Zero;
            this.uniformStagingBufferMemory.MapMemory(0, uboSize, MemoryMapFlags.None, ref memoryBuffer);

            MemUtil.WriteToPtr(memoryBuffer, ubo);

            this.uniformStagingBufferMemory.UnmapMemory();

            this.CopyBuffer(this.uniformStagingBuffer, this.uniformBuffer, uboSize);
        }

        public override void Stop()
        {
            this.updateLoop.Deregister(this);

            this.surface.Destroy();
            this.surface = null;

            this.reportCallback.Destroy();
            this.reportCallback = null;

            this.instance.Destroy();
            this.instance = null;
        }

        private void RecreateSwapChain()
        {
            this.device.WaitIdle();

            foreach (var frameBuffer in this.frameBuffers)
            {
                frameBuffer.Dispose();
            }
            this.frameBuffers = null;

            this.pipeline.Dispose();
            this.pipeline = null;

            this.pipelineLayout.Dispose();
            this.pipelineLayout = null;

            foreach (var imageView in this.swapChainImageViews)
            {
                imageView.Dispose();
            }
            this.swapChainImageViews = null;

            this.renderPass.Dispose();
            this.renderPass = null;

            this.swapChain.Dispose();
            this.swapChain = null;

            this.CreateSwapChain();
            this.CreateImageViews();
            this.CreateRenderPass();
            this.CreateGraphicsPipeline();
            this.CreateFrameBuffers();
            this.CreateCommandBuffers();
        }

        private void CreateInstance()
        {
            var enabledLayers = new List<string>();

            if (Instance.EnumerateLayerProperties().Any(x => x.LayerName == "VK_LAYER_LUNARG_standard_validation"))
            {
                enabledLayers.Add("VK_LAYER_LUNARG_standard_validation");
            }

            this.instance = Instance.Create(new InstanceCreateInfo
            {
                ApplicationInfo = new ApplicationInfo
                {
                    ApplicationName = "SharpVk Test Harness",
                    ApplicationVersion = Constants.SharpVkVersion,
                    EngineName = "SharpVk",
                    EngineVersion = Constants.SharpVkVersion,
                    ApiVersion = Constants.ApiVersion10
                },
                EnabledExtensionNames = new[]
                {
                    KhrSurface.ExtensionName,
                    KhrWin32Surface.ExtensionName,
                    ExtDebugReport.ExtensionName
                },
                EnabledLayerNames = enabledLayers.ToArray()
            }, null);

            this.reportCallback = this.instance.CreateDebugReportCallback(new DebugReportCallbackCreateInfo
            {
                Flags = DebugReportFlags.Error | DebugReportFlags.Warning,
                PfnCallback = this.debugReportDelegate
            });
        }

        private void CreateSurface()
        {
            this.surface = this.instance.CreateWin32Surface(new Win32SurfaceCreateInfo
            {
                Hwnd = this.windowHost.WindowHandle
            });
        }

        private void PickPhysicalDevice()
        {
            var availableDevices = this.instance.EnumeratePhysicalDevices();

            this.physicalDevice = availableDevices.First(IsSuitableDevice);
        }

        private void CreateLogicalDevice()
        {
            QueueFamilyIndices queueFamilies = FindQueueFamilies(this.physicalDevice);

            this.device = physicalDevice.CreateDevice(new DeviceCreateInfo
            {
                QueueCreateInfos = queueFamilies.Indices
                                                .Select(index => new DeviceQueueCreateInfo
                                                {
                                                    QueueFamilyIndex = index,
                                                    QueuePriorities = new[] { 1f }
                                                }).ToArray(),
                EnabledExtensionNames = new[] { KhrSwapchain.ExtensionName },
                EnabledLayerNames = null
            });

            this.graphicsQueue = this.device.GetQueue(queueFamilies.GraphicsFamily.Value, 0);
            this.presentQueue = this.device.GetQueue(queueFamilies.PresentFamily.Value, 0);
            this.transferQueue = this.device.GetQueue(queueFamilies.TransferFamily.Value, 0);
        }

        private void CreateSwapChain()
        {
            SwapChainSupportDetails swapChainSupport = this.QuerySwapChainSupport(this.physicalDevice);

            uint imageCount = swapChainSupport.Capabilities.MinImageCount + 1;
            if (swapChainSupport.Capabilities.MaxImageCount > 0 && imageCount > swapChainSupport.Capabilities.MaxImageCount)
            {
                imageCount = swapChainSupport.Capabilities.MaxImageCount;
            }

            SurfaceFormat surfaceFormat = this.ChooseSwapSurfaceFormat(swapChainSupport.Formats);

            QueueFamilyIndices queueFamilies = this.FindQueueFamilies(this.physicalDevice);

            var indices = queueFamilies.Indices.ToArray();

            Extent2D extent = this.ChooseSwapExtent(swapChainSupport.Capabilities);

            this.swapChain = device.CreateSwapchain(new SwapchainCreateInfo
            {
                Surface = surface,
                Flags = SwapchainCreateFlags.None,
                PresentMode = this.ChooseSwapPresentMode(swapChainSupport.PresentModes),
                MinImageCount = imageCount,
                ImageExtent = extent,
                ImageUsage = ImageUsageFlags.ColorAttachment,
                PreTransform = swapChainSupport.Capabilities.CurrentTransform,
                ImageArrayLayers = 1,
                ImageSharingMode = indices.Length == 1
                                    ? SharingMode.Exclusive
                                    : SharingMode.Concurrent,
                QueueFamilyIndices = indices,
                ImageFormat = surfaceFormat.Format,
                ImageColorSpace = surfaceFormat.ColorSpace,
                Clipped = true,
                CompositeAlpha = CompositeAlphaFlags.Opaque,
                OldSwapchain = this.swapChain
            });

            this.swapChainImages = this.swapChain.GetImages();
            this.swapChainFormat = surfaceFormat.Format;
            this.swapChainExtent = extent;
        }

        private void CreateImageViews()
        {
            this.swapChainImageViews = this.swapChainImages.Select(image => this.CreateImageView(image, this.swapChainFormat)).ToArray();
        }

        private void CreateRenderPass()
        {
            this.renderPass = device.CreateRenderPass(new RenderPassCreateInfo
            {
                Attachments = new[]
                       {
                        new AttachmentDescription
                        {
                            Format = this.swapChainFormat,
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
        }

        private void CreateOffScreenRenderPass()
        {
            this.offScreenRenderPass = device.CreateRenderPass(new RenderPassCreateInfo
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
        }

        private void CreateDescriptorSetLayout()
        {
            this.descriptorSetLayout = device.CreateDescriptorSetLayout(new DescriptorSetLayoutCreateInfo
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
        }

        private void CreateGraphicsPipeline()
        {
            int codeSize;
            var vertShaderData = LoadShaderData(@".\Shaders\vert.spv", out codeSize);

            var vertShader = device.CreateShaderModule(new ShaderModuleCreateInfo
            {
                Code = vertShaderData,
                CodeSize = codeSize
            });

            var fragShaderData = LoadShaderData(@".\Shaders\frag.spv", out codeSize);

            var fragShader = device.CreateShaderModule(new ShaderModuleCreateInfo
            {
                Code = fragShaderData,
                CodeSize = codeSize
            });

            var bindingDescription = Vertex.GetBindingDescription();
            var attributeDescriptions = Vertex.GetAttributeDescriptions();

            this.pipelineLayout = device.CreatePipelineLayout(new PipelineLayoutCreateInfo()
            {
                SetLayouts = new[]
                {
                    this.descriptorSetLayout
                }
            });

            var pipelines = device.CreateGraphicsPipelines(null, new[]
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
                                Width = this.swapChainExtent.Width,
                                Height = this.swapChainExtent.Height,
                                MaxDepth = 1,
                                MinDepth = 0
                            }
                        },
                        Scissors = new[]
                        {
                            new Rect2D
                            {
                                Offset = new Offset2D(),
                                Extent= this.swapChainExtent
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
                            Module = vertShader,
                            Name = "main"
                        },
                        new PipelineShaderStageCreateInfo
                        {
                            Stage = ShaderStageFlags.Fragment,
                            Module = fragShader,
                            Name = "main"
                        }
                    }
                },
                new GraphicsPipelineCreateInfo
                {
                    Layout = this.pipelineLayout,
                    RenderPass = this.offScreenRenderPass,
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
                            Module = vertShader,
                            Name = "main"
                        },
                        new PipelineShaderStageCreateInfo
                        {
                            Stage = ShaderStageFlags.Fragment,
                            Module = fragShader,
                            Name = "main"
                        }
                    }
                }
            });

            this.pipeline = pipelines[0];
            this.offScreenPipeline = pipelines[1];
        }

        private void CreateFrameBuffers()
        {
            this.frameBuffers = this.swapChainImageViews.Select(imageView => device.CreateFramebuffer(new FramebufferCreateInfo
            {
                RenderPass = renderPass,
                Attachments = new[] { imageView },
                Layers = 1,
                Height = this.swapChainExtent.Height,
                Width = this.swapChainExtent.Width
            })).ToArray();
        }

        private void CreateOffScreenFrameBuffer()
        {
            this.offScreenFrameBuffer = device.CreateFramebuffer(new FramebufferCreateInfo
            {
                RenderPass = this.offScreenRenderPass,
                Attachments = new[] { this.offScreenImageView },
                Layers = 1,
                Height = gbTextureHeight,
                Width = gbTextureWidth
            });
        }

        private void CreateCommandPools()
        {
            QueueFamilyIndices queueFamilies = FindQueueFamilies(this.physicalDevice);

            this.transientCommandPool = device.CreateCommandPool(new CommandPoolCreateInfo
            {
                Flags = CommandPoolCreateFlags.Transient,
                QueueFamilyIndex = queueFamilies.TransferFamily.Value
            });

            this.commandPool = device.CreateCommandPool(new CommandPoolCreateInfo
            {
                QueueFamilyIndex = queueFamilies.GraphicsFamily.Value
            });
        }

        private void CreateTextureImage()
        {
            var bitmap = new Bitmap(@".\Textures\Texture.png");
            uint textureWidth = (uint)bitmap.Width;
            uint textureHeight = (uint)bitmap.Height;
            DeviceSize imageSize = textureWidth * textureHeight * 4;

            byte[] rgbaBytes = new byte[imageSize];

            int i = 0;

            for (var y = 0; y < textureHeight; y++)
            {
                for (var x = 0; x < textureWidth; x++)
                {
                    Color pix = bitmap.GetPixel(x, y);

                    rgbaBytes[i++] = pix.R;
                    rgbaBytes[i++] = pix.G;
                    rgbaBytes[i++] = pix.B;
                    rgbaBytes[i++] = pix.A;
                }
            }

            bitmap.Dispose();

            this.CreateImage(textureWidth, textureHeight, Format.R8G8B8A8UNorm, ImageTiling.Linear, ImageUsageFlags.TransferSource, MemoryPropertyFlags.HostVisible | MemoryPropertyFlags.HostCoherent, out this.stagingImage, out this.stagingImageMemory);

            IntPtr memoryBuffer = IntPtr.Zero;
            this.stagingImageMemory.MapMemory(0, imageSize, MemoryMapFlags.None, ref memoryBuffer);

            MemUtil.WriteToPtr(memoryBuffer, rgbaBytes, 0, rgbaBytes.Length);

            this.stagingImageMemory.UnmapMemory();

            this.CreateImage(gbTextureWidth, gbTextureHeight, Format.R8G8B8A8UNorm, ImageTiling.Optimal, ImageUsageFlags.TransferDestination | ImageUsageFlags.Sampled, MemoryPropertyFlags.DeviceLocal, out this.textureImage, out this.textureImageMemory);

            this.TransitionImageLayout(this.stagingImage, Format.R8G8B8A8UNorm, ImageLayout.Preinitialized, ImageLayout.TransferSourceOptimal);
            this.TransitionImageLayout(this.textureImage, Format.R8G8B8A8UNorm, ImageLayout.Preinitialized, ImageLayout.TransferDestinationOptimal);

            this.CopyImage(this.stagingImage, this.textureImage, textureWidth, textureHeight);
            this.TransitionImageLayout(this.textureImage, Format.R8G8B8A8UNorm, ImageLayout.TransferDestinationOptimal, ImageLayout.ShaderReadOnlyOptimal);
        }

        private void CreateTextureImageView()
        {
            this.textureImageView = this.CreateImageView(this.textureImage, Format.R8G8B8A8UNorm);
        }

        private void CreateOffScreenImage()
        {
            this.CreateImage(gbTextureWidth, gbTextureHeight, Format.R8G8B8A8UNorm, ImageTiling.Optimal, ImageUsageFlags.ColorAttachment | ImageUsageFlags.Sampled, MemoryPropertyFlags.DeviceLocal, out this.offScreenImage, out this.offScreenImageMemory);
        }

        private void CreateOffScreenImageView()
        {
            this.offScreenImageView = this.CreateImageView(this.offScreenImage, Format.R8G8B8A8UNorm);
        }

        private void CreateTextureSampler()
        {
            this.textureSampler = this.device.CreateSampler(new SamplerCreateInfo
            {
                MagFilter = Filter.Nearest,
                MinFilter = Filter.Nearest,
                AddressModeU = SamplerAddressMode.ClampToBorder,
                AddressModeV = SamplerAddressMode.ClampToBorder,
                AddressModeW = SamplerAddressMode.ClampToBorder,
                AnisotropyEnable = false,
                MaxAnisotropy = 0,
                BorderColor = BorderColor.FloatTransparentBlack,
                UnnormalizedCoordinates = false,
                CompareEnable = false,
                CompareOp = CompareOp.Always,
                MipmapMode = SamplerMipmapMode.Nearest,
                MipLodBias = 0f,
                MinLod = 0f,
                MaxLod = 0f
            });
        }

        private void CreateVertexBuffers()
        {
            uint bufferSize = MemUtil.SizeOf<Vertex>() * (uint)quadVertices.Length;
            Buffer stagingBuffer;
            DeviceMemory stagingBufferMemory;

            this.CreateBuffer(bufferSize, BufferUsageFlags.TransferSource, MemoryPropertyFlags.HostVisible | MemoryPropertyFlags.HostCoherent, out stagingBuffer, out stagingBufferMemory);

            IntPtr memoryBuffer = IntPtr.Zero;
            stagingBufferMemory.MapMemory(0, bufferSize, MemoryMapFlags.None, ref memoryBuffer);

            MemUtil.WriteToPtr(memoryBuffer, quadVertices, 0, quadVertices.Length);

            stagingBufferMemory.UnmapMemory();

            this.CreateBuffer(bufferSize, BufferUsageFlags.TransferDestination | BufferUsageFlags.VertexBuffer, MemoryPropertyFlags.DeviceLocal, out this.vertexBuffer, out this.vertexBufferMemory);

            this.CopyBuffer(stagingBuffer, this.vertexBuffer, bufferSize);

            stagingBuffer.Dispose();
            this.device.FreeMemory(stagingBufferMemory);
        }

        private void CreateIndexBuffer()
        {
            ulong bufferSize = MemUtil.SizeOf<ushort>() * (uint)this.quadIndices.Length;
            Buffer stagingBuffer;
            DeviceMemory stagingBufferMemory;

            this.CreateBuffer(bufferSize, BufferUsageFlags.TransferSource, MemoryPropertyFlags.HostVisible | MemoryPropertyFlags.HostCoherent, out stagingBuffer, out stagingBufferMemory);

            IntPtr memoryBuffer = IntPtr.Zero;
            stagingBufferMemory.MapMemory(0, bufferSize, MemoryMapFlags.None, ref memoryBuffer);

            MemUtil.WriteToPtr(memoryBuffer, quadIndices, 0, quadIndices.Length);

            stagingBufferMemory.UnmapMemory();

            this.CreateBuffer(bufferSize, BufferUsageFlags.TransferDestination | BufferUsageFlags.IndexBuffer, MemoryPropertyFlags.DeviceLocal, out this.indexBuffer, out this.indexBufferMemory);

            this.CopyBuffer(stagingBuffer, this.indexBuffer, bufferSize);

            stagingBuffer.Dispose();
            this.device.FreeMemory(stagingBufferMemory);
        }

        private void CreateUniformBuffer()
        {
            uint bufferSize = MemUtil.SizeOf<UniformBufferObject>();

            this.CreateBuffer(bufferSize, BufferUsageFlags.TransferSource, MemoryPropertyFlags.HostVisible | MemoryPropertyFlags.HostCoherent, out this.uniformStagingBuffer, out this.uniformStagingBufferMemory);
            this.CreateBuffer(bufferSize, BufferUsageFlags.TransferDestination | BufferUsageFlags.UniformBuffer, MemoryPropertyFlags.DeviceLocal, out this.uniformBuffer, out this.uniformBufferMemory);
        }

        private void CreateDescriptorPool()
        {
            this.descriptorPool = device.CreateDescriptorPool(new DescriptorPoolCreateInfo
            {
                PoolSizes = new[]
                {
                    new DescriptorPoolSize
                    {
                        DescriptorCount = 2,
                        Type = DescriptorType.UniformBuffer
                    },
                    new DescriptorPoolSize
                    {
                        DescriptorCount = 2,
                        Type = DescriptorType.CombinedImageSampler
                    }
                },
                MaxSets = 2
            });
        }

        private void CreateDescriptorSet()
        {
            var descriptorSets = this.device.AllocateDescriptorSets(new DescriptorSetAllocateInfo
            {
                DescriptorPool = this.descriptorPool,
                SetLayouts = new[]
                {
                    this.descriptorSetLayout,
                    this.descriptorSetLayout
                }
            });

            this.descriptorSet = descriptorSets[0];
            this.offScreenDescriptorSet = descriptorSets[1];

            this.device.UpdateDescriptorSets(new[]
            {
                new WriteDescriptorSet
                {
                    BufferInfo = new []
                    {
                        new DescriptorBufferInfo
                        {
                            Buffer = this.uniformBuffer,
                            Offset = 0,
                            Range = MemUtil.SizeOf<UniformBufferObject>()
                        }
                    },
                    DestinationSet = this.descriptorSet,
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
                            ImageView = this.offScreenImageView,
                            Sampler = this.textureSampler,
                            ImageLayout = ImageLayout.ShaderReadOnlyOptimal
                        }
                    },
                    DestinationSet = this.descriptorSet,
                    DestinationBinding = 1,
                    DestinationArrayElement = 0,
                    DescriptorType = DescriptorType.CombinedImageSampler
                }
            }, null);

            this.device.UpdateDescriptorSets(new[]
            {
                new WriteDescriptorSet
                {
                    BufferInfo = new []
                    {
                        new DescriptorBufferInfo
                        {
                            Buffer = this.uniformBuffer,
                            Offset = 0,
                            Range = MemUtil.SizeOf<UniformBufferObject>()
                        }
                    },
                    DestinationSet = this.offScreenDescriptorSet,
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
                            ImageView = this.textureImageView,
                            Sampler = this.textureSampler,
                            ImageLayout = ImageLayout.ShaderReadOnlyOptimal
                        }
                    },
                    DestinationSet = this.offScreenDescriptorSet,
                    DestinationBinding = 1,
                    DestinationArrayElement = 0,
                    DescriptorType = DescriptorType.CombinedImageSampler
                }
            }, null);
        }

        private void CreateCommandBuffers()
        {
            this.commandPool.Reset(CommandPoolResetFlags.ReleaseResources);

            this.offScreenCommandBuffer = device.AllocateCommandBuffers(new CommandBufferAllocateInfo
            {
                CommandBufferCount = 1,
                CommandPool = this.commandPool,
                Level = CommandBufferLevel.Primary
            }).Single();

            offScreenCommandBuffer.Begin(new CommandBufferBeginInfo
            {
                Flags = CommandBufferUsageFlags.SimultaneousUse
            });

            offScreenCommandBuffer.BeginRenderPass(new RenderPassBeginInfo
            {
                RenderPass = this.offScreenRenderPass,
                Framebuffer = this.offScreenFrameBuffer,
                RenderArea = new Rect2D
                {
                    Offset = new Offset2D(),
                    Extent = new Extent2D
                    {
                        Width = gbTextureWidth,
                        Height = gbTextureHeight
                    }
                },
                ClearValues = new ClearValue[]
                {
                        new ClearColorValue(0f, 0f, 0f, 1f)
                }
            }, SubpassContents.Inline);

            offScreenCommandBuffer.ClearAttachments(new[]
            {
                    new ClearAttachment
                    {
                        AspectMask = ImageAspectFlags.Color,
                        ClearValue = new ClearColorValue(0f, 0f, 1f, 1f),
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
                            Extent = new Extent2D
                            {
                                Width = gbTextureWidth,
                                Height = gbTextureHeight
                            }
                        }
                    }
            });

            offScreenCommandBuffer.BindPipeline(PipelineBindPoint.Graphics, this.offScreenPipeline);

            offScreenCommandBuffer.BindVertexBuffers(0, new[] { this.vertexBuffer }, new DeviceSize[] { 0 });

            offScreenCommandBuffer.BindIndexBuffer(this.indexBuffer, 0, IndexType.UInt16);

            offScreenCommandBuffer.BindDescriptorSets(PipelineBindPoint.Graphics, this.pipelineLayout, 0, new[] { this.offScreenDescriptorSet }, null);

            offScreenCommandBuffer.DrawIndexed((uint)this.quadIndices.Length, 1, 0, 0, 0);

            offScreenCommandBuffer.EndRenderPass();

            offScreenCommandBuffer.End();

            this.commandBuffers = device.AllocateCommandBuffers(new CommandBufferAllocateInfo
            {
                CommandBufferCount = (uint)this.frameBuffers.Length,
                CommandPool = this.commandPool,
                Level = CommandBufferLevel.Primary
            });

            for (int index = 0; index < this.frameBuffers.Length; index++)
            {
                var commandBuffer = this.commandBuffers[index];

                commandBuffer.Begin(new CommandBufferBeginInfo
                {
                    Flags = CommandBufferUsageFlags.SimultaneousUse
                });

                commandBuffer.BeginRenderPass(new RenderPassBeginInfo
                {
                    RenderPass = this.renderPass,
                    Framebuffer = this.frameBuffers[index],
                    RenderArea = new Rect2D
                    {
                        Offset = new Offset2D(),
                        Extent = this.swapChainExtent
                    },
                    ClearValues = new ClearValue[]
                    {
                        new ClearColorValue(0f, 0f, 0f, 1f)
                    }
                }, SubpassContents.Inline);

                commandBuffer.ClearAttachments(new[]
                {
                    new ClearAttachment
                    {
                        AspectMask = ImageAspectFlags.Color,
                        ClearValue = new ClearColorValue(0.25f, 0.5f, 0f, 1f),
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
                            Extent = this.swapChainExtent
                        }
                    }
                });

                commandBuffer.BindPipeline(PipelineBindPoint.Graphics, this.pipeline);

                commandBuffer.BindVertexBuffers(0, new[] { this.vertexBuffer }, new DeviceSize[] { 0 });

                commandBuffer.BindIndexBuffer(this.indexBuffer, 0, IndexType.UInt16);

                commandBuffer.BindDescriptorSets(PipelineBindPoint.Graphics, this.pipelineLayout, 0, new[] { this.descriptorSet }, null);

                commandBuffer.DrawIndexed((uint)this.quadIndices.Length, 1, 0, 0, 0);

                commandBuffer.EndRenderPass();

                commandBuffer.End();
            }
        }

        private void CreateSemaphores()
        {
            this.screenRenderSemaphore = device.CreateSemaphore(new SemaphoreCreateInfo());
            this.imageAvailableSemaphore = device.CreateSemaphore(new SemaphoreCreateInfo());
            this.renderFinishedSemaphore = device.CreateSemaphore(new SemaphoreCreateInfo());
        }

        private Bool32 DebugReport(DebugReportFlags flags, DebugReportObjectType objectType, ulong @object, Size location, int messageCode, string layerPrefix, string message, IntPtr userData)
        {
            this.logging.Log($"{flags}: {message}");

            return true;
        }

        private uint FindMemoryType(uint typeFilter, MemoryPropertyFlags flags)
        {
            var memoryProperties = this.physicalDevice.GetMemoryProperties();

            for (int i = 0; i < memoryProperties.MemoryTypes.Length; i++)
            {
                if ((typeFilter & (1u << i)) > 0
                        && memoryProperties.MemoryTypes[i].PropertyFlags.HasFlag(flags))
                {
                    return (uint)i;
                }
            }

            throw new Exception("No compatible memory type.");
        }

        private QueueFamilyIndices FindQueueFamilies(PhysicalDevice device)
        {
            QueueFamilyIndices indices = new QueueFamilyIndices();

            var queueFamilies = device.GetQueueFamilyProperties();

            for (uint index = 0; index < queueFamilies.Length && !indices.IsComplete; index++)
            {
                if (queueFamilies[index].QueueFlags.HasFlag(QueueFlags.Graphics))
                {
                    indices.GraphicsFamily = index;
                }

                if (device.GetSurfaceSupport(index, this.surface))
                {
                    indices.PresentFamily = index;
                }

                if (queueFamilies[index].QueueFlags.HasFlag(QueueFlags.Transfer) && !queueFamilies[index].QueueFlags.HasFlag(QueueFlags.Graphics))
                {
                    indices.TransferFamily = index;
                }
            }

            if (!indices.TransferFamily.HasValue)
            {
                indices.TransferFamily = indices.GraphicsFamily;
            }

            return indices;
        }

        private SurfaceFormat ChooseSwapSurfaceFormat(SurfaceFormat[] availableFormats)
        {
            if (availableFormats.Length == 1 && availableFormats[0].Format == Format.Undefined)
            {
                return new SurfaceFormat
                {
                    Format = Format.B8G8R8A8UNorm,
                    ColorSpace = ColorSpace.SrgbNonlinear
                };
            }

            foreach (var format in availableFormats)
            {
                if (format.Format == Format.B8G8R8A8UNorm && format.ColorSpace == ColorSpace.SrgbNonlinear)
                {
                    return format;
                }
            }

            return availableFormats[0];
        }

        private PresentMode ChooseSwapPresentMode(PresentMode[] availablePresentModes)
        {
            return availablePresentModes.Contains(PresentMode.Mailbox)
                    ? PresentMode.Mailbox
                    : PresentMode.Fifo;
        }

        public Extent2D ChooseSwapExtent(SurfaceCapabilities capabilities)
        {
            if (capabilities.CurrentExtent.Width != uint.MaxValue)
            {
                return capabilities.CurrentExtent;
            }
            else
            {
                return new Extent2D
                {
                    Width = Math.Max(capabilities.MinImageExtent.Width, Math.Min(capabilities.MaxImageExtent.Width, this.windowHost.WindowWidth)),
                    Height = Math.Max(capabilities.MinImageExtent.Height, Math.Min(capabilities.MaxImageExtent.Height, this.windowHost.WindowHeight))
                };
            }
        }

        SwapChainSupportDetails QuerySwapChainSupport(PhysicalDevice device)
        {
            return new SwapChainSupportDetails
            {
                Capabilities = device.GetSurfaceCapabilities(this.surface),
                Formats = device.GetSurfaceFormats(this.surface),
                PresentModes = device.GetSurfacePresentModes(this.surface)
            };
        }

        private bool IsSuitableDevice(PhysicalDevice device)
        {
            return device.EnumerateDeviceExtensionProperties(null).Any(extension => extension.ExtensionName == KhrSwapchain.ExtensionName)
                    && FindQueueFamilies(device).IsComplete;
        }

        private static uint[] LoadShaderData(string filePath, out int codeSize)
        {
            var fileBytes = System.IO.File.ReadAllBytes(filePath);
            var shaderData = new uint[(int)Math.Ceiling(fileBytes.Length / 4f)];

            System.Buffer.BlockCopy(fileBytes, 0, shaderData, 0, fileBytes.Length);

            codeSize = fileBytes.Length;

            return shaderData;
        }

        private void CreateBuffer(ulong size, BufferUsageFlags usage, MemoryPropertyFlags properties, out Buffer buffer, out DeviceMemory bufferMemory)
        {
            buffer = device.CreateBuffer(new BufferCreateInfo
            {
                Size = size,
                Usage = usage,
                SharingMode = SharingMode.Exclusive
            });

            var memRequirements = buffer.GetMemoryRequirements();

            bufferMemory = device.AllocateMemory(new MemoryAllocateInfo
            {
                AllocationSize = memRequirements.Size,
                MemoryTypeIndex = FindMemoryType(memRequirements.MemoryTypeBits, properties)
            });

            buffer.BindMemory(bufferMemory, 0);
        }

        private void CopyBuffer(Buffer sourceBuffer, Buffer destinationBuffer, ulong size)
        {
            var transferBuffers = this.BeginSingleTimeCommand();

            transferBuffers[0].CopyBuffer(sourceBuffer, destinationBuffer, new[] { new BufferCopy { Size = size } });

            this.EndSingleTimeCommand(transferBuffers);
        }

        private void CopyImage(Image sourceImage, Image destinationImage, uint width, uint height)
        {
            var transferBuffers = this.BeginSingleTimeCommand();

            ImageSubresourceLayers subresource = new ImageSubresourceLayers
            {
                AspectMask = ImageAspectFlags.Color,
                BaseArrayLayer = 0,
                LayerCount = 1,
                MipLevel = 0
            };

            ImageCopy region = new ImageCopy
            {
                DestinationSubresource = subresource,
                SourceSubresource = subresource,
                SourceOffset = new Offset3D(),
                DestinationOffset = new Offset3D(),
                Extent = new Extent3D
                {
                    Width = width,
                    Height = height,
                    Depth = 1
                }
            };

            transferBuffers[0].CopyImage(sourceImage, ImageLayout.TransferSourceOptimal, destinationImage, ImageLayout.TransferDestinationOptimal, new[] { region });

            this.EndSingleTimeCommand(transferBuffers);
        }

        private void TransitionImageLayout(Image image, Format format, ImageLayout oldLayout, ImageLayout newLayout)
        {
            var commandBuffer = this.BeginSingleTimeCommand();

            var barrier = new ImageMemoryBarrier
            {
                OldLayout = oldLayout,
                NewLayout = newLayout,
                SourceQueueFamilyIndex = Constants.QueueFamilyIgnored,
                DestinationQueueFamilyIndex = Constants.QueueFamilyIgnored,
                Image = image,
                SubresourceRange = new ImageSubresourceRange
                {
                    AspectMask = ImageAspectFlags.Color,
                    BaseMipLevel = 0,
                    LevelCount = 1,
                    BaseArrayLayer = 0,
                    LayerCount = 1
                }
            };

            if (oldLayout == ImageLayout.Preinitialized && newLayout == ImageLayout.TransferSourceOptimal)
            {
                barrier.SourceAccessMask = AccessFlags.HostWrite;
                barrier.DestinationAccessMask = AccessFlags.TransferRead;
            }
            else if (oldLayout == ImageLayout.Preinitialized && newLayout == ImageLayout.TransferDestinationOptimal)
            {
                barrier.SourceAccessMask = AccessFlags.HostWrite;
                barrier.DestinationAccessMask = AccessFlags.TransferWrite;
            }
            else if (oldLayout == ImageLayout.TransferDestinationOptimal && newLayout == ImageLayout.ShaderReadOnlyOptimal)
            {
                barrier.SourceAccessMask = AccessFlags.TransferWrite;
                barrier.DestinationAccessMask = AccessFlags.ShaderRead;
            }
            else
            {
                throw new Exception("Unsupported layout transition");
            }

            commandBuffer[0].PipelineBarrier(PipelineStageFlags.TopOfPipe,
                                                PipelineStageFlags.TopOfPipe,
                                                DependencyFlags.None,
                                                null,
                                                null,
                                                new[] { barrier });

            this.EndSingleTimeCommand(commandBuffer);
        }

        private CommandBuffer[] BeginSingleTimeCommand()
        {
            var result = device.AllocateCommandBuffers(new CommandBufferAllocateInfo
            {
                Level = CommandBufferLevel.Primary,
                CommandPool = this.transientCommandPool,
                CommandBufferCount = 1
            });

            result[0].Begin(new CommandBufferBeginInfo
            {
                Flags = CommandBufferUsageFlags.OneTimeSubmit
            });

            return result;
        }

        private void EndSingleTimeCommand(CommandBuffer[] transferBuffers)
        {
            transferBuffers[0].End();

            this.transferQueue.Submit(new[] { new SubmitInfo { CommandBuffers = transferBuffers } }, null);
            this.transferQueue.WaitIdle();

            this.transientCommandPool.FreeCommandBuffers(transferBuffers);
        }

        private void CreateImage(uint width, uint height, Format format, ImageTiling imageTiling, ImageUsageFlags usage, MemoryPropertyFlags properties, out Image image, out DeviceMemory imageMemory)
        {
            image = this.device.CreateImage(new ImageCreateInfo
            {
                ImageType = ImageType.Image2d,
                Extent = new Extent3D
                {
                    Width = width,
                    Height = height,
                    Depth = 1
                },
                ArrayLayers = 1,
                MipLevels = 1,
                Format = format,
                Tiling = imageTiling,
                InitialLayout = ImageLayout.Preinitialized,
                Usage = usage,
                SharingMode = SharingMode.Exclusive,
                Samples = SampleCountFlags.SampleCount1,
                Flags = ImageCreateFlags.None
            });

            var memoryRequirements = image.GetMemoryRequirements();

            imageMemory = this.device.AllocateMemory(new MemoryAllocateInfo
            {
                AllocationSize = memoryRequirements.Size,
                MemoryTypeIndex = this.FindMemoryType(memoryRequirements.MemoryTypeBits, properties)
            });

            image.BindMemory(imageMemory, 0);
        }

        private ImageView CreateImageView(Image image, Format format)
        {
            return device.CreateImageView(new ImageViewCreateInfo
            {
                Components = ComponentMapping.Identity,
                Format = format,
                Image = image,
                Flags = ImageViewCreateFlags.None,
                ViewType = ImageViewType.ImageView2d,
                SubresourceRange = new ImageSubresourceRange
                {
                    AspectMask = ImageAspectFlags.Color,
                    BaseMipLevel = 0,
                    LevelCount = 1,
                    BaseArrayLayer = 0,
                    LayerCount = 1
                }
            });
        }

        private struct QueueFamilyIndices
        {
            public uint? GraphicsFamily;
            public uint? PresentFamily;
            public uint? TransferFamily;

            public IEnumerable<uint> Indices
            {
                get
                {
                    if (this.GraphicsFamily.HasValue)
                    {
                        yield return this.GraphicsFamily.Value;
                    }

                    if (this.PresentFamily.HasValue && this.PresentFamily != this.GraphicsFamily)
                    {
                        yield return this.PresentFamily.Value;
                    }

                    if (this.TransferFamily.HasValue && this.TransferFamily != this.PresentFamily && this.TransferFamily != this.GraphicsFamily)
                    {
                        yield return this.TransferFamily.Value;
                    }
                }
            }

            public bool IsComplete
            {
                get
                {
                    return this.GraphicsFamily.HasValue
                        && this.PresentFamily.HasValue
                        && this.TransferFamily.HasValue;
                }
            }
        }

        private struct SwapChainSupportDetails
        {
            public SurfaceCapabilities Capabilities;
            public SurfaceFormat[] Formats;
            public PresentMode[] PresentModes;
        }

        public struct UniformBufferObject
        {
            public mat4 World;
            public mat4 View;
            public mat4 Projection;
        };

        private struct Vertex
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
                    Stride = (uint)Marshal.SizeOf<Vertex>(),
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
}
