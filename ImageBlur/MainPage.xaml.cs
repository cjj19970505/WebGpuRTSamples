using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;
using WebGpuRT;
using System.Threading.Tasks;
using Windows.Graphics.Imaging;
using Windows.UI.ViewManagement;
// The Blank Page item template is documented at https://go.microsoft.com/fwlink/?LinkId=402352&clcid=0x409

namespace ImageBlur
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainPage : Page
    {
        struct Settings
        {
            public uint FilterSize { get; set; }
            public uint Iterations { get; set; }
        }
        const GpuTextureFormat SwapChainFormat = GpuTextureFormat.BGRA8UNorm;
        uint[] Batch = new UInt32[2] { 4, 4 };
        const uint TileDim = 256;
        Gpu Gpu { get; set; }
        GpuDevice Device { get; set; }
        GpuSwapChainDescriptor SwapChainDescriptor { get; set; }
        GpuSwapChain SwapChain { set; get; }
        ViewModel ViewModel { get; set; }

        public MainPage()
        {
            Gpu = new Gpu();
#if DEBUG
            Gpu.EnableD3D12DebugLayer();
#endif
            this.InitializeComponent();
            ApplicationView.PreferredLaunchViewSize = new Size(512, 512);
            ApplicationView.PreferredLaunchWindowingMode = ApplicationViewWindowingMode.PreferredLaunchViewSize;
            DataContext = ViewModel = new ViewModel();
        }

        Windows.Storage.Streams.IBuffer CreateBufferFromArray<T>(T[] data)
        {
            byte[] byteData = new byte[Buffer.ByteLength(data)];
            Buffer.BlockCopy(data, 0, byteData, 0, byteData.Length);
            var buffer = new Windows.Storage.Streams.Buffer((uint)byteData.Length)
            {
                Length = (uint)byteData.Length
            };
            byteData.CopyTo(buffer);
            return buffer;
        }

        
        GpuComputePipeline BlurPipeline { get; set; }
        GpuBindGroup ComputeConstants { get; set; }
        GpuBindGroup ComputeBindGroup0 { get; set; }
        GpuBindGroup ComputeBindGroup1 { get; set; }
        GpuBindGroup ComputeBindGroup2 { get; set; }
        GpuBindGroup UniformBindGroup { get; set; }
        GpuRenderPipeline Pipeline { get; set; }
        GpuBuffer VerticesBuffer { get; set; }
        uint SrcWidth { get; set; }
        uint SrcHeight { get; set; }
        GpuBuffer BlurParamsBuffer { get; set; }
        async Task Init()
        {
            var adapter = await Gpu.RequestAdapterAsync();
            Device = await adapter.RequestDeviceAsync();
            var rectVerts = new float[]
            {
                1.0f,  1.0f, 0.0f, 1.0f, 0.0f,
                1.0f, -1.0f, 0.0f, 1.0f, 1.0f,
                -1.0f, -1.0f, 0.0f, 0.0f, 1.0f,
                1.0f,  1.0f, 0.0f, 1.0f, 0.0f,
                -1.0f, -1.0f, 0.0f, 0.0f, 1.0f,
                -1.0f,  1.0f, 0.0f, 0.0f, 0.0f
            };
            VerticesBuffer = Device.CreateBuffer(new GpuBufferDescriptor((uint)Buffer.ByteLength(rectVerts), GpuBufferUsageFlags.Vertex) { MappedAtCreation = true });
            CreateBufferFromArray(rectVerts).CopyTo(VerticesBuffer.MappedRange());
            VerticesBuffer.Unmap();

            var blurBindGroupLayouts = new GpuBindGroupLayout[] { 
                Device.CreateBindGroupLayout(new GpuBindGroupLayoutDescriptor(new GpuBindGroupLayoutEntry[]
                {
                    new GpuBindGroupLayoutEntry()
                    {
                        Binding = 0, 
                        Visibility = GpuShaderStageFlags.Compute, 
                        Sampler = new GpuSamplerBindingLayout()
                        { 
                            Type = GpuSamplerBindingType.Filtering
                        } 
                    },
                    new GpuBindGroupLayoutEntry()
                    {
                        Binding = 1, 
                        Visibility = GpuShaderStageFlags.Compute, 
                        Buffer = new GpuBufferBindingLayout()
                        {
                            Type = GpuBufferBindingType.Uniform, 
                            HasDynamicOffset = false, 
                            MinBindingSize = 2*4 /* 2 uints */
                        }
                    }
                })),
                Device.CreateBindGroupLayout(new GpuBindGroupLayoutDescriptor(new GpuBindGroupLayoutEntry[]
                {
                    new GpuBindGroupLayoutEntry()
                    {
                        Binding = 1, 
                        Visibility = GpuShaderStageFlags.Compute, 
                        Texture = new GpuTextureBindingLayout()
                        {
                            SampleType = GpuTextureSampleType.Float, 
                            ViewDimension = GpuTextureViewDimension._2D, 
                            Multisampled = false 
                        } 
                    },
                    new GpuBindGroupLayoutEntry()
                    {
                        Binding = 2,
                        Visibility = GpuShaderStageFlags.Compute,
                        StorageTexture = new GpuStorageTextureBindingLayout()
                        {
                            Format = GpuTextureFormat.RGBA8UNorm,
                            Access = GpuStorageTextureAccess.WriteOnly,
                            ViewDimension = GpuTextureViewDimension._2D
                        }
                    },
                    new GpuBindGroupLayoutEntry()
                    {
                        Binding = 3,
                        Visibility = GpuShaderStageFlags.Compute,
                        Buffer = new GpuBufferBindingLayout()
                        {
                            Type = GpuBufferBindingType.Uniform,
                            HasDynamicOffset = false,
                            MinBindingSize = 4
                        }
                    }
                }))
            };
            
            GpuShaderModule computeShader;
            using (var shaderFileStream = typeof(MainPage).Assembly.GetManifestResourceStream("ImageBlur.compute.hlsl"))
            using (var shaderStreamReader = new StreamReader(shaderFileStream))
            {
                var shaderCode = await shaderStreamReader.ReadToEndAsync();
                computeShader = Device.CreateShaderModule(new GpuShaderModuleDescriptor(GpuShaderSourceType.Hlsl, shaderCode));
            }
            GpuShaderModule drawShader;
            using (var shaderFileStream = typeof(MainPage).Assembly.GetManifestResourceStream("ImageBlur.draw.hlsl"))
            using (var shaderStreamReader = new StreamReader(shaderFileStream))
            {
                var shaderCode = await shaderStreamReader.ReadToEndAsync();
                drawShader = Device.CreateShaderModule(new GpuShaderModuleDescriptor(GpuShaderSourceType.Hlsl, shaderCode));
            }
            BlurPipeline = Device.CreateComputePipeline(new GpuComputePipelineDescriptor(new GpuProgrammableStage(computeShader, "main"))
            {
                Layout = Device.CreatePipelineLayout(new GpuPipelineLayoutDescriptor() { BindGroupLayouts = blurBindGroupLayouts })
            });

            var bindGroupLayout = Device.CreateBindGroupLayout(new GpuBindGroupLayoutDescriptor(new GpuBindGroupLayoutEntry[]
            {
                new GpuBindGroupLayoutEntry()
                {
                    Binding = 0,
                    Visibility = GpuShaderStageFlags.Fragment,
                    Sampler = new GpuSamplerBindingLayout()
                    {
                        Type = GpuSamplerBindingType.Filtering,
                    }
                },
                new GpuBindGroupLayoutEntry()
                {
                    Binding = 1,
                    Visibility = GpuShaderStageFlags.Fragment,
                    Texture = new GpuTextureBindingLayout()
                    {
                        SampleType = GpuTextureSampleType.Float,
                        ViewDimension = GpuTextureViewDimension._2D,
                        Multisampled = false
                    }
                }
            }));


            Pipeline = Device.CreateRenderPipeline(new GpuRenderPipelineDescriptor(new GpuVertexState(drawShader, "VSMain")
            {
                VertexBuffers = new GpuVertexBufferLayout[]
                {
                    new GpuVertexBufferLayout(20, new GpuVertexAttribute[]
                    {
                        new GpuVertexAttribute()
                        {
                            Format = GpuVertexFormat.Float3,
                            Offset = 0,
                            ShaderLocation = 0
                        },
                        new GpuVertexAttribute()
                        {
                            Format = GpuVertexFormat.Float2,
                            Offset = 12,
                            ShaderLocation = 1
                        }

                    })
                }
            })
            {
                Layout = Device.CreatePipelineLayout(new GpuPipelineLayoutDescriptor() 
                {
                    BindGroupLayouts = new GpuBindGroupLayout[] {bindGroupLayout }
                }),
                Fragment = new GpuFragmentState(drawShader, "PSMain", new GpuColorTargetState[] { new GpuColorTargetState() {Format = SwapChainFormat, Blend = null, WriteMask = GpuColorWriteFlags.All } }),
                Primitive = new GpuPrimitiveState() 
                { 
                    Topology = GpuPrimitiveTopology.TriangleList, 
                    CullMode = GpuCullMode.None,
                    FrontFace = GpuFrontFace.Ccw
                }
            });
            var sampler = Device.CreateSampler(new GpuSamplerDescriptor()
            {
                MinFilter = GpuFilterMode.Linear,
                MagFilter = GpuFilterMode.Linear
            });

            var imgDecoder = await BitmapDecoder.CreateAsync(typeof(MainPage).Assembly.GetManifestResourceStream("ImageBlur.Di_3d.png").AsRandomAccessStream());
            var imageBitmap = await imgDecoder.GetSoftwareBitmapAsync();
            (SrcWidth, SrcHeight) = ((uint)imageBitmap.PixelWidth, (uint)imageBitmap.PixelHeight);
            var cubeTexture = Device.CreateTexture(new GpuTextureDescriptor(new GpuExtend3DDict { Width = (uint)imageBitmap.PixelWidth, Height = (uint)imageBitmap.PixelHeight, Depth = 1 }, GpuTextureFormat.BGRA8UNorm, GpuTextureUsageFlags.Sampled | GpuTextureUsageFlags.CopyDst));
            Device.DefaultQueue.CopyImageBitmapToTexture(new GpuImageCopyImageBitmap(imageBitmap), new GpuImageCopyTexture(cubeTexture), new GpuExtend3DDict { Width = (uint)imageBitmap.PixelWidth, Height = (uint)imageBitmap.PixelHeight, Depth = 1 });
            var textures = new GpuTexture[2]
            {
                Device.CreateTexture(new GpuTextureDescriptor(new GpuExtend3DDict{Width = SrcWidth, Height = SrcHeight, Depth=1 }, GpuTextureFormat.RGBA8UNorm, GpuTextureUsageFlags.CopyDst | GpuTextureUsageFlags.Storage | GpuTextureUsageFlags.Sampled)),
                Device.CreateTexture(new GpuTextureDescriptor(new GpuExtend3DDict{Width = SrcWidth, Height = SrcHeight, Depth=1 }, GpuTextureFormat.RGBA8UNorm, GpuTextureUsageFlags.CopyDst | GpuTextureUsageFlags.Storage | GpuTextureUsageFlags.Sampled))
            };
            var buffer0 = Device.CreateBuffer(new GpuBufferDescriptor(4, GpuBufferUsageFlags.Uniform) { MappedAtCreation = true });
            using(var stream = buffer0.MappedRange().AsStream())
            using(var writer = new BinaryWriter(stream))
            {
                writer.Write(0u);
            }
            buffer0.Unmap();

            var buffer1 = Device.CreateBuffer(new GpuBufferDescriptor(4, GpuBufferUsageFlags.Uniform) { MappedAtCreation = true });
            using (var stream = buffer1.MappedRange().AsStream())
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(1u);
            }
            buffer1.Unmap();

            BlurParamsBuffer = Device.CreateBuffer(new GpuBufferDescriptor(8, GpuBufferUsageFlags.CopyDst | GpuBufferUsageFlags.Uniform));
            ComputeConstants = Device.CreateBindGroup(new GpuBindGroupDescriptor(blurBindGroupLayouts[0], new GpuBindGroupEntry[]
            {
                new GpuBindGroupEntry(0, sampler),
                new GpuBindGroupEntry(1, new GpuBufferBinding(BlurParamsBuffer, BlurParamsBuffer.Size))
            }));
            ComputeBindGroup0 = Device.CreateBindGroup(new GpuBindGroupDescriptor(blurBindGroupLayouts[1], new GpuBindGroupEntry[]
            {
                new GpuBindGroupEntry(1, cubeTexture.CreateView()),
                new GpuBindGroupEntry(2, textures[0].CreateView()),
                new GpuBindGroupEntry(3, new GpuBufferBinding(buffer0, buffer0.Size))
            }));
            ComputeBindGroup1 = Device.CreateBindGroup(new GpuBindGroupDescriptor(blurBindGroupLayouts[1], new GpuBindGroupEntry[]
            {
                new GpuBindGroupEntry(1, textures[0].CreateView()),
                new GpuBindGroupEntry(2, textures[1].CreateView()),
                new GpuBindGroupEntry(3, new GpuBufferBinding(buffer1, buffer1.Size))
            }));
            ComputeBindGroup2 = Device.CreateBindGroup(new GpuBindGroupDescriptor(blurBindGroupLayouts[1], new GpuBindGroupEntry[]
            {
                new GpuBindGroupEntry(1, textures[1].CreateView()),
                new GpuBindGroupEntry(2, textures[0].CreateView()),
                new GpuBindGroupEntry(3, new GpuBufferBinding(buffer0, buffer0.Size))
            }));
            UniformBindGroup = Device.CreateBindGroup(new GpuBindGroupDescriptor(bindGroupLayout, new GpuBindGroupEntry[]
            {
                new GpuBindGroupEntry(0, sampler),
                new GpuBindGroupEntry(1, textures[1].CreateView())
            }));
            
        }
        private Settings _LastSettings = new Settings();
        void DrawFrame(Settings settings)
        {
            //Device.DefaultQueue.WriteBuffer()
            var blockDim = TileDim - (settings.FilterSize - 1);
            if(_LastSettings.FilterSize == settings.FilterSize && _LastSettings.Iterations == settings.Iterations)
            {

            }
            else
            {
                Device.DefaultQueue.WriteBuffer(BlurParamsBuffer, 0, CreateBufferFromArray(new UInt32[] { settings.FilterSize, blockDim }));
                _LastSettings = settings;
            }
            

            var commandEncoder = Device.CreateCommandEncoder();
            var computePass = commandEncoder.BeginComputePass();
            computePass.SetPipeline(BlurPipeline);
            computePass.SetBindGroup(0, ComputeConstants);
            computePass.SetBindGroup(1, ComputeBindGroup0);
            computePass.Dispatch((uint)MathF.Ceiling(SrcWidth / ((float)blockDim)), (uint)MathF.Ceiling(SrcHeight / ((float)Batch[1])), 1);
            computePass.Dispatch(2, (uint)MathF.Ceiling(SrcHeight / ((float)Batch[1])), 1);
            computePass.SetBindGroup(1, ComputeBindGroup1);
            computePass.Dispatch((uint)MathF.Ceiling(SrcHeight / ((float)blockDim)), (uint)MathF.Ceiling(SrcWidth / ((float)Batch[1])), 1);
            for (int i = 0; i < settings.Iterations - 1; ++i)
            {
                computePass.SetBindGroup(1, ComputeBindGroup2);
                computePass.Dispatch((uint)MathF.Ceiling(SrcWidth / ((float)blockDim)), (uint)MathF.Ceiling(SrcHeight / ((float)Batch[1])), 1);

                computePass.SetBindGroup(1, ComputeBindGroup1);
                computePass.Dispatch((uint)MathF.Ceiling(SrcHeight / ((float)blockDim)), (uint)MathF.Ceiling(SrcWidth / ((float)Batch[1])), 1);
            }
            computePass.EndPass();
            var passEncoder = commandEncoder.BeginRenderPass(new GpuRenderPassDescriptor(new GpuRenderPassColorAttachment[] 
            {
                new GpuRenderPassColorAttachment(SwapChain.GetCurrentTexture().CreateView(), new GpuColorDict(){R = 0, G = 0, B = 0, A=1 })
            }));
            passEncoder.SetPipeline(Pipeline);
            passEncoder.SetVertexBuffer(0, VerticesBuffer, 0, VerticesBuffer.Size);
            passEncoder.SetBindGroup(0, UniformBindGroup);
            passEncoder.Draw(6, 1, 0, 0);
            passEncoder.EndPass();
            Device.DefaultQueue.Sumit(new GpuCommandBuffer[] { commandEncoder.Finish() });
        }

        private async void Page_Loaded(object sender, RoutedEventArgs e)
        {
            await Init();
            GpuFence fence = Device.DefaultQueue.CreateFence();
            UInt64 currentFenceValue = 0;
            while (true)
            {
                if (SwapChain == null)
                {
                    SwapChainDescriptor = new GpuSwapChainDescriptor(SwapChainFormat, (uint)GpuView.Width, (uint)GpuView.Height);
                    SwapChain = Device.ConfigureSwapChainForSwapChainPanel(SwapChainDescriptor, GpuView);
                }
                Settings settings = new Settings { FilterSize = (uint)ViewModel.FilterSize, Iterations = (uint)ViewModel.Iterations };
                DrawFrame(settings);
                SwapChain.Present();
                var fenceValueWaitFor = ++currentFenceValue;
                Device.DefaultQueue.Signal(fence, fenceValueWaitFor);
                await fence.OnCompletionAsync(fenceValueWaitFor);
            }
        }

        

        private void Page_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            GpuView.Width = Window.Current.Bounds.Width;
            GpuView.Height = Window.Current.Bounds.Height;
            SwapChainDescriptor = null;
            SwapChain = null;
        }
    }
}
