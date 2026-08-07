using System.Numerics;
using System.Runtime.InteropServices;
using Silk.NET.Core.Native;
using Silk.NET.WebGPU;
using Silk.NET.WebGPU.Extensions.WGPU;
using SwiftDotNet.Graphics;
using Buffer = Silk.NET.WebGPU.Buffer;

namespace SwiftDotNet;

/// <summary>
/// Owns the GPU device and turns a recorded <see cref="WebGpuCanvas"/> into draw calls.
/// </summary>
/// <remarks>
/// <para>The whole UI is one instanced draw per image batch: instance data lives in a storage buffer, and
/// the shader resolves each instance into a shape. There are no per-shape pipeline switches, no vertex
/// buffers, and no scissor changes — clipping rides on the instance.</para>
///
/// <para>The device is created without a surface, so this class works headless (render to a texture and
/// read the pixels back) as readily as it does against a swapchain. That is what makes the backend
/// testable: <see cref="RenderToRgba"/> is used by the test suite to assert real GPU output.</para>
/// </remarks>
public sealed unsafe class WebGpuRenderer : IDisposable
{
    const TextureFormat Format = TextureFormat.Rgba8Unorm;

    readonly WebGPU _wgpu;
    readonly Wgpu _wgpuNative;
    readonly Silk.NET.WebGPU.Instance* _instance;
    readonly Adapter* _adapter;
    readonly Device* _device;
    readonly Queue* _queue;

    RenderPipeline* _pipeline;
    BindGroupLayout* _frameLayout;
    BindGroupLayout* _textureLayout;
    readonly Sampler* _sampler;

    Buffer* _uniforms;
    Buffer* _instanceBuffer;
    Buffer* _stopBuffer;
    ulong _instanceCapacity;
    ulong _stopCapacity;

    Texture* _atlasTexture;
    TextureView* _atlasView;
    BindGroup* _atlasGroup;

    Texture* _whiteTexture;
    TextureView* _whiteView;
    BindGroup* _whiteGroup;

    readonly Dictionary<nint, nint> _imageGroups = new();

    public WebGpuRenderer()
    {
        _wgpu = WebGPU.GetApi();
        _wgpuNative = new Wgpu(_wgpu.Context);

        var instanceDescriptor = new InstanceDescriptor();
        _instance = _wgpu.CreateInstance(in instanceDescriptor);

        Adapter* adapter = null;
        var adapterOptions = new RequestAdapterOptions { PowerPreference = PowerPreference.HighPerformance };
        _wgpu.InstanceRequestAdapter(_instance, in adapterOptions,
            new PfnRequestAdapterCallback((status, result, message, _) =>
            {
                if (status != RequestAdapterStatus.Success)
                    throw new InvalidOperationException($"WebGPU adapter request failed: {SilkMarshal.PtrToString((nint)message)}");
                adapter = result;
            }), null);
        _adapter = adapter is null ? throw new InvalidOperationException("No WebGPU adapter available.") : adapter;

        Device* device = null;
        _wgpu.AdapterRequestDevice(_adapter, null,
            new PfnRequestDeviceCallback((status, result, message, _) =>
            {
                if (status != RequestDeviceStatus.Success)
                    throw new InvalidOperationException($"WebGPU device request failed: {SilkMarshal.PtrToString((nint)message)}");
                device = result;
            }), null);
        _device = device is null ? throw new InvalidOperationException("No WebGPU device available.") : device;

        _queue = _wgpu.DeviceGetQueue(_device);

        _sampler = CreateSampler();
        CreatePipeline();

        _uniforms = CreateBuffer((ulong)sizeof(Vector4), BufferUsage.Uniform | BufferUsage.CopyDst);
        CreateSolidTexture(255, 255, 255, 255, out _whiteTexture, out _whiteView);
        _whiteGroup = CreateTextureGroup(_whiteView);
    }

    /// <summary>The backend name wgpu resolved to (Metal, Vulkan, D3D12, …). Useful in diagnostics.</summary>
    public string BackendName
    {
        get
        {
            var props = new AdapterProperties();
            _wgpu.AdapterGetProperties(_adapter, ref props);
            return props.BackendType.ToString();
        }
    }

    // ---- setup ---------------------------------------------------------------

    Sampler* CreateSampler()
    {
        var descriptor = new SamplerDescriptor
        {
            AddressModeU = AddressMode.ClampToEdge,
            AddressModeV = AddressMode.ClampToEdge,
            AddressModeW = AddressMode.ClampToEdge,
            MagFilter = FilterMode.Linear,
            MinFilter = FilterMode.Linear,
            MipmapFilter = MipmapFilterMode.Linear,
            LodMaxClamp = 1f,
            MaxAnisotropy = 1,
        };
        return _wgpu.DeviceCreateSampler(_device, in descriptor);
    }

