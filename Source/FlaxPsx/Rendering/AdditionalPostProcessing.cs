// www.acidicvoid.com

using System;
using System.Runtime.InteropServices;
using AcidicVoid.FlaxPsx.Rendering;
using FlaxEngine;

namespace AcidicVoid.FlaxPsx.Rendering;

/// <summary>
/// AdditionalPostProcessing Script.
/// </summary>
[Category(name: "Flax PSX")]
[ExecuteInEditMode]
public class AdditionalPostProcessing : PostProcessEffect
{
    [StructLayout(LayoutKind.Sequential)]
    protected struct AdditionalPostProcessingData
    {
        public Float2 resolution;
        public Float2 texelSize;
        public float  blendWithOriginal;
    }
    
    // Params
    [Range(0f,1f)]
    public float Blend = 0.5f;
    
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
        
        // Set constant buffer data (memory copy is used under the hood to copy raw data from CPU to GPU memory)
        var cb0 = Shader.GPU.GetCB(0);
        if (cb0 != IntPtr.Zero)
        {
            _additionalPostProcessingData = new()
            {
                resolution = source.Size,
                texelSize = 1f / source.Size,
                blendWithOriginal = Blend
            };
            fixed (AdditionalPostProcessingData* cbData = &_additionalPostProcessingData)
                context.UpdateCB(cb0, new IntPtr(cbData));
        }

        // Set shader data
        context.BindCB(0, cb0);
        context.BindSR(0, source);
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
