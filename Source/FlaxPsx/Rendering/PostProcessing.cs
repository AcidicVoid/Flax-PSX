using System;
using System.Runtime.InteropServices;
using FlaxEngine;

namespace AcidicVoid.FlaxPsx.Rendering;

/// <summary>
/// PostProcessing class.
/// </summary>
[Category(name: "Flax PSX")]
[ExecuteInEditMode]
public class PostProcessing : PostProcessEffect
{
    [HideInEditor]
    public event Action OnChange;
    [HideInEditor]
    public event Action<Int2> OnResolutionChanged;
    
    [StructLayout(LayoutKind.Sequential)]
    protected struct ComposerData
    {
        public Float2 sceneRenderSize;
        public Float2 upscaledSize;
        public float  depthNear;
        public float  depthFar;
        public float  depthProd;
        public float  depthDiff;
        public float  depthDiffDiffRecip;
        public float  fogBoost;
        public int    useDithering;
        public int    useHighColor;
        public Float4 fogColor;
        public float  scanlineStrength;
        public float  ditherStrength;
        public float  ditherBlend;
        public int    ditherSize;
        public int    usePsxColorPrecision;
        public Float2 sceneToUpscaleRatio;
        public int    fogStyle;
    }
    [Header("Rendering", 12)]
    public Int2 RenderSize = new(640, 480);
    [Tooltip("Use for pixel-perfect scaling, will cause black borders")]
    public bool IntegerScaling = false;
    [Tooltip("Use to maintain 4:3 aspect ratio when game output has a consistent resolution")]
    public bool UseCustomViewport = true;
    [Tooltip("Use to maintain 4:3 aspect ratio when viewport has a inconsistent aspect ratio like free scalable window")]
    public bool RecalculateViewportSizeOnChange = true;
    public PostProcessingResources Resources;

    [Space(10)]
    [Header("Fog", 12)]
    public Color FogColor = Color.Transparent;
    [Range(0, 1)]  public int   FogStyle = 1;
    [Range(0, 1)]  public float FogBoost = 0.25f;
    [Range(0, 1)]  public float Falloff = 1f;
    
    [Space(10)]
    [Header("Colors", 12)]
    public bool UseDithering = false;
    [Range(0, 1)]  public float DitherStrength = 1f;
    [Range(0, 1)]  public float DitherBlend = 1f;
    public bool UsePsxColorPrecision = true;
    public bool UseHighColor = false;
    
    [Space(10)]
    [Header("Other", 12)]
    [Range(0, 1)]  public float ScanlineStrength = 0.0f;
    
    [HideInEditor] public Viewport TargetViewport => _targetViewport;

    private Int2 _renderSize;
    private bool _integerScaling;
    private bool _useHighColor;
    private bool _useCustomViewport;
    private bool _recalculateViewportSizeOnChange;
    private Viewport _targetViewport;
    private Int2 _targetSize;
    private ComposerData _composerData;
    private GPUPipelineState _psComposer;
    
    private GPUTextureDescription _depthBufferDesc = GPUTextureDescription.New2D(640, 480, 
        PixelFormat.D32_Float, 
        GPUTextureFlags.DepthStencil | GPUTextureFlags.ShaderResource);

    private GPUTexture _uiDepthBuffer;

    private Shader _shader;
    public Shader Shader
    {
        get => _shader;
        set
        {
            if (_shader != value)
            {
                _shader = value;
                ReleaseShader();
            }
        }
    }
    
    public static Action<float, float> ViewportSizeChanged;
    
    protected void CustomPostProcessing()
    {
        UseSingleTarget = true; // Ignore underlying image, this overrides existing input
    }

    public override void OnEnable()
    {
#if FLAX_EDITOR
        // Register for asset reloading event and dispose resources that use shader
        Content.AssetReloading += OnAssetReloading;
#endif
        
        // Register Event
        ViewportSizeChanged += OnViewportSizeChanged;

        // Recalculating the viewport will have no effect when custom viewport isn't used
        // so, we deactivate it
        if (!UseCustomViewport)
            RecalculateViewportSizeOnChange = false;

        // Calculate Viewport Size
        _targetSize = GetOutputSize();
        _targetViewport = RenderUtils.CalculateDisplayViewport(RenderSize, _targetSize, IntegerScaling);
        
        // Plug into main scene rendering
        MainRenderTask.Instance.AddCustomPostFx(this);

        // Disable Scene Rendering on Main Task
        MainRenderTask.Instance.ActorsSource = ActorsSources.None;

        if (_uiDepthBuffer == null)
        {
            _uiDepthBuffer = new();
            _uiDepthBuffer.Init(ref _depthBufferDesc);
        }
    }

