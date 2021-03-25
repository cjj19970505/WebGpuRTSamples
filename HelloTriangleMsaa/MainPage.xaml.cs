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

namespace HelloTriangleMsaa
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainPage : Page
    {
        public Gpu Gpu { get; }
        GpuDevice Device { get; set; }
        GpuRenderPipeline Pipeline { get; set; }
        GpuSwapChainDescriptor SwapChainDescriptor { get; set; }
        GpuSwapChain SwapChain { get; set; }
        GpuTextureView Attachment { get; set; }
        GpuTextureFormat SwapChainFormat { get { return GpuTextureFormat.BGRA8UNorm; } }
        UInt32 SampleCount { get { return 4; } }
        public MainPage()
        {
            Gpu = new Gpu();
            Gpu.EnableD3D12DebugLayer();
            this.InitializeComponent();
            GpuView.Width = Window.Current.Bounds.Width;
            GpuView.Height = Window.Current.Bounds.Height;
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
                    var texture = Device.CreateTexture(new GpuTextureDescriptor(new GpuExtend3DDict { Width = SwapChainDescriptor.Width, Height = SwapChainDescriptor.Height, Depth = 1 }, SwapChainFormat, GpuTextureUsageFlags.OutputAttachment) { SampleCount = SampleCount });
                    Attachment = texture.CreateView();
                }
                DrawFrame();
                var fenceValueWaitFor = ++currentFenceValue;
                Device.DefaultQueue.Signal(fence, fenceValueWaitFor);
                await fence.OnCompletionAsync(fenceValueWaitFor);
            }
        }

        private void Page_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            SwapChainDescriptor = null;
            SwapChain = null;
            GpuView.Width = Window.Current.Bounds.Width;
            GpuView.Height = Window.Current.Bounds.Height;
        }

        async Task Init()
        {
            var adapter = await Gpu.RequestAdapterAsync();
            Device = await adapter.RequestDeviceAsync();

            string shaderCode;
            using (var shaderFileStream = typeof(MainPage).Assembly.GetManifestResourceStream("HelloTriangleMsaa.shader.hlsl"))
            using (var shaderStreamReader = new StreamReader(shaderFileStream))
            {
                shaderCode = shaderStreamReader.ReadToEnd();
            }
            Pipeline = Device.CreateRenderPipeline(new GpuRenderPipelineDescriptor(
                new GpuVertexState(Device.CreateShaderModule(new GpuShaderModuleDescriptor(GpuShaderSourceType.Hlsl, shaderCode)), "VSMain"))
            {
                Fragment = new GpuFragmentState(Device.CreateShaderModule(new GpuShaderModuleDescriptor(GpuShaderSourceType.Hlsl, shaderCode)), "PSMain", new GpuColorTargetState[] { new GpuColorTargetState() { Format = SwapChainFormat, Blend = null, WriteMask = GpuColorWriteFlags.All } }),
                Primitive = new GpuPrimitiveState()
                {
                    Topology = GpuPrimitiveTopology.TriangleList,
                    FrontFace = GpuFrontFace.Ccw,
                    CullMode = GpuCullMode.None,
                    StripIndexFormat = null
                },
                Multisample = new GpuMultisampleState()
                {
                     Count = SampleCount,
                     AlphaToCoverageEnabled = false,
                     Mask = 0xFFFFFFFF
                }
            });
            


        }
        void DrawFrame()
        {
            var encoder = Device.CreateCommandEncoder();
            var renderpassDescriptor = new GpuRenderPassDescriptor(new GpuRenderPassColorAttachment[]
            {
                new GpuRenderPassColorAttachment(Attachment, new GpuColorDict{R = 0, G = 0, B = 0, A = 1 })
                {
                    ResolveTarget = SwapChain.GetCurrentTexture().CreateView()
                }
            });
            var passEncoder = encoder.BeginRenderPass(renderpassDescriptor);
            passEncoder.SetPipeline(Pipeline);
            passEncoder.Draw(3, 1, 0, 0);
            passEncoder.EndPass();
            Device.DefaultQueue.Sumit(new GpuCommandBuffer[] { encoder.Finish() });
            SwapChain.Present();
        }
    }
}