    void CreatePipeline()
    {
        // group 0: viewport uniform + instance/stop storage buffers.
        var frameEntries = stackalloc BindGroupLayoutEntry[3];
        frameEntries[0] = new BindGroupLayoutEntry
        {
            Binding = 0,
            Visibility = ShaderStage.Vertex | ShaderStage.Fragment,
            Buffer = new BufferBindingLayout { Type = BufferBindingType.Uniform },
        };
        frameEntries[1] = new BindGroupLayoutEntry
        {
            Binding = 1,
            Visibility = ShaderStage.Vertex | ShaderStage.Fragment,
            Buffer = new BufferBindingLayout { Type = BufferBindingType.ReadOnlyStorage },
        };
        frameEntries[2] = new BindGroupLayoutEntry
        {
            Binding = 2,
            Visibility = ShaderStage.Fragment,
            Buffer = new BufferBindingLayout { Type = BufferBindingType.ReadOnlyStorage },
        };

        var frameLayoutDescriptor = new BindGroupLayoutDescriptor { EntryCount = 3, Entries = frameEntries };
        _frameLayout = _wgpu.DeviceCreateBindGroupLayout(_device, in frameLayoutDescriptor);

        // groups 1 and 2 share a shape: a sampled texture plus its sampler (glyph atlas, then image).
        var textureEntries = stackalloc BindGroupLayoutEntry[2];
        textureEntries[0] = new BindGroupLayoutEntry
        {
            Binding = 0,
            Visibility = ShaderStage.Fragment,
            Texture = new TextureBindingLayout
            {
                SampleType = TextureSampleType.Float,
                ViewDimension = TextureViewDimension.Dimension2D,
            },
        };
        textureEntries[1] = new BindGroupLayoutEntry
        {
            Binding = 1,
            Visibility = ShaderStage.Fragment,
            Sampler = new SamplerBindingLayout { Type = SamplerBindingType.Filtering },
        };

        var textureLayoutDescriptor = new BindGroupLayoutDescriptor { EntryCount = 2, Entries = textureEntries };
        _textureLayout = _wgpu.DeviceCreateBindGroupLayout(_device, in textureLayoutDescriptor);

        var layouts = stackalloc BindGroupLayout*[3] { _frameLayout, _textureLayout, _textureLayout };
        var pipelineLayoutDescriptor = new PipelineLayoutDescriptor { BindGroupLayoutCount = 3, BindGroupLayouts = layouts };
        var pipelineLayout = _wgpu.DeviceCreatePipelineLayout(_device, in pipelineLayoutDescriptor);

        var module = CreateShaderModule(Shaders.Wgsl);

        var blend = new BlendState
        {
            // Premultiplied alpha: the fragment shader already multiplied colour by coverage.
            Color = new BlendComponent
            {
                SrcFactor = BlendFactor.One,
                DstFactor = BlendFactor.OneMinusSrcAlpha,
                Operation = BlendOperation.Add,
            },
            Alpha = new BlendComponent
            {
                SrcFactor = BlendFactor.One,
                DstFactor = BlendFactor.OneMinusSrcAlpha,
                Operation = BlendOperation.Add,
            },
        };

        var colorTarget = new ColorTargetState
        {
            Format = Format,
            Blend = &blend,
            WriteMask = ColorWriteMask.All,
        };

        var fsName = (byte*)SilkMarshal.StringToPtr("fs");
        var vsName = (byte*)SilkMarshal.StringToPtr("vs");

        var fragment = new FragmentState
        {
            Module = module,
            EntryPoint = fsName,
            TargetCount = 1,
            Targets = &colorTarget,
        };

        var descriptor = new RenderPipelineDescriptor
        {
            Layout = pipelineLayout,
            Vertex = new VertexState { Module = module, EntryPoint = vsName, BufferCount = 0 },
            Primitive = new PrimitiveState
            {
                Topology = PrimitiveTopology.TriangleList,
                CullMode = CullMode.None,
            },
            Multisample = new MultisampleState { Count = 1, Mask = uint.MaxValue },
            Fragment = &fragment,
        };

        _pipeline = _wgpu.DeviceCreateRenderPipeline(_device, in descriptor);

        SilkMarshal.Free((nint)fsName);
        SilkMarshal.Free((nint)vsName);
    }

