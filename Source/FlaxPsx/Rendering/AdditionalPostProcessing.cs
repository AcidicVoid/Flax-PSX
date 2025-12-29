// www.acidicvoid.com

using System;
using System.Runtime.InteropServices;
using FlaxEngine;

namespace AcidicVoid.FlaxPsx.Rendering;

/// <summary>
/// AdditionalPostProcessing Script.
/// </summary>
[Category(name: "Flax PSX")]
[ExecuteInEditMode]
public class AdditionalPostProcessing : PostProcessEffect
{
    public Texture SlotMask;
    
    [StructLayout(LayoutKind.Sequential)]
    protected struct AdditionalPostProcessingData
    {
        public Float2 resolution;
        public Float2 texelSize;
        public Float2 slotmaskSize;
        public float  aspectRatio;
        public float  internalAspectRatio;
        public int    slotmaskBlendMode;
        public float  slotmaskScale;
        public float  slotMaskStrength;
        public float  blurX;
        public float  blurY;
        public float  brightnessBoost;
        public float  blendWithOriginal;
    }
    
    // Params
    [Range( 0f, 1f)] public float Blend = 0.5f;
    [Range(-4f, 4f)] public int   SlotMaskScale = 1;
    [Range( 0.01f, 100f)] public float SlotMaskScaleMultiplierOverride = 1f;
    [Tooltip("0 = Off, 1 = Multiply, 2 = Overlay, 3 = Screen")]
    [Range( 0f, 3f)] public int SlotmaskBlendMode = 0;
    [Range( 0f, 1f)] public float SlotMaskStrength = 0.5f;
    [Range( 0f, 1f)] public float BlurX = 0.1f;
    [Range( 0f, 1f)] public float BlurY = 0.1f;
    [Range( 0f, 1f)] public float BrightnessBoost = 0.1f;
    
    public PostProcessingResources Resources;
    private GPUPipelineState _ps;
    private AdditionalPostProcessingData _additionalPostProcessingData;
    
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
    
    public override void OnEnable()
    {
#if FLAX_EDITOR
        // Register for asset reloading event and dispose resources that use shader
        Content.AssetReloading += OnAssetReloading;
#endif
        // Register postFx to game view
        MainRenderTask.Instance.AddCustomPostFx(this);
    }
    
    private void ReleaseShader()
    {
        // Release resources using shader
        Destroy(ref _ps);
    }
    
    public override bool CanRender()
    {
        return base.CanRender() && Shader && Shader.IsLoaded;
    }
    
#if FLAX_EDITOR
    private void OnAssetReloading(Asset asset)
    {
        // Shader will be hot-reloaded
        if (asset == Shader)
            ReleaseShader();
    }
#endif
    
    public override unsafe void Render(GPUContext context, ref RenderContext renderContext, GPUTexture sourceTexture, GPUTexture output)
    {
#if FLAX_EDITOR
        Profiler.BeginEventGPU("Additional Post Processing (Flax-PSX)");
#endif
        // make sure the required resources exist, otherwise skip rendering;
        // rendering without the resources will crash the game
        PostProcessingResources resources = null;
        bool resourcesAvailable = Resources?.TryGetResources(out resources) ?? false;
        if (!resourcesAvailable)
            return;

        if (!_ps)
        {
            _ps = new GPUPipelineState();
            var desc = GPUPipelineState.Description.DefaultFullscreenTriangle;
            desc.PS = Shader.GPU.GetPS("PS_FlaxPsxAdditionalPostProcessing");
            _ps.Init(ref desc);
        }
        
        // Get source and target textures
        var source = sourceTexture; // Output from plugin post-processing
        var target = output;
        
        // Get output size
        var viewportSize = MainRenderTask.Instance?.Output?.Size != null ? 
            MainRenderTask.Instance.Output.Size : 
            Screen.Size;
        var viewport = new Viewport(new(0, 0), viewportSize);
        
        // Calculate actual slotmask value
        float slotmaskScale = 1f;
        if (SlotMaskScale > 0)
            slotmaskScale = Mathf.Floor(SlotMaskScale); // Floor for safety
        else if (SlotMaskScale < 0)
            slotmaskScale = 1f / Mathf.Abs(Mathf.Floor(SlotMaskScale));
        slotmaskScale *= SlotMaskScaleMultiplierOverride;

        // Set constant buffer data (memory copy is used under the hood to copy raw data from CPU to GPU memory)
        var cb0 = Shader.GPU.GetCB(0);
        if (cb0 != IntPtr.Zero)
        {
            _additionalPostProcessingData = new()
            {
                resolution = source.Size,
                aspectRatio = source.Size.X / source.Size.Y,
                internalAspectRatio = (float) Resources.InternalRenderSize.X / (float) Resources.InternalRenderSize.Y,
                texelSize = 1f / source.Size,
                slotmaskSize = SlotMask?.Size ?? new(1,1),
                slotmaskBlendMode = SlotmaskBlendMode,
                slotmaskScale = slotmaskScale,
                slotMaskStrength = SlotMaskStrength,
                blurX = BlurX,
                blurY = BlurY,
                brightnessBoost = BrightnessBoost,
                blendWithOriginal = Blend
            };
            fixed (AdditionalPostProcessingData* cbData = &_additionalPostProcessingData)
                context.UpdateCB(cb0, new IntPtr(cbData));
        }

        // Set shader data
        context.BindCB(0, cb0);
        context.BindSR(0, source);
        if (SlotMask?.Texture)
            context.BindSR(1, SlotMask!.Texture);
        context.SetState(_ps);

        // Draw
        context.SetViewport(ref viewport);
        context.SetRenderTarget(target.View());
        context.DrawFullscreenTriangle();
#if FLAX_EDITOR
        Profiler.EndEventGPU();
#endif
    }
    
    public override void OnDisable()
    {
        // Remember to unregister from events and release created resources (it's gamedev, not webdev)
        MainRenderTask.Instance.RemoveCustomPostFx(this);
#if FLAX_EDITOR
        Content.AssetReloading -= OnAssetReloading;
#endif
        ReleaseShader();
    }
}
