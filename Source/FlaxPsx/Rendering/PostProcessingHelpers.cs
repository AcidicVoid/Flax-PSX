// www.acidicvoid.com

using FlaxEngine;
using Object = FlaxEngine.Object;

namespace FlaxPsx.Rendering;

/// <summary>
/// PostProcessingHelpers Script.
/// </summary>
public class PostProcessingHelpers
{
    public const PixelFormat PixelFormat8  = PixelFormat.R8G8B8A8_UNorm;
    public const PixelFormat PixelFormat16 = PixelFormat.R16G16B16A16_UNorm;
    
    /// <summary>
    /// Resizes current scene GPU texture
    /// </summary>
    /// <param name="texture">Texture to resize</param>
    /// <param name="size">Desired texture size</param>
    public GPUTexture ResizeGpuTexture(ref GPUTexture texture, Int2 size)
    {
        Debug.Log($"[{GetType().Name}] Attempting to resize GPUTexture...");
        texture.Resize(size.X, size.Y);
        Debug.Log($"[{GetType().Name}] ...done!");
        return texture;
    }

    /// <summary>
    /// Helper for creating texture description for Flax-PSX post-processing
    /// </summary>
    /// <param name="size">Desired texture size</param>
    /// <param name="highColor">Use 16-bit color depth instead of 8-bit</param>
    public GPUTextureDescription CreateGpuTextureDescription(Int2 size, bool highColor = false)
    {
        GPUTextureDescription desc = GPUTextureDescription.New2D(
            width: size.X,
            height: size.Y,
            format: highColor ? PixelFormat16 : PixelFormat8,
            mipCount: 1,
            msaaLevel: MSAALevel.None
        );
        return desc;
    }
    
    /// <summary>
    /// Helper for creating actual GPU texture for Flax-PSX post-processing
    /// </summary>
    /// <param name="desc">Texture description. Use GPUTextureDescription</param>
    /// <param name="isShaderResource">Sets flags = GPUTextureFlags.ShaderResource | GPUTextureFlags.RenderTarget</param>
    /// <param name="init">Initializes texture on creation</param>
    public GPUTexture CreateGpuTexture(GPUTextureDescription desc, bool isShaderResource = false)
    {
        Debug.Log($"[{GetType().Name}] Attempting to create GPUTexture...");
        if (isShaderResource)
            desc.Flags = GPUTextureFlags.ShaderResource | GPUTextureFlags.RenderTarget;
        
        var texture = new GPUTexture();
        texture.Init(ref desc);
        
        Debug.Log($"[{GetType().Name}] ...done: " + texture.Format);
        return texture;
    }

    /// <summary>
    /// Creates new render task.
    /// </summary>
    /// <param name="texture">Reference to target render texture</param>
    /// <param name="camera">Camera to use for scene rendering</param>
    /// <param name="order"></param>
    /// <param name="actorsSources"></param>
    /// <param name="enable">Enable task on creation</param>
    public SceneRenderTask CreateSceneRenderTask(ref GPUTexture texture, Camera camera, int order, ActorsSources actorsSources = ActorsSources.Scenes, bool enable = false)
    {
        Debug.Log($"[{GetType().Name}] Attempting to create SceneRenderTask...");
        var sceneRenderTask = new SceneRenderTask
        {
            ViewMode = ViewMode.Default,
            ViewLayersMask = camera.RenderLayersMask,
            Enabled = enable,
            Camera = camera,
            Output = texture,
            Order = order,
            ActorsSource = actorsSources
        };
        Debug.Log($"[{GetType().Name}] ...done!");
        return sceneRenderTask;
    }

    /// <summary>
    /// Destroys the scene render task
    /// </summary>
    /// <param name="task">SceneRenderTask to destroy</param>
    public void DestroySceneRenderTask(SceneRenderTask task)
    {
        Debug.Log($"[{GetType().Name}] Destroying SceneRenderTask...");
        if (task)
        {
            task.Enabled = false;
            Object.Destroy(task);
        }
        Debug.Log($"[{GetType().Name}] ...done!");
    }
    
    /// <summary>
    /// Destroys the GPU texture
    /// </summary>
    /// <param name="texture">The texture to destroy</param>
    public void DestroyGpuTexture(GPUTexture texture)
    {
        Debug.Log($"[{GetType().Name}] Destroying GPUTexture...");
        if (texture)
        {
            texture.ReleaseGPU();
            Object.Destroy(texture);
        }
        Debug.Log($"[{GetType().Name}] ...done!");
    }
}
