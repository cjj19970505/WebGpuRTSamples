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
using Meshes;
using System.Numerics;

// The Blank Page item template is documented at https://go.microsoft.com/fwlink/?LinkId=402352&clcid=0x409

namespace InstancedCube
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainPage : Page
    {
        const UInt32 XCount = 4;
        const UInt32 YCount = 4;
        const UInt32 NumInstances = XCount * YCount;
        const UInt32 MatrixFloatCount = 16; // 4x4 matrix
        const UInt32 MatrixSize = 4 * MatrixFloatCount;
        const UInt32 UniformBufferSize = NumInstances * MatrixSize; // Matrix4x4
        const float Step = 4.0f;
        readonly DateTime StartDateTime = DateTime.Now;
        GpuDevice Device { get; set; }
        GpuSwapChainDescriptor SwapChainDescriptor { get; set; }
        GpuSwapChain SwapChain { set; get; }
        GpuTexture DepthTexture { set; get; }
        GpuBuffer UniformBuffer { get; set; }
        Windows.Storage.Streams.IBuffer UniformCpuBuffer { get; set; }
        GpuRenderPipeline Pipeline { get; set; }
        GpuBindGroup UniformBindGroup { get; set; }
        GpuBuffer VerticesBuffer { get; set; }
        Matrix4x4[] MvpMatrices { get; set; }
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
            Device = await (await gpu.RequestAdapterAsync()).RequestDeviceAsync();
            Windows.Storage.Streams.Buffer verticeCpuBuffer = new Windows.Storage.Streams.Buffer((uint)Buffer.ByteLength(Cube.CubeVertexArray));
            verticeCpuBuffer.Length = verticeCpuBuffer.Capacity;
            using (var verticeCpuStream = verticeCpuBuffer.AsStream())
            {
                byte[] vertexBufferBytes = new byte[Buffer.ByteLength(Cube.CubeVertexArray)];
                Buffer.BlockCopy(Cube.CubeVertexArray, 0, vertexBufferBytes, 0, Buffer.ByteLength(Cube.CubeVertexArray));
                await verticeCpuStream.WriteAsync(vertexBufferBytes, 0, Buffer.ByteLength(Cube.CubeVertexArray));
            }
            VerticesBuffer = Device.CreateBuffer(new GpuBufferDescriptor((ulong)Buffer.ByteLength(Cube.CubeVertexArray), GpuBufferUsageFlags.Vertex)
            {
                MappedAtCreation = true
            });
            verticeCpuBuffer.CopyTo(VerticesBuffer.MappedRange());
            VerticesBuffer.Unmap();

            string shaderCode;
            using (var shaderFileStream = typeof(MainPage).Assembly.GetManifestResourceStream("InstancedCube.shader.hlsl"))
            using (var shaderStreamReader = new StreamReader(shaderFileStream))
            {
                shaderCode = shaderStreamReader.ReadToEnd();
            }
            var shader = Device.CreateShaderModule(new GpuShaderModuleDescriptor(GpuShaderSourceType.Hlsl, shaderCode));
            var vertexState = new GpuVertexState(shader, "VSMain")
            {
                VertexBuffers = new GpuVertexBufferLayout[] { new GpuVertexBufferLayout(Cube.CubeVertexSize, new GpuVertexAttribute[]
                {
                    new GpuVertexAttribute()
                    {
                        ShaderLocation = 0,
                        Format = GpuVertexFormat.Float4,
                        Offset = Cube.CubePositionOffset
                    },
                    new GpuVertexAttribute()
                    {
                        ShaderLocation = 1,
                        Format = GpuVertexFormat.Float4,
                        Offset = Cube.CubeColorOffset
                    }

                })  }
            };
            var fragmentState = new GpuFragmentState(shader, "PSMain", new GpuColorTargetState[] { new GpuColorTargetState { Format = GpuTextureFormat.BGRA8UNorm, Blend = null, WriteMask = GpuColorWriteFlags.All } });
            var primitiveState = new GpuPrimitiveState
            {
                Topology = GpuPrimitiveTopology.TriangleList,
                FrontFace = GpuFrontFace.Ccw,
                CullMode = GpuCullMode.Back,
                StripIndexFormat = null
            };
            var depthState = new GpuDepthStencilState(GpuTextureFormat.Depth24PlusStencil8)
            {
                DepthWriteEnabled = true,
                DepthCompare = GpuCompareFunction.Less,
            };
            var uniformBindGroupLayout = Device.CreateBindGroupLayout(new GpuBindGroupLayoutDescriptor(new GpuBindGroupLayoutEntry[]
            {
                new GpuBindGroupLayoutEntry()
                {
                    Binding = 0,
                    Visibility = GpuShaderStageFlags.Vertex,
                    Buffer = new GpuBufferBindingLayout
                    {
                        Type = GpuBufferBindingType.Uniform,
                        HasDynamicOffset = false,
                        MinBindingSize = UniformBufferSize
                    }
                }
            }));
            var pipelineLayout = Device.CreatePipelineLayout(new GpuPipelineLayoutDescriptor()
            {
                BindGroupLayouts = new GpuBindGroupLayout[]
                {
                    uniformBindGroupLayout
                }
            });
            Pipeline = Device.CreateRenderPipeline(new GpuRenderPipelineDescriptor(vertexState)
            {
                Fragment = fragmentState,
                Primitive = primitiveState,
                DepthStencilState = depthState,
                Layout = pipelineLayout
            });
            UniformBuffer = Device.CreateBuffer(new GpuBufferDescriptor(UniformBufferSize, GpuBufferUsageFlags.Uniform | GpuBufferUsageFlags.CopyDst));
            UniformCpuBuffer = new Windows.Storage.Streams.Buffer(UniformBufferSize);
            UniformCpuBuffer.Length = UniformCpuBuffer.Capacity;
            UniformBindGroup = Device.CreateBindGroup(new GpuBindGroupDescriptor(uniformBindGroupLayout, new GpuBindGroupEntry[]
            {
                new GpuBindGroupEntry(0, new GpuBufferBinding(UniformBuffer, UniformBufferSize))
            }));
            MvpMatrices = new Matrix4x4[NumInstances];
        }

        void DrawFrame()
        {
            UpdateTransformationMatrix();
            WriteMatricesToBuffer(UniformCpuBuffer, MvpMatrices);
            Device.DefaultQueue.WriteBuffer(UniformBuffer, 0, UniformCpuBuffer);

            GpuRenderPassDescriptor renderPassDescriptor = new GpuRenderPassDescriptor(new GpuRenderPassColorAttachment[] { new GpuRenderPassColorAttachment(SwapChain.GetCurrentTexture().CreateView(), new GpuColorDict { R = 0.5f, G = 0.5f, B = 0.5f, A = 1.0f }) })
            {
                DepthStencilAttachment = new GpuRenderPassDepthStencilAttachment(DepthTexture.CreateView(), 1.0f, GpuStoreOp.Store, null, GpuStoreOp.Store)
            };
            var commandEncoder = Device.CreateCommandEncoder();
            var passEncoder = commandEncoder.BeginRenderPass(renderPassDescriptor);
            passEncoder.SetPipeline(Pipeline);
            passEncoder.SetBindGroup(0, UniformBindGroup);
            passEncoder.SetVertexBuffer(0, VerticesBuffer, 0, VerticesBuffer.Size);
            passEncoder.Draw(36, NumInstances, 0, 0);
            passEncoder.EndPass();
            Device.DefaultQueue.Sumit(new GpuCommandBuffer[] { commandEncoder.Finish() });
            SwapChain.Present();
        }

        void UpdateTransformationMatrix()
        {
            var now = (float)((DateTime.Now - StartDateTime).TotalMilliseconds / 1000);
            int m = 0;
            var view = Matrix4x4.CreateTranslation(new Vector3(0, 0, -12));
            var persepctive = Matrix4x4.CreatePerspectiveFieldOfView(2 * MathF.PI / 5, (float)SwapChainDescriptor.Width / (float)SwapChainDescriptor.Height, 1, 100);
            for (var x = 0; x < XCount;++x)
            {
                for (var y = 0; y < YCount; ++y)
                {
                    var model = Matrix4x4.CreateTranslation(Step * (x - (float)XCount / 2 + 0.5f), Step * (y - (float)YCount / 2 + 0.5f), 0);
                    model = Matrix4x4.CreateFromAxisAngle(Vector3.Normalize(new Vector3(MathF.Sin((x + 0.5f) * now), MathF.Cos((y + 0.5f) * now), 0)), 1) * model;
                    MvpMatrices[m] = model * view * persepctive;
                    ++m;
                }
            }
        }

        void WriteMatricesToBuffer(Windows.Storage.Streams.IBuffer buffer, Matrix4x4[] mats)
        {
            using (var stream = buffer.AsStream())
            using (var writer = new BinaryWriter(stream))
            {
                foreach(var mat in mats)
                {
                    writer.Write(mat.M11);
                    writer.Write(mat.M12);
                    writer.Write(mat.M13);
                    writer.Write(mat.M14);

                    writer.Write(mat.M21);
                    writer.Write(mat.M22);
                    writer.Write(mat.M23);
                    writer.Write(mat.M24);

                    writer.Write(mat.M31);
                    writer.Write(mat.M32);
                    writer.Write(mat.M33);
                    writer.Write(mat.M34);

                    writer.Write(mat.M41);
                    writer.Write(mat.M42);
                    writer.Write(mat.M43);
                    writer.Write(mat.M44);
                }
                
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
    }
}