    private Int2 GetOutputSize()
    {
        // Screen.Size is fallback option
        // For some reason, MainRenderTask.Instance.Output.Size does not exist in cooked game
        // Todo: Investigate!
        return MainRenderTask.Instance?.Output?.Size != null ? 
            Utils.Float2ToInt2(MainRenderTask.Instance.Output.Size) : 
            Utils.Float2ToInt2(Screen.Size);
    }

    public override void OnDisable()
    {
        // Cleanup
        MainRenderTask.Instance.RemoveCustomPostFx(this);
        MainRenderTask.Instance.ActorsSource = ActorsSources.Scenes;

        // Unregister Event
        ViewportSizeChanged -= OnViewportSizeChanged;
    }

    private bool HandleChanges(PostProcessingResources resources)
    {
        bool changesDetected = false;

        if (UseHighColor != _useHighColor)
        {
            _useHighColor = UseHighColor;
            var desc = resources.SceneGpuTexture.Description;
            desc.Format = _useHighColor ? PostProcessingResources.PixelFormat8 : PostProcessingResources.PixelFormat16;
            resources.SceneGpuTexture.Init(ref desc);
        }
        
        if (RenderSize != _renderSize)
        {
            _renderSize = RenderSize;
            changesDetected = true;
            // Trigger Event
            if (OnResolutionChanged != null)
                OnResolutionChanged.Invoke(_renderSize);
            _targetViewport = RenderUtils.CalculateDisplayViewport(RenderSize, _targetSize, IntegerScaling);
        }
        if (resources.InternalRenderSize != _renderSize)
        {
            resources.ResizeGpuTexture(RenderSize);
            changesDetected = true;
        }
        if (IntegerScaling != _integerScaling)
        {
            _integerScaling = IntegerScaling;
            changesDetected = true;
            if (IntegerScaling)
                UseCustomViewport = true;
            _targetViewport = RenderUtils.CalculateDisplayViewport(RenderSize, _targetSize, IntegerScaling);
        }
        if (UseCustomViewport != _useCustomViewport)
        {
            _useCustomViewport = UseCustomViewport;
            changesDetected = true;
            if (!UseCustomViewport)
                RecalculateViewportSizeOnChange = false;
        }
        if (RecalculateViewportSizeOnChange != _recalculateViewportSizeOnChange)
        {
            _recalculateViewportSizeOnChange = RecalculateViewportSizeOnChange;
            changesDetected = true;
            if (RecalculateViewportSizeOnChange)
                UseCustomViewport = true;
        }
        // Trigger Event
        if (changesDetected && OnChange != null)
            OnChange.Invoke();
        return changesDetected;
    }
    
    public override bool CanRender()
    {
        return base.CanRender() && Shader && Shader.IsLoaded;
    }

    private void ReleaseShader()
    {
        // Release resources using shader
        Destroy(ref _psComposer);
    }
    