    ShaderModule* CreateShaderModule(string wgsl)
    {
        var code = (byte*)SilkMarshal.StringToPtr(wgsl);
        var wgslDescriptor = new ShaderModuleWGSLDescriptor
        {
            Chain = new ChainedStruct { SType = SType.ShaderModuleWgslDescriptor },
            Code = code,
        };
        var descriptor = new ShaderModuleDescriptor { NextInChain = (ChainedStruct*)&wgslDescriptor };
        var module = _wgpu.DeviceCreateShaderModule(_device, in descriptor);
        SilkMarshal.Free((nint)code);
        return module;
    }

    Buffer* CreateBuffer(ulong size, BufferUsage usage)
    {
        var descriptor = new BufferDescriptor { Size = size, Usage = usage };
        return _wgpu.DeviceCreateBuffer(_device, in descriptor);
    }

    void CreateSolidTexture(byte r, byte g, byte b, byte a, out Texture* texture, out TextureView* view)
    {
        var descriptor = new TextureDescriptor
        {
            Size = new Extent3D(1, 1, 1),
            MipLevelCount = 1,
            SampleCount = 1,
            Dimension = TextureDimension.Dimension2D,
            Format = Format,
            Usage = TextureUsage.TextureBinding | TextureUsage.CopyDst,
        };
        texture = _wgpu.DeviceCreateTexture(_device, in descriptor);
        view = _wgpu.TextureCreateView(texture, null);

        var pixel = stackalloc byte[4] { r, g, b, a };
        var destination = new ImageCopyTexture { Texture = texture, MipLevel = 0, Aspect = TextureAspect.All };
        var layout = new TextureDataLayout { BytesPerRow = 4, RowsPerImage = 1 };
        var extent = new Extent3D(1, 1, 1);
        _wgpu.QueueWriteTexture(_queue, in destination, pixel, 4, in layout, in extent);
    }

    BindGroup* CreateTextureGroup(TextureView* view)
    {
        var entries = stackalloc BindGroupEntry[2];
        entries[0] = new BindGroupEntry { Binding = 0, TextureView = view };
        entries[1] = new BindGroupEntry { Binding = 1, Sampler = _sampler };

        var descriptor = new BindGroupDescriptor { Layout = _textureLayout, EntryCount = 2, Entries = entries };
        return _wgpu.DeviceCreateBindGroup(_device, in descriptor);
    }

    // ---- per-frame -----------------------------------------------------------

    void EnsureAtlas(GlyphAtlas atlas)
    {
        if (_atlasTexture is null)
        {
            var descriptor = new TextureDescriptor
            {
                Size = new Extent3D((uint)atlas.Width, (uint)atlas.Height, 1),
                MipLevelCount = 1,
                SampleCount = 1,
                Dimension = TextureDimension.Dimension2D,
                Format = TextureFormat.R8Unorm,
                Usage = TextureUsage.TextureBinding | TextureUsage.CopyDst,
            };
            _atlasTexture = _wgpu.DeviceCreateTexture(_device, in descriptor);
            _atlasView = _wgpu.TextureCreateView(_atlasTexture, null);
            _atlasGroup = CreateTextureGroup(_atlasView);
        }

        if (!atlas.Dirty) return;

        fixed (byte* pixels = atlas.Pixels)
        {
            var destination = new ImageCopyTexture { Texture = _atlasTexture, MipLevel = 0, Aspect = TextureAspect.All };
            var layout = new TextureDataLayout { BytesPerRow = (uint)atlas.Width, RowsPerImage = (uint)atlas.Height };
            var extent = new Extent3D((uint)atlas.Width, (uint)atlas.Height, 1);
            _wgpu.QueueWriteTexture(_queue, in destination, pixels, (nuint)atlas.Pixels.Length, in layout, in extent);
        }
        atlas.Dirty = false;
    }

    BindGroup* GroupFor(WebGpuImage? image)
    {
        if (image is null) return _whiteGroup;
        if (_imageGroups.TryGetValue(image.GetHashCode(), out var cached)) return (BindGroup*)cached;

        var descriptor = new TextureDescriptor
        {
            Size = new Extent3D((uint)image.Width, (uint)image.Height, 1),
            MipLevelCount = 1,
            SampleCount = 1,
            Dimension = TextureDimension.Dimension2D,
            Format = Format,
            Usage = TextureUsage.TextureBinding | TextureUsage.CopyDst,
        };
        var texture = _wgpu.DeviceCreateTexture(_device, in descriptor);
        var view = _wgpu.TextureCreateView(texture, null);

        fixed (byte* pixels = image.Source.Rgba)
        {
            var destination = new ImageCopyTexture { Texture = texture, MipLevel = 0, Aspect = TextureAspect.All };
            var layout = new TextureDataLayout { BytesPerRow = (uint)(image.Width * 4), RowsPerImage = (uint)image.Height };
            var extent = new Extent3D((uint)image.Width, (uint)image.Height, 1);
            _wgpu.QueueWriteTexture(_queue, in destination, pixels, (nuint)image.Source.Rgba.Length, in layout, in extent);
        }

        image.Texture = texture;
        image.View = view;
        var group = CreateTextureGroup(view);
        _imageGroups[image.GetHashCode()] = (nint)group;
        return group;
    }

