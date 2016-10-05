using GBJam5.Vulkan;
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
        : GameService, IGraphicsDeviceService, IUpdatable, IVulkanInstance
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
        private CommandPool transientCommandPool;
        private CommandPool commandPool;
        private Image stagingImage;
        private DeviceMemory stagingImageMemory;
        private Image textureImage;
        private DeviceMemory textureImageMemory;
        private ImageView textureImageView;
        private Sampler textureSampler;
        private DescriptorPool descriptorPool;

        private OffScreenRenderPipeline offScreenRenderPipeline;
        private SwapchainRenderPipeline swapchainRenderPipeline;

        private Semaphore screenRenderSemaphore;
        private Semaphore imageAvailableSemaphore;
        private Semaphore renderFinishedSemaphore;

        private Format swapChainFormat;
        private Extent2D swapChainExtent;

        private uint stagingBufferSize;
        private Buffer stagingBuffer;
        private DeviceMemory stagingBufferMemory;

        public VulkanDeviceService()
        {
            this.debugReportDelegate = this.DebugReport;
        }

        public int PixelScale
        {
            get;
            set;
        } = 4;

        public Device Device => this.device;

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
            this.CreateCommandPools();
            this.CreateTextureImage();
            this.CreateTextureImageView();
            this.CreateTextureSampler();
            this.CreateDescriptorPool();
            this.CreateOffScreenRenderPipeline();
            this.CreateSwapchainRenderPipeline();
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

            this.UpdateBuffer(this.offScreenRenderPipeline.UniformBuffer,
                new[]
                {
                    new UniformBufferObject
                    {
                        World = mat4.Translate(32, 32, 0) * mat4.Scale(16, 16, 1),
                        View = mat4.Identity,
                        Projection = mat4.Translate(-1, -1, 0)
                                        * mat4.Scale(2)
                                        * mat4.Scale(1 / (float)gbTextureWidth, 1 / (float)gbTextureHeight, 1)
                    }
                });

            this.graphicsQueue.Submit(new SubmitInfo[]
            {
                new SubmitInfo
                {
                    CommandBuffers = new [] { this.offScreenRenderPipeline.CommandBuffers[0] },
                    SignalSemaphores = new [] { this.screenRenderSemaphore },
                    WaitDestinationStageMask = new [] { PipelineStageFlags.ColorAttachmentOutput },
                    WaitSemaphores = new [] { this.imageAvailableSemaphore }
                }
            }, null);

            this.UpdateBuffer(this.swapchainRenderPipeline.UniformBuffer, new UniformBufferObject
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
                    CommandBuffers = new [] { this.swapchainRenderPipeline.CommandBuffers[nextImage] },
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

        public void UpdateBuffer<T>(Buffer buffer, T data, int offset = 0)
            where T : struct
        {
            uint dataSize = MemUtil.SizeOf<T>();
            uint dataOffset = (uint)(offset * dataSize);

            this.CheckStagingBufferSize(dataSize, dataOffset);

            IntPtr memoryBuffer = IntPtr.Zero;
            this.stagingBufferMemory.MapMemory(dataOffset, dataSize, MemoryMapFlags.None, ref memoryBuffer);

            MemUtil.WriteToPtr(memoryBuffer, data);

            this.stagingBufferMemory.UnmapMemory();

            this.CopyBuffer(this.stagingBuffer, buffer, dataOffset + dataSize);
        }

        public void UpdateBuffer<T>(Buffer buffer, T[] data, int offset = 0)
            where T : struct
        {
            uint dataSize = (uint)(MemUtil.SizeOf<T>() * data.Length);
            uint dataOffset = (uint)(offset * dataSize);

            this.CheckStagingBufferSize(dataSize, dataOffset);

            IntPtr memoryBuffer = IntPtr.Zero;
            this.stagingBufferMemory.MapMemory(dataOffset, dataSize, MemoryMapFlags.None, ref memoryBuffer);

            MemUtil.WriteToPtr(memoryBuffer, data, 0, data.Length);

            this.stagingBufferMemory.UnmapMemory();

            this.CopyBuffer(this.stagingBuffer, buffer, dataOffset + dataSize);
        }

        public void CheckStagingBufferSize(uint dataSize, uint dataOffset)
        {
            uint memRequirement = dataOffset + dataSize;

            if (memRequirement > this.stagingBufferSize)
            {
                if (stagingBuffer != null)
                {
                    this.stagingBuffer.Destroy();
                    this.device.FreeMemory(this.stagingBufferMemory);
                }

                this.CreateBuffer(memRequirement, BufferUsageFlags.TransferSource, MemoryPropertyFlags.HostVisible | MemoryPropertyFlags.HostCoherent, out this.stagingBuffer, out this.stagingBufferMemory);
                this.stagingBufferSize = memRequirement;
            }
        }

        private void RecreateSwapChain()
        {
            this.device.WaitIdle();
            
            this.swapChain.Dispose();
            this.swapChain = null;

            this.CreateSwapChain();

            this.CreateSwapchainRenderPipeline();
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
            
            this.swapChainFormat = surfaceFormat.Format;
            this.swapChainExtent = extent;
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

        private void CreateOffScreenRenderPipeline()
        {
            this.offScreenRenderPipeline = new OffScreenRenderPipeline(this,
                                                                        this.commandPool,
                                                                        new ClearColorValue(0.25f, 0.5f, 0f, 1f),
                                                                        this.descriptorPool,
                                                                        this.textureImageView,
                                                                        this.textureSampler);
        }

        private void CreateSwapchainRenderPipeline()
        {
            this.swapchainRenderPipeline = new SwapchainRenderPipeline(this,
                                                                        this.commandPool,
                                                                        new ClearColorValue(0f, 0f, 0f, 1f),
                                                                        this.descriptorPool,
                                                                        this.swapChain,
                                                                        this.swapChainExtent,
                                                                        this.swapChainFormat,
                                                                        this.offScreenRenderPipeline.ImageView,
                                                                        this.textureSampler);
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

        public uint[] LoadShaderData(string filePath, out int codeSize)
        {
            var fileBytes = System.IO.File.ReadAllBytes(filePath);
            var shaderData = new uint[(int)Math.Ceiling(fileBytes.Length / 4f)];

            System.Buffer.BlockCopy(fileBytes, 0, shaderData, 0, fileBytes.Length);

            codeSize = fileBytes.Length;

            return shaderData;
        }

        public void CreateBuffer(ulong size, BufferUsageFlags usage, MemoryPropertyFlags properties, out Buffer buffer, out DeviceMemory bufferMemory)
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

        public void CreateImage(uint width, uint height, Format format, ImageTiling imageTiling, ImageUsageFlags usage, MemoryPropertyFlags properties, out Image image, out DeviceMemory imageMemory)
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

        public ImageView CreateImageView(Image image, Format format)
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
    }
}