    public override unsafe void Render(GPUContext context, ref RenderContext renderContext, GPUTexture sourceTexture, GPUTexture output)
    {
#if FLAX_EDITOR
        Profiler.BeginEventGPU("Custom Rendering");
#endif
        // make sure the required resources exist, otherwise skip rendering;
        // rendering without the resources will crash the game
        PostProcessingResources resources = null;
        bool resourcesAvailable = Resources?.TryGetResources(out resources) ?? false;
        if (!resourcesAvailable)
            return;
        
        // Extra safety-check for race condition
        if (resources.SceneRenderTask.Buffers.DepthBuffer.Width == 0) 
            return;
        
        // If any of the main rendering options have changed, restart the script
        HandleChanges(resources);
        
        // Recalculate the viewport if needed
        if (RecalculateViewportSizeOnChange && (_targetSize != GetOutputSize()))
        {
            _targetSize = GetOutputSize();
            ViewportSizeChanged.Invoke(_targetSize.X, _targetSize.Y);
        }

        // Skip Rendering when custom viewport is too small
        if (UseCustomViewport && ((_targetSize.X < 320) || (_targetSize.Y < 240)))
        {
            return;
        }

        // Starting here, we perform custom rendering on top of the in-build drawing
        // Setup missing resources
        if (!_psComposer)
        {
            _psComposer = new GPUPipelineState();
            var desc = GPUPipelineState.Description.DefaultFullscreenTriangle;
            desc.DepthEnable = true;
            desc.DepthWriteEnable = true;
            desc.StencilEnable = true;
            desc.PS = Shader.GPU.GetPS("PS_FlaxPsxPostProcessing");
            _psComposer.Init(ref desc);
        }
        
        // Pre-calculations for efficiency
        var v = resources.SceneRenderTask!.View;
        float far = v.Far;
        float depthProd = v.Near * far;
        float depthDiff = far - v.Near;
        // reciprocal for faster normalization (A / B = A * (1/B))
        // We use MathF.ReciprocalEstimate() or MathF.Reciprocal() if available,
        // but 1.0f / diff is standard and accurate for C#.
        float depthDiffDiffRecip = 1.0f / depthDiff;
        
        // Pre-calculated ratio for Scanlines optimization (sceneRenderSize / upscaledSize)
        Float2 sceneRenderSize = resources.SceneRenderTask!.Output.Size;
        Float2 sceneToUpscaleRatio = sceneRenderSize / Screen.Size;

        // Set constant buffer data (memory copy is used under the hood to copy raw data from CPU to GPU memory)
        var cb0 = Shader.GPU.GetCB(0);
        if (cb0 != IntPtr.Zero)
        {
            _composerData = new()
            {
                sceneRenderSize = sceneRenderSize,
                upscaledSize = Screen.Size,
                depthNear = v.Near,
                depthFar = far,
                depthProd = depthProd,
                depthDiff = depthDiff,
                depthDiffDiffRecip = depthDiffDiffRecip,
                fogBoost = FogBoost,
                useDithering = UseDithering ? 1 : 0,
                useHighColor = UseHighColor ? 1 : 0,
                fogColor = new(FogColor.R,FogColor.G,FogColor.B,FogColor.A),
                scanlineStrength = ScanlineStrength,
                ditherStrength = DitherStrength,
                ditherBlend = DitherBlend,
                ditherSize = 1,
                usePsxColorPrecision = UsePsxColorPrecision ? 1 : 0,
                sceneToUpscaleRatio = sceneToUpscaleRatio,
                fogStyle = FogStyle,
            };
        
            fixed (ComposerData* cbData = &_composerData)
                context.UpdateCB(cb0, new IntPtr(cbData));
        }
        
        // Clear
        context.Clear(sourceTexture.View(), Color.Transparent);
        context.ClearDepth(MainRenderTask.Instance.Buffers.DepthBuffer.View());

        // Draw objects to depth buffer
        Renderer.DrawSceneDepth(context, resources.SceneRenderTask, MainRenderTask.Instance.Buffers.DepthBuffer, (Actor[])[]);

        // Draw fullscreen triangle using custom Pixel Shader
        context.BindCB(0, cb0);
        context.BindSR(0, resources.SceneRenderTask.Output);
        context.BindSR(2, resources.SceneRenderTask.Buffers.DepthBuffer.View());
        context.BindSR(3, _uiDepthBuffer.View());

        context.SetState(_psComposer);
        // If custom viewport is used, we set it here
        // Otherwise, the renderer will use the default viewport
        if (UseCustomViewport)
            context.SetViewport(ref _targetViewport);

        // Source texture holds the final viewport's dimensions, NOT the low res
        // This also applies if UseCustomViewport is set to true
        //context.SetRenderTarget(sourceTexture.View());
        context.SetRenderTarget(output.View());
        context.DrawFullscreenTriangle();
#if FLAX_EDITOR
        Profiler.EndEventGPU();
#endif
    }
    
    private void OnViewportSizeChanged(float w, float h)
    {
        _targetViewport = RenderUtils.CalculateDisplayViewport(_renderSize, _targetSize, IntegerScaling);
    }

#if FLAX_EDITOR
    private void OnAssetReloading(Asset asset)
    {
        // Shader will be hot-reloaded
        if (asset == Shader)
            ReleaseShader();
    }
#endif
}
