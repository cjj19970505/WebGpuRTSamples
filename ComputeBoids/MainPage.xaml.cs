﻿using System;
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
using Windows.UI.ViewManagement;


// The Blank Page item template is documented at https://go.microsoft.com/fwlink/?LinkId=402352&clcid=0x409

namespace ComputeBoids
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainPage : Page
    {
        const uint NumParticles = 1500;
        Gpu Gpu { get; set; }
        GpuDevice Device { get; set; }
        GpuSwapChainDescriptor SwapChainDescriptor { get; set; }
        GpuSwapChain SwapChain { set; get; }
        GpuTexture DepthTexture { set; get; }
        
        float[] SimParamData = new float[] {
                0.04f, // deltaT
                0.1f,     // rule1Distance
                0.025f,   // rule2Distance
                0.025f,   // rule3Distance
                0.02f,    // rule1Scale
                0.05f,    // rule2Scale
                0.005f,   // rule3Scale
        };
        float[] VertexBufferData = new float[]
        {
            -0.01f,
            -0.02f,
            0.01f,
            -0.02f,
            0.0f,
            0.02f
        };
        public MainPage()
        {
            Gpu = new Gpu();
            Gpu.EnableD3D12DebugLayer();
            this.InitializeComponent();
            ApplicationView.PreferredLaunchViewSize = new Size(512, 512);
            ApplicationView.PreferredLaunchWindowingMode = ApplicationViewWindowingMode.PreferredLaunchViewSize;
        }
        ulong T { get; set; }
        GpuBindGroup[] ParticleBindGroups { get; set; }
        GpuComputePipeline ComputePipeline { get; set; }
        GpuRenderPipeline RenderPipeline { get; set; }
        GpuBuffer[] ParticleBuffers { get; set; }
        GpuBuffer VerticesBuffer { get; set; }
        async Task Init()
        {
            var adapter = await Gpu.RequestAdapterAsync();
            Device = await adapter.RequestDeviceAsync();
            GpuShaderModule computeShader;
            using (var shaderFileStream = typeof(MainPage).Assembly.GetManifestResourceStream("ComputeBoids.compute.hlsl"))
            using (var shaderStreamReader = new StreamReader(shaderFileStream))
            {
                var shaderCode = await shaderStreamReader.ReadToEndAsync();
                computeShader = Device.CreateShaderModule(new GpuShaderModuleDescriptor(GpuShaderSourceType.Hlsl, shaderCode));
            }

            GpuShaderModule drawShader;
            using (var shaderFileStream = typeof(MainPage).Assembly.GetManifestResourceStream("ComputeBoids.draw.hlsl"))
            using (var shaderStreamReader = new StreamReader(shaderFileStream))
            {
                var shaderCode = await shaderStreamReader.ReadToEndAsync();
                drawShader = Device.CreateShaderModule(new GpuShaderModuleDescriptor(GpuShaderSourceType.Hlsl, shaderCode));
            }
            RenderPipeline = Device.CreateRenderPipeline(new GpuRenderPipelineDescriptor(new GpuVertexState(drawShader, "VSMain") 
            {
                VertexBuffers = new GpuVertexBufferLayout[] 
                {
                    new GpuVertexBufferLayout(4*4, new GpuVertexAttribute[]
                    {
                        new GpuVertexAttribute()
                        {
                            Format = GpuVertexFormat.Float2,
                            Offset = 0,
                            ShaderLocation = 0
                        },
                        new GpuVertexAttribute()
                        {
                            Format = GpuVertexFormat.Float2,
                            Offset = 2*4,
                            ShaderLocation = 1
                        }
                    })
                    {
                        StepMode = GpuInputStepMode.Instance
                    },
                    new GpuVertexBufferLayout(2*4, new GpuVertexAttribute[]
                    {
                        new GpuVertexAttribute()
                        {
                            Format = GpuVertexFormat.Float2,
                            Offset = 0,
                            ShaderLocation = 2
                        }
                    })
                }
            })
            {
                Fragment = new GpuFragmentState(drawShader, "PSMain", new GpuColorTargetState[] { new GpuColorTargetState {Format = GpuTextureFormat.BGRA8UNorm, Blend = null, WriteMask = GpuColorWriteFlags.All } }),
                Primitive = new GpuPrimitiveState { Topology = GpuPrimitiveTopology.TriangleList, CullMode = GpuCullMode.None, FrontFace = GpuFrontFace.Ccw},
                DepthStencilState = new GpuDepthStencilState(GpuTextureFormat.Depth24PlusStencil8)
                {
                    DepthWriteEnabled = true,
                    DepthCompare = GpuCompareFunction.Less,
                }
            });
            var computeBindGroupLayout = Device.CreateBindGroupLayout(new GpuBindGroupLayoutDescriptor(new GpuBindGroupLayoutEntry[]
            {
                new GpuBindGroupLayoutEntry()
                {
                    Binding = 0,
                    Visibility = GpuShaderStageFlags.Compute,
                    Buffer = new GpuBufferBindingLayout()
                    {
                        Type = GpuBufferBindingType.Uniform,
                        HasDynamicOffset = false,
                        MinBindingSize = (ulong)(SimParamData.Length * sizeof(float))
                    }
                },
                new GpuBindGroupLayoutEntry()
                {
                    Binding = 1,
                    Visibility = GpuShaderStageFlags.Compute,
                    Buffer = new GpuBufferBindingLayout()
                    {
                        Type = GpuBufferBindingType.ReadOnlyStorage,
                        HasDynamicOffset = false,
                        MinBindingSize = NumParticles * 16
                    }
                },
                new GpuBindGroupLayoutEntry()
                {
                    Binding = 2,
                    Visibility = GpuShaderStageFlags.Compute,
                    Buffer = new GpuBufferBindingLayout()
                    {
                        Type = GpuBufferBindingType.Storage,
                        HasDynamicOffset = false,
                        MinBindingSize = NumParticles * 16
                    }
                }
            }));
            ComputePipeline = Device.CreateComputePipeline(new GpuComputePipelineDescriptor(new GpuProgrammableStage(computeShader, "main"))
            {
                Layout = Device.CreatePipelineLayout(new GpuPipelineLayoutDescriptor() 
                {
                    BindGroupLayouts = new GpuBindGroupLayout[] { computeBindGroupLayout } 
                }),
            });
            
            VerticesBuffer = Device.CreateBuffer(new GpuBufferDescriptor((ulong)(sizeof(float) * VertexBufferData.Length), GpuBufferUsageFlags.Vertex)
            {
                MappedAtCreation = true
            });
            using (var stream = VerticesBuffer.MappedRange().AsStream())
            using (var binaryWriter = new BinaryWriter(stream))
            {
                for (int i = 0; i < VertexBufferData.Length; ++i)
                {
                    binaryWriter.Write(VertexBufferData[i]);
                }
            }
            VerticesBuffer.Unmap();

            var simParamBuffer = Device.CreateBuffer(new GpuBufferDescriptor((ulong)(sizeof(float) * SimParamData.Length), GpuBufferUsageFlags.Uniform) 
            { 
                MappedAtCreation = true
            });

            using(var stream = simParamBuffer.MappedRange().AsStream())
            using(var writer = new BinaryWriter(stream))
            {
                for(int i = 0; i < SimParamData.Length;++i)
                {
                    writer.Write(SimParamData[i]);

                }
            }
            simParamBuffer.Unmap();

            float[] initialParticleData = new float[NumParticles * 4];
            Random random = new Random();
            for(var i = 0; i < NumParticles; ++i)
            {
                initialParticleData[4 * i + 0] = (float)(2 * (random.NextDouble() - 0.5f));
                initialParticleData[4 * i + 1] = (float)(2 * (random.NextDouble() - 0.5f));
                initialParticleData[4 * i + 2] = (float)(2 * (random.NextDouble() - 0.5f) * 0.1);
                initialParticleData[4 * i + 3] = (float)(2 * (random.NextDouble() - 0.5f) * 0.1);
            }
            Windows.Storage.Streams.Buffer initialParticleDataBuffer = new Windows.Storage.Streams.Buffer((uint)(sizeof(float) * initialParticleData.Length))
            {
                Length = (uint)(sizeof(float) * initialParticleData.Length)
            };
            using(var stream = initialParticleDataBuffer.AsStream())
            using(var writer = new BinaryWriter(stream))
            {
                for (int i = 0; i < initialParticleData.Length; ++i)
                {
                    writer.Write(initialParticleData[i]);
                }
            }
            
            ParticleBuffers = new GpuBuffer[2];
            for(int i = 0; i < 2; ++i)
            {
                ParticleBuffers[i] = Device.CreateBuffer(new GpuBufferDescriptor(initialParticleDataBuffer.Length, GpuBufferUsageFlags.Vertex | GpuBufferUsageFlags.Storage) {MappedAtCreation = true });
                initialParticleDataBuffer.CopyTo(ParticleBuffers[i].MappedRange());
                ParticleBuffers[i].Unmap();
            }
            ParticleBindGroups = new GpuBindGroup[2];
            for(var i = 0; i < 2;++i)
            {
                ParticleBindGroups[i] = Device.CreateBindGroup(new GpuBindGroupDescriptor(computeBindGroupLayout, new GpuBindGroupEntry[]
                {
                    new GpuBindGroupEntry(0, new GpuBufferBinding(simParamBuffer, simParamBuffer.Size)),
                    new GpuBindGroupEntry(1, new GpuBufferBinding(ParticleBuffers[i], ParticleBuffers[i].Size)),
                    new GpuBindGroupEntry(2, new GpuBufferBinding(ParticleBuffers[(i + 1) % 2], ParticleBuffers[(i + 1) % 2].Size))
                }));
            }
            T = 0;
        }
        void DrawFrame()
        {
            var renderPassDescriptor = new GpuRenderPassDescriptor(new GpuRenderPassColorAttachment[] 
            { 
                new GpuRenderPassColorAttachment(SwapChain.GetCurrentTexture().CreateView(), new GpuColorDict { R = 0, G = 0, B = 0, A = 0 }) 
            });
            renderPassDescriptor.DepthStencilAttachment = new GpuRenderPassDepthStencilAttachment(DepthTexture.CreateView(), 1.0f, GpuStoreOp.Store, null, GpuStoreOp.Store);
            var commandEncoder = Device.CreateCommandEncoder();
            var cpass = commandEncoder.BeginComputePass();
            cpass.SetPipeline(ComputePipeline);
            cpass.SetBindGroup(0, ParticleBindGroups[T % 2]);
            cpass.Dispatch(NumParticles, 1, 1);
            cpass.EndPass();
            var rpass = commandEncoder.BeginRenderPass(renderPassDescriptor);
            rpass.SetPipeline(RenderPipeline);
            rpass.SetVertexBuffer(0, ParticleBuffers[(T + 1) % 2], 0, ParticleBuffers[(T + 1) % 2].Size);
            rpass.SetVertexBuffer(1, VerticesBuffer, 0, VerticesBuffer.Size);
            rpass.Draw(3, NumParticles, 0, 0);
            rpass.EndPass();
            Device.DefaultQueue.Sumit(new GpuCommandBuffer[] { commandEncoder.Finish() });
            SwapChain.Present();
            ++T;
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
                    SwapChainDescriptor = new GpuSwapChainDescriptor(GpuTextureFormat.BGRA8UNorm, (uint)GpuView.Width, (uint)GpuView.Height);
                    SwapChain = Device.ConfigureSwapChainForSwapChainPanel(SwapChainDescriptor, GpuView);
                    DepthTexture = Device.CreateTexture(new GpuTextureDescriptor(new GpuExtend3DDict { Width = SwapChainDescriptor.Width, Height = SwapChainDescriptor.Height, Depth = 1 }, GpuTextureFormat.Depth24PlusStencil8, GpuTextureUsageFlags.OutputAttachment));
                }
                DrawFrame();
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
            DepthTexture = null;
        }
    }
}