    void UploadFrame(WebGpuCanvas canvas)
    {
        var viewport = new Vector4(canvas.Surface.Width, canvas.Surface.Height, 0, 0);
        _wgpu.QueueWriteBuffer(_queue, _uniforms, 0, &viewport, (nuint)sizeof(Vector4));

        // Storage buffers must never be zero-length, and the shader indexes them unconditionally — so a
        // frame with no instances or no gradients still gets a one-element buffer.
        var instances = canvas.Instances;
        var instanceBytes = (ulong)Math.Max(1, instances.Count) * (ulong)sizeof(Instance);
        if (_instanceBuffer is null || instanceBytes > _instanceCapacity)
        {
            _instanceCapacity = Math.Max(instanceBytes, _instanceCapacity * 2);
            _instanceBuffer = CreateBuffer(_instanceCapacity, BufferUsage.Storage | BufferUsage.CopyDst);
        }

        if (instances.Count > 0)
        {
            var array = new Instance[instances.Count];
            for (var i = 0; i < instances.Count; i++) array[i] = instances[i];
            fixed (Instance* p = array)
                _wgpu.QueueWriteBuffer(_queue, _instanceBuffer, 0, p, (nuint)(array.Length * sizeof(Instance)));
        }

        var stops = canvas.Stops;
        var stopBytes = (ulong)Math.Max(1, stops.Count) * (ulong)sizeof(GpuStop);
        if (_stopBuffer is null || stopBytes > _stopCapacity)
        {
            _stopCapacity = Math.Max(stopBytes, _stopCapacity * 2);
            _stopBuffer = CreateBuffer(_stopCapacity, BufferUsage.Storage | BufferUsage.CopyDst);
        }

        if (stops.Count > 0)
        {
            var array = new GpuStop[stops.Count];
            for (var i = 0; i < stops.Count; i++) array[i] = stops[i];
            fixed (GpuStop* p = array)
                _wgpu.QueueWriteBuffer(_queue, _stopBuffer, 0, p, (nuint)(array.Length * sizeof(GpuStop)));
        }
    }

    BindGroup* CreateFrameGroup()
    {
        var entries = stackalloc BindGroupEntry[3];
        entries[0] = new BindGroupEntry { Binding = 0, Buffer = _uniforms, Size = (ulong)sizeof(Vector4) };
        entries[1] = new BindGroupEntry { Binding = 1, Buffer = _instanceBuffer, Size = _instanceCapacity };
        entries[2] = new BindGroupEntry { Binding = 2, Buffer = _stopBuffer, Size = _stopCapacity };

        var descriptor = new BindGroupDescriptor { Layout = _frameLayout, EntryCount = 3, Entries = entries };
        return _wgpu.DeviceCreateBindGroup(_device, in descriptor);
    }

    /// <summary>Records and submits the frame into <paramref name="target"/>.</summary>
    public void Render(WebGpuCanvas canvas, TextureView* target)
    {
        EnsureAtlas(canvas.Fonts.Atlas);
        UploadFrame(canvas);

        var batches = canvas.Finish();
        var frameGroup = CreateFrameGroup();

        var (r, g, b, a) = canvas.ClearColor.ToFloats();
        var colorAttachment = new RenderPassColorAttachment
        {
            View = target,
            LoadOp = LoadOp.Clear,
            StoreOp = StoreOp.Store,
            ClearValue = new Silk.NET.WebGPU.Color(r, g, b, a),
            DepthSlice = WebGPU.DepthSliceUndefined,
        };

        var passDescriptor = new RenderPassDescriptor { ColorAttachmentCount = 1, ColorAttachments = &colorAttachment };

        var encoderDescriptor = new CommandEncoderDescriptor();
        var encoder = _wgpu.DeviceCreateCommandEncoder(_device, in encoderDescriptor);
        var pass = _wgpu.CommandEncoderBeginRenderPass(encoder, in passDescriptor);

        _wgpu.RenderPassEncoderSetPipeline(pass, _pipeline);
        _wgpu.RenderPassEncoderSetBindGroup(pass, 0, frameGroup, 0, null);
        _wgpu.RenderPassEncoderSetBindGroup(pass, 1, _atlasGroup, 0, null);

        foreach (var batch in batches)
        {
            _wgpu.RenderPassEncoderSetBindGroup(pass, 2, GroupFor(batch.Image), 0, null);
            _wgpu.RenderPassEncoderDraw(pass, 6, (uint)batch.Count, 0, (uint)batch.Start);
        }

        _wgpu.RenderPassEncoderEnd(pass);

        var commandsDescriptor = new CommandBufferDescriptor();
        var commands = _wgpu.CommandEncoderFinish(encoder, in commandsDescriptor);
        _wgpu.QueueSubmit(_queue, 1, &commands);
    }

