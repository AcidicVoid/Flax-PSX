using System;
using System.Runtime.InteropServices;
using FlaxEngine;
using FlaxPsx.Rendering;

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
        public Float2 sceneToUpscaleRatio;
        public float  depthNear;
        public float  depthFar;
        public float  depthProd;
        public float  depthDiff;
        public float  depthDiffDiffRecip;
        public int    useDithering;
        public int    useHighColor;
        public float  ditherStrength;
        public float  ditherBlend;
        public int    ditherSize;
        public int    usePsxColorPrecision;
    }
    [Header("Rendering", 12)]
    public Int2 RenderSize = new(640, 480);
    [Tooltip("Use for pixel-perfect scaling, will cause black borders")]
    public bool IntegerScaling = false;
    [Tooltip("Use to maintain 4:3 aspect ratio when game output has a consistent resolution")]
    public bool UseCustomViewport = true;

    public Camera MainCamera;
    public ActorsSources ActorSources = ActorsSources.ScenesAndCustomActors;
    
    [Space(10)]
    [Header("Colors", 12)]
    public bool UseDithering = false;
    [Range(0, 1)]  public float DitherStrength = 1f;
    [Range(0, 1)]  public float DitherBlend = 1f;
    public bool UsePsxColorPrecision = true;
    public bool UseHighColor = false;

    [HideInEditor] public Viewport TargetViewport => _targetViewport;
    
    // Internals
    private bool _integerScaling;
    private bool _useHighColor;
    private bool _useCustomViewport;
    private Int2 _renderSize;
    private Int2 _targetSize;
    private Viewport _targetViewport;
    private GPUTexture _gpuTexture;
    private ComposerData _composerData;
    private static SceneRenderTask _sceneRenderTask;
    private static GPUPipelineState _psComposer;
    private readonly PostProcessingHelpers _helpers = new();

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
        
        // Cache values
        _renderSize                      = RenderSize;
        _targetSize                      = GetOutputSize();
        _useHighColor                    = UseHighColor;
        _integerScaling                  = IntegerScaling;
        _targetViewport                  = RenderUtils.CalculateTargetViewport(_renderSize, _targetSize, _integerScaling); // Calculate Viewport Size
        _useCustomViewport               = UseCustomViewport;

        // Create GPU texture for post-processing
        // Primarily used to render with actual low resolution instead using downscale or pixelation effects
        if (_gpuTexture == null)
        {
            var desc = _helpers.CreateGpuTextureDescription(_renderSize, _useHighColor);
            _gpuTexture = _helpers.CreateGpuTexture(
                desc,
                true
                );
        }

        // Create Scene Render Task
        if (_sceneRenderTask == null)
        {
            _sceneRenderTask = _helpers.CreateSceneRenderTask(
                ref _gpuTexture,
                MainCamera != null ? MainCamera : Camera.MainCamera,
                Order,
                ActorSources
            );
        }
        _sceneRenderTask.Enabled = true;
        
        // Register Event
        ViewportSizeChanged += OnViewportSizeChanged;
        
        // Plug into main scene rendering
        MainRenderTask.Instance.AddCustomPostFx(this);

        // Disable Scene Rendering on Main Task
        MainRenderTask.Instance.ActorsSource = ActorsSources.None;
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

        if (_sceneRenderTask != null)
        {
            _sceneRenderTask.Enabled = false;
            Destroy(_sceneRenderTask);
            _sceneRenderTask = null;
        }

        if (_gpuTexture != null)
        {
            _gpuTexture.ReleaseGPU();
            Destroy(_gpuTexture);
            _gpuTexture = null;
        }

        // Unregister Event
        ViewportSizeChanged -= OnViewportSizeChanged;
    }

    private bool HandleChanges(PostProcessingHelpers helpers)
    {
        bool changesDetected = false;
        if (UseHighColor != _useHighColor)
        {
            changesDetected    = true;
            _useHighColor = UseHighColor;
            var desc = _gpuTexture.Description;
            desc.Format = 
                _useHighColor ? PostProcessingHelpers.PixelFormat8 : PostProcessingHelpers.PixelFormat16;
            _gpuTexture.Init(ref desc);
        }
        if (IntegerScaling != _integerScaling || RenderSize != _renderSize)
        {
            changesDetected    = true;
            _integerScaling    = IntegerScaling;
            UseCustomViewport  = _integerScaling || _useCustomViewport;
            _renderSize        = RenderSize;

            // Trigger Event
            if (OnResolutionChanged != null)
                OnResolutionChanged.Invoke(_renderSize);
        }
        if (_useCustomViewport != UseCustomViewport)
        {
            changesDetected = true;
            _useCustomViewport = UseCustomViewport;
        }
        if (_gpuTexture != null)
        {
            Float2 gpuTextureSize = _gpuTexture.Size;
            Int2 gpuTextureSizeInt = new Int2(
                Mathf.CeilToInt(gpuTextureSize.X),
                Mathf.CeilToInt(gpuTextureSize.Y)
            );
            if (gpuTextureSizeInt != _renderSize)
                changesDetected = true;
        }
        // Recalculate Viewport dimensions
        if (changesDetected)
        {
            _helpers.ResizeGpuTexture(ref _gpuTexture, _renderSize);
            _targetViewport = RenderUtils.CalculateTargetViewport(_renderSize, _targetSize, _integerScaling);
        }
        // Trigger Event
        if (changesDetected && OnChange != null)
            OnChange.Invoke();
        return changesDetected;
    }
    
    public override bool CanRender()
    {
        var gpuTextureAvailable = _gpuTexture?.IsAllocated ?? false;
        var sceneRenderTaskAvailable = _sceneRenderTask?.Enabled ?? false;
        return base.CanRender() && gpuTextureAvailable && sceneRenderTaskAvailable&& Shader && Shader.IsLoaded;
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
        
        // Extra safety-check
        // if (!CanRender() || _sceneRenderTask.Buffers.DepthBuffer.Width == 0) 
        if (!CanRender()) 
            return;
        
        // If any of the main rendering options have changed, restart the script
        HandleChanges(_helpers);
        
        // Recalculate the viewport if needed
        if (_targetSize != GetOutputSize())
        {
            _targetSize = GetOutputSize();
            ViewportSizeChanged.Invoke(_targetSize.X, _targetSize.Y);
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
        var v = _sceneRenderTask!.View;
        float far = v.Far;
        float depthProd = v.Near * far;
        float depthDiff = far - v.Near;
        // reciprocal for faster normalization (A / B = A * (1/B))
        // We use MathF.ReciprocalEstimate() or MathF.Reciprocal() if available,
        // but 1.0f / diff is standard and accurate for C#.
        float depthDiffDiffRecip = 1.0f / depthDiff;
        
        // Pre-calculated ratio for Scanlines optimization (sceneRenderSize / upscaledSize)
        Float2 sceneRenderSize = _sceneRenderTask.Output.Size;
        Float2 sceneToUpscaleRatio = sceneRenderSize / Screen.Size;

        // Set constant buffer data (memory copy is used under the hood to copy raw data from CPU to GPU memory)
        var cb0 = Shader.GPU.GetCB(0);
        if (cb0 != IntPtr.Zero)
        {
            _composerData = new()
            {
                sceneRenderSize = sceneRenderSize,
                upscaledSize = Screen.Size,
                sceneToUpscaleRatio = sceneToUpscaleRatio,
                depthNear = v.Near,
                depthFar = far,
                depthProd = depthProd,
                depthDiff = depthDiff,
                depthDiffDiffRecip = depthDiffDiffRecip,
                useDithering = UseDithering ? 1 : 0,
                useHighColor = UseHighColor ? 1 : 0,
                ditherStrength = DitherStrength,
                ditherBlend = DitherBlend,
                ditherSize = 1,
                usePsxColorPrecision = UsePsxColorPrecision ? 1 : 0,
            };
        
            fixed (ComposerData* cbData = &_composerData)
                context.UpdateCB(cb0, new IntPtr(cbData));
        }
        
        // Clear
        context.Clear(sourceTexture.View(), Color.Transparent);
        context.ClearDepth(MainRenderTask.Instance.Buffers.DepthBuffer.View());

        // Draw objects to depth buffer
        Renderer.DrawSceneDepth(context, _sceneRenderTask, MainRenderTask.Instance.Buffers.DepthBuffer, (Actor[])[]);

        // Draw fullscreen triangle using custom Pixel Shader
        context.BindCB(0, cb0);
        context.BindSR(0, _sceneRenderTask.Output);
        context.BindSR(1, _sceneRenderTask.Buffers.DepthBuffer.View());

        context.SetState(_psComposer);
        // If custom viewport is used, we set it here
        // Otherwise, the renderer will use the default viewport
        if (UseCustomViewport)
            context.SetViewport(ref _targetViewport);

        // Source texture holds the final viewport's dimensions, NOT the low res
        // This also applies if UseCustomViewport is set to true
        context.SetRenderTarget(output.View());
        context.DrawFullscreenTriangle();
#if FLAX_EDITOR
        Profiler.EndEventGPU();
#endif
    }
    
    private void OnViewportSizeChanged(float w, float h)
    {
        _targetViewport = RenderUtils.CalculateTargetViewport(_renderSize, _targetSize, IntegerScaling);
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
