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
    public Action OnChange;
    [HideInEditor]
    public Action<Int2> OnResolutionChanged;
    
    [StructLayout(LayoutKind.Sequential)]
    protected struct ComposerData
    {
        public Float2 sceneRenderSize;
        public Float2 upscaledSize;
        public float  near;
        public float  far;
        public float  fogBoost;
        public float  falloff;
        public Float4 fogColor;
        public float  fogMin;
        public float  scanlineStrength;
        public int    useDithering;
        public float  ditherStrength;
        public float  ditherBlend;
        public int    ditherSize;
    }
    public Int2 RenderSize = new(640, 480);
    [Tooltip("Use for pixel-perfect scaling, will cause black borders")]
    public bool IntegerScaling = false;
    [Tooltip("Use to maintain 4:3 aspect ratio when game output has a consistent resolution")]
    public bool UseCustomViewport = true;
    [Tooltip("Use to maintain 4:3 aspect ratio when viewport has a inconsistent aspect ratio like free scalable window")]
    public bool RecalculateViewportSizeOnChange = true;
    public PostProcessingResources Resources;

    public Color FogColor = Color.Transparent;
    [Range(0, 1)]  public float FogBoost = 0.25f;
    [Range(0, 1)]  public float Falloff = 1f;
    [Range(0, 1)]  public float FogMinimumValue = 0f;
    [Range(0, 1)]  public float ScanlineStrength = 0.0f;
    public bool UseDithering = false;
    [Range(0, 1)]  public float DitherStrength = 1f;
    [Range(0, 1)]  public float DitherBlend = 1f;
    [Range(1, 2)]  public int DitherSize = 1;
    [HideInEditor] public Viewport TargetViewport => _targetViewport;

    private Int2 _renderSize;
    private bool _integerScaling;
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

        _uiDepthBuffer = new();
        _uiDepthBuffer.Init(ref _depthBufferDesc);
    }

    private Int2 GetOutputSize()
    {
        // Screen.Size is fallback option
        // For some reason, MainRenderTask.Instance.Output.Size does not exist in cooked game
        // Todo: Investigate!
        bool mainRenderTaskAvailable = MainRenderTask.Instance != null;

        if (mainRenderTaskAvailable)
            return Utils.Float2ToInt2(MainRenderTask.Instance!.Output.Size);

        return Utils.Float2ToInt2(Screen.Size);
    }

    public override void OnDisable()
    {
        // Cleanup
        MainRenderTask.Instance.RemoveCustomPostFx(this);
        MainRenderTask.Instance.ActorsSource = ActorsSources.Scenes;

        // Unregister Event
        ViewportSizeChanged -= OnViewportSizeChanged;
    }

    private bool HandleChanges()
    {
        bool changesDetected = false;

        if (RenderSize != _renderSize)
        {
            _renderSize = RenderSize;
            changesDetected = true;
            // Trigger Event
            if (OnResolutionChanged != null)
                OnResolutionChanged.Invoke(_renderSize);
        }
        if (Resources != null && Resources.InternalRenderSize != _renderSize)
        {
            Resources.ResizeGpuTexture(RenderSize);
            changesDetected = true;
        }
        if (IntegerScaling != _integerScaling)
        {
            _integerScaling = IntegerScaling;
            changesDetected = true;
            if (IntegerScaling)
                UseCustomViewport = true;
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
        // If any of the main rendering options have changed, restart the script
        HandleChanges();
        
        // Recalculate the viewport if needed
        if (RecalculateViewportSizeOnChange && (_targetSize != GetOutputSize()))
        {
            _targetSize = GetOutputSize();
            ViewportSizeChanged.Invoke(_targetSize.X, _targetSize.Y);
        }

        // Skip Rendering when custom viewport is too small
        if (UseCustomViewport && ((_targetSize.X < 320) || (_targetSize.Y < 240)))
        {
            Debug.Log("Composer Rendering skipped");
            return;
        }
        
        // make sure the required resources exist, otherwise skip rendering;
        // rendering without the resources will crash the game
        if (!Resources || 
            !Resources?.SceneRenderTask?.Output)
        {
            Debug.LogWarning("Composer Rendering skipped due to missing render resources");
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

        var v = Resources!.SceneRenderTask!.View;
        // Set constant buffer data (memory copy is used under the hood to copy raw data from CPU to GPU memory)
        var cb0 = Shader.GPU.GetCB(0);
        if (cb0 != IntPtr.Zero)
        {
            _composerData = new()
            {
                sceneRenderSize = Resources!.SceneRenderTask!.Output.Size,
                upscaledSize = Screen.Size,
                near = v.Near,
                far = v.Far,
                fogColor = new(FogColor.R,FogColor.G,FogColor.B,FogColor.A),
                fogBoost = FogBoost,
                falloff = Falloff,
                fogMin = FogMinimumValue,
                scanlineStrength = ScanlineStrength,
                useDithering = UseDithering ? 1 : 0,
                ditherStrength = DitherStrength,
                ditherBlend = DitherBlend,
                ditherSize = DitherSize,
            };
        
            fixed (ComposerData* cbData = &_composerData)
                context.UpdateCB(cb0, new IntPtr(cbData));
        }

        // Input.MousePosition += sourceTexture.Size - _targetSize;
        
        
        // Clear
        context.Clear(sourceTexture.View(), Color.Transparent);
        context.ClearDepth(MainRenderTask.Instance.Buffers.DepthBuffer.View());

        // Draw objects to depth buffer
        Renderer.DrawSceneDepth(context, Resources!.SceneRenderTask!, MainRenderTask.Instance.Buffers.DepthBuffer, (Actor[])[]);

        // Draw fullscreen triangle using custom Pixel Shader
        context.BindCB(0, cb0);
        context.BindSR(0, Resources!.SceneRenderTask!.Output);
        context.BindSR(2, Resources!.SceneRenderTask!.Buffers.DepthBuffer.View());
        context.BindSR(3, _uiDepthBuffer.View());

        context.SetState(_psComposer);
        // If custom viewport is used, we set it here
        // Otherwise, the renderer will use the default viewport
        if (UseCustomViewport)
            context.SetViewport(ref _targetViewport);

        context.SetRenderTarget(sourceTexture.View());
        context.DrawFullscreenTriangle();
#if FLAX_EDITOR
        Profiler.EndEventGPU();
#endif
    }
    
    // Gets invoked in SetRenderSize
    private void OnViewportSizeChanged(float w, float h)
    {
        _targetViewport = RenderUtils.CalculateDisplayViewport(RenderSize, _targetSize, IntegerScaling);
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
