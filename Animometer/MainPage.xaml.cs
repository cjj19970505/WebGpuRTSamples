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
// The Blank Page item template is documented at https://go.microsoft.com/fwlink/?LinkId=402352&clcid=0x409

namespace Animometer
{
    
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainPage : Page
    {
        struct Settings
        {
            public UInt32 NumTriangles;
            public bool RenderBundles;
            public bool DynmicOffsets;
        }
        const UInt32 Vec4Size = 4 * sizeof(float);

        const GpuTextureFormat SwapChainFormat = GpuTextureFormat.BGRA8UNorm;
        GpuDevice Device { get; set; }
        GpuSwapChainDescriptor SwapChainDescriptor { get; set; }
        GpuSwapChain SwapChain { set; get; }

        GpuBindGroupLayout BindGroupLayout { get; set; }
        GpuBindGroupLayout DynamicBindGroupLayout { get; set; }
        GpuBindGroupLayout TimeBindGroupLayout { get; set; }
        GpuRenderPipeline Pipeline { get; set; }
        GpuRenderPipeline DynamicPipeline { get; set; }
        GpuBuffer VertexBuffer { get; set; }
        public MainPage()
        {
            this.InitializeComponent();
            GpuView.Width = Window.Current.Bounds.Width;
            GpuView.Height = Window.Current.Bounds.Height;
        }

        async Task Init()
        {
            var gpu = new Gpu();
            gpu.EnableD3D12DebugLayer();
            var adapter = await gpu.RequestAdapterAsync();
            Device = await adapter.RequestDeviceAsync();
            TimeBindGroupLayout = Device.CreateBindGroupLayout(new GpuBindGroupLayoutDescriptor(new GpuBindGroupLayoutEntry[]
            {
                new GpuBindGroupLayoutEntry()
                {
                    Binding = 0,
                    Visibility = GpuShaderStageFlags.Vertex,
                    Buffer = new GpuBufferBindingLayout()
                    {
                        Type = GpuBufferBindingType.Uniform,
                        MinBindingSize = sizeof(float)
                    }
                }
            }));
            BindGroupLayout = Device.CreateBindGroupLayout(new GpuBindGroupLayoutDescriptor(new GpuBindGroupLayoutEntry[]
            {
                new GpuBindGroupLayoutEntry()
                {
                    Binding = 0,
                    Visibility = GpuShaderStageFlags.Vertex,
                    Buffer = new GpuBufferBindingLayout()
                    {
                        Type = GpuBufferBindingType.Uniform,
                        MinBindingSize = 20
                    }
                }
            }));
            DynamicBindGroupLayout = Device.CreateBindGroupLayout(new GpuBindGroupLayoutDescriptor(new GpuBindGroupLayoutEntry[]
            {
                new GpuBindGroupLayoutEntry()
                {
                    Binding = 0,
                    Visibility = GpuShaderStageFlags.Vertex,
                    Buffer = new GpuBufferBindingLayout()
                    {
                        Type = GpuBufferBindingType.Uniform,
                        HasDynamicOffset = true,
                        MinBindingSize = 20
                    }
                }
            }));

            var pipelineLayout = Device.CreatePipelineLayout(new GpuPipelineLayoutDescriptor()
            {
                BindGroupLayouts = new GpuBindGroupLayout[] { TimeBindGroupLayout, BindGroupLayout }
            });
            var dynamicPipelineLayout = Device.CreatePipelineLayout(new GpuPipelineLayoutDescriptor()
            {
                BindGroupLayouts = new GpuBindGroupLayout[] { TimeBindGroupLayout, DynamicBindGroupLayout }
            });

            string shaderCode;
            using (var shaderFileStream = typeof(MainPage).Assembly.GetManifestResourceStream("Animometer.shader.hlsl"))
            using (var shaderStreamReader = new StreamReader(shaderFileStream))
            {
                shaderCode = shaderStreamReader.ReadToEnd();
            }
            var shader = Device.CreateShaderModule(new GpuShaderModuleDescriptor(GpuShaderSourceType.Hlsl, shaderCode));

            var pipelineDescriptor = new GpuRenderPipelineDescriptor(new GpuVertexState(shader, "VSMain")
            {
                VertexBuffers = new GpuVertexBufferLayout[]
                {
                    new GpuVertexBufferLayout(2 * Vec4Size, new GpuVertexAttribute[]
                    {
                        new GpuVertexAttribute()
                        {
                            ShaderLocation = 0,
                            Offset = 0,
                            Format = GpuVertexFormat.Float4
                        },
                        new GpuVertexAttribute()
                        {
                            ShaderLocation = 1,
                            Offset = Vec4Size,
                            Format = GpuVertexFormat.Float4
                        }
                    })
                    {
                        StepMode = GpuInputStepMode.Vertex
                    }
                }
            })
            {
                Fragment = new GpuFragmentState(shader, "PSMain", new GpuColorTargetState[] { new GpuColorTargetState() { Format = SwapChainFormat } }),
                Primitive = new GpuPrimitiveState()
                {
                    Topology = GpuPrimitiveTopology.TriangleList,
                    FrontFace = GpuFrontFace.Ccw,
                    CullMode = GpuCullMode.None
                }
            };
            pipelineDescriptor.Layout = pipelineLayout;
            Pipeline = Device.CreateRenderPipeline(pipelineDescriptor);
            pipelineDescriptor.Layout = dynamicPipelineLayout;
            DynamicPipeline = Device.CreateRenderPipeline(pipelineDescriptor);
            VertexBuffer = Device.CreateBuffer(new GpuBufferDescriptor(2 * 3 * Vec4Size, GpuBufferUsageFlags.Vertex) { MappedAtCreation = true });
            float[] vertexData = new float[]
            {
                0, 0.1f, 0, 1,  1, 0, 0, 1,
                -0.1f,-0.1f,0,1, 0,1,0,1,
                0.1f,-0.1f,0,1, 0,0,1,1
            };
            Windows.Storage.Streams.Buffer verticeCpuBuffer = new Windows.Storage.Streams.Buffer((uint)Buffer.ByteLength(vertexData));
            verticeCpuBuffer.Length = verticeCpuBuffer.Capacity;
            using (var verticeCpuStream = verticeCpuBuffer.AsStream())
            {
                byte[] vertexBufferBytes = new byte[Buffer.ByteLength(vertexData)];
                Buffer.BlockCopy(vertexData, 0, vertexBufferBytes, 0, Buffer.ByteLength(vertexData));
                await verticeCpuStream.WriteAsync(vertexBufferBytes, 0, Buffer.ByteLength(vertexData));
            }
            verticeCpuBuffer.CopyTo(VertexBuffer.MappedRange());
            VertexBuffer.Unmap();
        }

        class Configure
        {
            Settings Settings { get; }
            GpuDevice Device { get; }
            GpuBindGroup[] BindGroups { get; }
            GpuBindGroup TimeBindGroup { get; }
            GpuBindGroup DynamicBindGroup { get; }
            GpuRenderPipeline Pipeline { get; }
            GpuRenderPipeline DynamicPipeline { get; }
            GpuBuffer VertexBuffer { get; }
            const UInt32 UniformBytes = 5 * sizeof(float);
            DateTime StartDateTime = DateTime.Now;
            GpuTextureFormat SwapChainFormat { get; }
            GpuRenderBundle RenderBundle { get; }
            GpuBuffer UniformBuffer { get; }
            UInt32 AlignedUniformBytes 
            { 
                get 
                {
                    return (UInt32)(MathF.Ceiling(((float)UniformBytes) / 256) * 256);
                } 
            }
            UInt32 TimeOffset
            {
                get
                {
                    return Settings.NumTriangles * AlignedUniformBytes;
                }
            }
            public Configure(GpuDevice device, Settings settings, GpuBindGroupLayout bindGroupLayout, GpuBindGroupLayout dynamicBindGroupLayout, GpuBindGroupLayout timeBindGroupLayout, GpuRenderPipeline pipeline, GpuRenderPipeline dynamicPipeline, GpuBuffer vertexBuffer, GpuTextureFormat swapChainFormat)
            {
                Device = device;
                Settings = settings;
                Pipeline = pipeline;
                DynamicPipeline = dynamicPipeline;
                VertexBuffer = vertexBuffer;
                SwapChainFormat = swapChainFormat;
                
                UniformBuffer = Device.CreateBuffer(new GpuBufferDescriptor(Settings.NumTriangles * AlignedUniformBytes + sizeof(float), GpuBufferUsageFlags.Uniform | GpuBufferUsageFlags.CopyDst));
                var uniformCpuBuffer = new Windows.Storage.Streams.Buffer(Settings.NumTriangles * AlignedUniformBytes)
                {
                    Length = Settings.NumTriangles * AlignedUniformBytes
                };
                using (var uniformCpuStream = uniformCpuBuffer.AsStream())
                using (var uniformCpuWriter = new BinaryWriter(uniformCpuStream))
                {
                    var rand = new Random();
                    for (var i = 0; i < Settings.NumTriangles; ++i)
                    {
                        uniformCpuWriter.Seek((int)(i * AlignedUniformBytes), SeekOrigin.Begin);
                        uniformCpuWriter.Write((float)(rand.NextDouble() * 0.2 + 0.2)); //Scale
                        uniformCpuWriter.Write((float)(0.9 * 2 * (rand.NextDouble() - 0.5))); //offsetX
                        uniformCpuWriter.Write((float)(0.9 * 2 * (rand.NextDouble() - 0.5))); //offsetY
                        uniformCpuWriter.Write((float)(rand.NextDouble() * 1.5 + 0.5)); //scalar
                        uniformCpuWriter.Write((float)(rand.NextDouble() * 10)); //scalar offset
                    }
                }
                BindGroups = new GpuBindGroup[Settings.NumTriangles];
                for (var i = 0; i < Settings.NumTriangles; ++i)
                {
                    BindGroups[i] = Device.CreateBindGroup(new GpuBindGroupDescriptor(bindGroupLayout, new GpuBindGroupEntry[]
                    {
                        new GpuBindGroupEntry(0, new GpuBufferBinding(UniformBuffer, 6*sizeof(float))
                        {
                            Offset = (UInt64)(i * AlignedUniformBytes)
                        })
                    }));
                }
                DynamicBindGroup = Device.CreateBindGroup(new GpuBindGroupDescriptor(dynamicBindGroupLayout, new GpuBindGroupEntry[]
                {
                    new GpuBindGroupEntry(0, new GpuBufferBinding(UniformBuffer, 6*sizeof(float)))
                }));

                
                TimeBindGroup = Device.CreateBindGroup(new GpuBindGroupDescriptor(timeBindGroupLayout, new GpuBindGroupEntry[]
                {
                    new GpuBindGroupEntry(0, new GpuBufferBinding(UniformBuffer, sizeof(float))
                    {
                        Offset = TimeOffset
                    })
                }));
                Device.DefaultQueue.WriteBuffer(UniformBuffer, 0, uniformCpuBuffer, uniformCpuBuffer.Length);
                var renderBundleEncoder = Device.CreateRenderBundleEncoder(new GpuRenderBundleEncoderDescriptor(new GpuTextureFormat[] { SwapChainFormat }));
                RecordRenderPass(renderBundleEncoder);
                RenderBundle = renderBundleEncoder.Finish();
            }
            void RecordRenderPass(GpuRenderEncoderBase passEncoder)
            {
                if(Settings.DynmicOffsets)
                {
                    passEncoder.SetPipeline(DynamicPipeline);
                }
                else
                {
                    passEncoder.SetPipeline(Pipeline);
                }
                passEncoder.SetVertexBuffer(0, VertexBuffer, 0, VertexBuffer.Size);
                (passEncoder as GpuProgrammablePassEncoder).SetBindGroup(0, TimeBindGroup);
                uint[] dynamicOffsets = new uint[1];
                for(var i = 0; i < Settings.NumTriangles; ++i)
                {
                    if(Settings.DynmicOffsets)
                    {
                        dynamicOffsets[0] = (uint)(i * AlignedUniformBytes);
                        (passEncoder as GpuProgrammablePassEncoder).SetBindGroup(1, DynamicBindGroup, dynamicOffsets);
                    }
                    else
                    {
                        (passEncoder as GpuProgrammablePassEncoder).SetBindGroup(1, BindGroups[i]);
                    }
                    passEncoder.Draw(3, 1, 0, 0);
                }
            }
            public void DoDraw(DateTime dateTime, GpuSwapChain swapChain)
            {
                var uniformTimeCpuBuffer = new Windows.Storage.Streams.Buffer(sizeof(float));
                using(var uniformTimeCpuBufferStream = uniformTimeCpuBuffer.AsStream())
                using(var uniformTimeCpuBufferWriter = new BinaryWriter(uniformTimeCpuBufferStream))
                {
                    uniformTimeCpuBufferWriter.Write(((float)(dateTime - StartDateTime).TotalMilliseconds) / 1000);
                }
                Device.DefaultQueue.WriteBuffer(UniformBuffer, TimeOffset, uniformTimeCpuBuffer);
                var renderPassDesriptor = new GpuRenderPassDescriptor(new GpuRenderPassColorAttachment[]
                {
                   new GpuRenderPassColorAttachment(swapChain.GetCurrentTexture().CreateView(), new GpuColorDict{ R = 0, B = 0, G = 0, A = 1.0f})
                });
                var commandEncoder = Device.CreateCommandEncoder();
                var passEncoder = commandEncoder.BeginRenderPass(renderPassDesriptor);
                if(Settings.RenderBundles)
                {
                    passEncoder.ExecuteBundles(new GpuRenderBundle[] { RenderBundle });
                }
                else
                {
                    RecordRenderPass(passEncoder);
                }
                passEncoder.EndPass();
                Device.DefaultQueue.Sumit(new GpuCommandBuffer[] { commandEncoder.Finish() });
            }
        }



        private async void Page_Loaded(object sender, RoutedEventArgs e)
        {
            await Init();
            GpuFence fence = Device.DefaultQueue.CreateFence();
            UInt64 currentFenceValue = 0;
            Settings settings = new Settings
            {
                DynmicOffsets = false,
                NumTriangles = 10,
                RenderBundles = false
            };
            Configure configure = new Configure(Device, settings, BindGroupLayout, DynamicBindGroupLayout, TimeBindGroupLayout, Pipeline, DynamicPipeline, VertexBuffer, SwapChainFormat);
            while (true)
            {
                if (SwapChain == null)
                {
                    SwapChainDescriptor = new GpuSwapChainDescriptor(SwapChainFormat, (uint)GpuView.Width, (uint)GpuView.Height);
                    SwapChain = Device.ConfigureSwapChainForSwapChainPanel(SwapChainDescriptor, GpuView);
                }

                configure.DoDraw(DateTime.Now, SwapChain);
                SwapChain.Present();
                var fenceValueWaitFor = ++currentFenceValue;
                Device.DefaultQueue.Signal(fence, fenceValueWaitFor);
                await fence.OnCompletionAsync(fenceValueWaitFor);
            }
        }

        private void Page_SizeChanged(object sender, SizeChangedEventArgs e)
        {

        }
    }
}
