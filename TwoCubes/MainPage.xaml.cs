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
using Meshes;
using System.Threading.Tasks;
using System.Numerics;
// The Blank Page item template is documented at https://go.microsoft.com/fwlink/?LinkId=402352&clcid=0x409

namespace TwoCubes
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainPage : Page
    {
        readonly DateTime StartDateTime = DateTime.Now;

        static readonly UInt32 MatrixSize = 4 * 16;
        static readonly UInt32 Offset = 256;
        static readonly UInt32 UniformBufferSize = Offset + MatrixSize;
        GpuDevice Device { get; set; }
        GpuBuffer VerticesBuffer { get; set; }
        GpuRenderPipeline Pipeline { get; set; }
        GpuBuffer UniformBuffer { get; set; }
        Windows.Storage.Streams.IBuffer UniformCpuBuffer { get; set; }
        GpuBindGroup UniformBindGroup1 { get; set; }
        GpuBindGroup UniformBindGroup2 { get; set; }

        GpuSwapChainDescriptor SwapChainDescriptor { get; set; }
        GpuSwapChain SwapChain { set; get; }
        GpuTexture DepthTexture { set; get; }

        public MainPage()
        {
            this.InitializeComponent();
            GpuView.Width = Window.Current.Bounds.Height;
            GpuView.Height = Window.Current.Bounds.Width;
        }

        void WriteMatrixToBuffer(Windows.Storage.Streams.IBuffer buffer ,Matrix4x4 mat)
        {
            using (var stream = buffer.AsStream())
            using (var writer = new BinaryWriter(stream))
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

        async Task Init()
        {
            var gpu = new Gpu();
#if DEBUG
            gpu.EnableD3D12DebugLayer();
#endif
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
            using (var shaderFileStream = typeof(MainPage).Assembly.GetManifestResourceStream("TwoCubes.shader.hlsl"))
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
                        MinBindingSize = MatrixSize
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
            UniformCpuBuffer = new Windows.Storage.Streams.Buffer(MatrixSize);
            UniformCpuBuffer.Length = UniformCpuBuffer.Capacity;
            UniformBindGroup1 = Device.CreateBindGroup(new GpuBindGroupDescriptor(uniformBindGroupLayout, new GpuBindGroupEntry[]
            {
                new GpuBindGroupEntry(0, new GpuBufferBinding(UniformBuffer, MatrixSize){Offset = 0 })
            }));
            UniformBindGroup2 = Device.CreateBindGroup(new GpuBindGroupDescriptor(uniformBindGroupLayout, new GpuBindGroupEntry[]
            {
                new GpuBindGroupEntry(0, new GpuBufferBinding(UniformBuffer, MatrixSize){Offset = Offset })
            }));
        }

        (Matrix4x4, Matrix4x4) GetTransformationMatrixs()
        {
            var now = (float)(DateTime.Now - StartDateTime).TotalMilliseconds;
            var viewMatrix = Matrix4x4.CreateTranslation(0, 0, -7);
            var modelMatrix1 = Matrix4x4.CreateFromAxisAngle(new Vector3(MathF.Sin(now / 1000.0f), MathF.Cos(now / 1000.0f), 0), 1) * Matrix4x4.CreateTranslation(-2, 0, 0);
            var modelMatrix2 = Matrix4x4.CreateFromAxisAngle(new Vector3(MathF.Sin(now / 1000.0f), MathF.Cos(now / 1000.0f), 0), 1) * Matrix4x4.CreateTranslation(2, 0, 0);
            var persepctive = Matrix4x4.CreatePerspectiveFieldOfView(2 * MathF.PI / 5, (float)SwapChainDescriptor.Width / (float)SwapChainDescriptor.Height, 1, 100);
            var mvp1 = modelMatrix1 * viewMatrix * persepctive;
            var mvp2 = modelMatrix2 * viewMatrix * persepctive;
            return (mvp1, mvp2);
        }

        void DrawFrame()
        {
            (Matrix4x4 modelViewProjectionMatrix1, Matrix4x4 modelViewProjectionMatrix2) = GetTransformationMatrixs();
            WriteMatrixToBuffer(UniformCpuBuffer, modelViewProjectionMatrix1);
            Device.DefaultQueue.WriteBuffer(UniformBuffer, 0, UniformCpuBuffer);
            WriteMatrixToBuffer(UniformCpuBuffer, modelViewProjectionMatrix2);
            Device.DefaultQueue.WriteBuffer(UniformBuffer, Offset, UniformCpuBuffer);

            GpuRenderPassDescriptor renderPassDescriptor = new GpuRenderPassDescriptor(new GpuRenderPassColorAttachment[] { new GpuRenderPassColorAttachment(SwapChain.GetCurrentTexture().CreateView(), new GpuColorDict { R = 0.5f, G = 0.5f, B = 0.5f, A = 1.0f }) })
            {
                DepthStencilAttachment = new GpuRenderPassDepthStencilAttachment(DepthTexture.CreateView(), 1.0f, GpuStoreOp.Store, null, GpuStoreOp.Store)
            };
            var commandEncoder = Device.CreateCommandEncoder();
            var passEncoder = commandEncoder.BeginRenderPass(renderPassDescriptor);
            passEncoder.SetPipeline(Pipeline);
            passEncoder.SetVertexBuffer(0, VerticesBuffer, 0, VerticesBuffer.Size);
            passEncoder.SetBindGroup(0, UniformBindGroup1);
            passEncoder.Draw(36, 1, 0, 0);
            passEncoder.SetBindGroup(1, UniformBindGroup2);
            passEncoder.Draw(36, 1, 0, 0);
            passEncoder.EndPass();
            Device.DefaultQueue.Sumit(new GpuCommandBuffer[] { commandEncoder.Finish() });
            SwapChain.Present();
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