    /// <summary>
    /// Renders a frame to an offscreen texture and reads it back as straight RGBA8. Used by the headless
    /// host and by the tests — asserting on real GPU output is the only way to know the shader is right.
    /// </summary>
    public byte[] RenderToRgba(WebGpuCanvas canvas)
    {
        var width = (uint)Math.Max(1, (int)canvas.Surface.Width);
        var height = (uint)Math.Max(1, (int)canvas.Surface.Height);

        var textureDescriptor = new TextureDescriptor
        {
            Size = new Extent3D(width, height, 1),
            MipLevelCount = 1,
            SampleCount = 1,
            Dimension = TextureDimension.Dimension2D,
            Format = Format,
            Usage = TextureUsage.RenderAttachment | TextureUsage.CopySrc,
        };
        var texture = _wgpu.DeviceCreateTexture(_device, in textureDescriptor);
        var view = _wgpu.TextureCreateView(texture, null);

        Render(canvas, view);

        // Texture-to-buffer copies need 256-byte-aligned rows, so the readback is padded and unpadded here.
        var bytesPerRow = (width * 4 + 255) / 256 * 256;
        var readbackSize = (ulong)bytesPerRow * height;
        var readback = CreateBuffer(readbackSize, BufferUsage.CopyDst | BufferUsage.MapRead);

        var encoderDescriptor = new CommandEncoderDescriptor();
        var encoder = _wgpu.DeviceCreateCommandEncoder(_device, in encoderDescriptor);

        var source = new ImageCopyTexture { Texture = texture, MipLevel = 0, Aspect = TextureAspect.All };
        var destination = new ImageCopyBuffer
        {
            Buffer = readback,
            Layout = new TextureDataLayout { BytesPerRow = bytesPerRow, RowsPerImage = height },
        };
        var extent = new Extent3D(width, height, 1);
        _wgpu.CommandEncoderCopyTextureToBuffer(encoder, in source, in destination, in extent);

        var commandsDescriptor = new CommandBufferDescriptor();
        var commands = _wgpu.CommandEncoderFinish(encoder, in commandsDescriptor);
        _wgpu.QueueSubmit(_queue, 1, &commands);

        var mapped = false;
        _wgpu.BufferMapAsync(readback, MapMode.Read, 0, (nuint)readbackSize,
            new PfnBufferMapCallback((status, _) =>
            {
                if (status != BufferMapAsyncStatus.Success)
                    throw new InvalidOperationException($"WebGPU readback map failed: {status}");
                mapped = true;
            }), null);

        // wgpu resolves map callbacks when the device is polled; without a surface there is no natural
        // frame boundary to do it on, so pump until the callback lands.
        for (var spins = 0; !mapped && spins < 10_000; spins++)
            _wgpuNative.DevicePoll(_device, true, null);

        if (!mapped) throw new TimeoutException("WebGPU readback did not complete.");

        var src = (byte*)_wgpu.BufferGetConstMappedRange(readback, 0, (nuint)readbackSize);
        var result = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
            Marshal.Copy((nint)(src + y * bytesPerRow), result, (int)(y * width * 4), (int)(width * 4));

        _wgpu.BufferUnmap(readback);
        return result;
    }

    public void Dispose()
    {
        // wgpu-native reference-counts these; the process usually exits right after, but releasing the
        // device explicitly keeps the validation layer quiet in tests that construct several renderers.
        if (_device is not null) _wgpu.DeviceRelease(_device);
        if (_adapter is not null) _wgpu.AdapterRelease(_adapter);
        if (_instance is not null) _wgpu.InstanceRelease(_instance);
        _wgpu.Dispose();
    }
}
