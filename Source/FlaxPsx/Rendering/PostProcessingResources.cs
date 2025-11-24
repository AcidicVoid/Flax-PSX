using FlaxEngine;

namespace AcidicVoid.FlaxPsx.Rendering;

/// <summary>
/// PostProcessingResources class.
/// </summary>
[Category(name: "Flax PSX")]
[ExecuteInEditMode]
public class PostProcessingResources : Script
{
    /// <summary>
    /// Anti Aliasing level. Strongly recommended to keep turned off.
    /// </summary>
    public MSAALevel AntiAliasing = MSAALevel.None;
    
    [HideInEditor]
    public Int2 InternalRenderSize => _internalRenderSize;
    private Int2 _internalRenderSize = new(320, 240);
    
    /// <summary>
    /// Custom camera to use
    /// </summary>
    public Camera CustomCamera;
    private Camera SceneCamera => (CustomCamera) ? CustomCamera : Camera.MainCamera; 
    
    public int SceneRenderOrder = -100;
    
    private const PixelFormat PixelFormat = FlaxEngine.PixelFormat.R8G8B8A8_UNorm;
    
    /// <summary>
    /// Gets current GPU texture
    /// </summary>
    private GPUTexture _sceneGpuTexture;
    public GPUTexture SceneGpuTexture => _sceneGpuTexture;
    
    private SceneRenderTask _sceneRenderTask;
    /// <summary>
    /// Gets current scene render task
    /// </summary>
    public SceneRenderTask SceneRenderTask => _sceneRenderTask;
    private Int2 _currentGameRes;
    
    /// <summary>
    /// Called when script is enabled. Creates resources and enables render task.
    /// </summary>
    public override void OnEnable()
    {
        CreateGpuTexture(ref _sceneGpuTexture, _internalRenderSize, true);
        CreateSceneRenderTask(ref _sceneRenderTask, ref _sceneGpuTexture, SceneCamera, SceneRenderOrder);
        _sceneRenderTask.Enabled = true;
    }

    /// <summary>
    /// Resizes current scene GPU texture
    /// </summary>
    /// <param name="size">Desired texture size</param>
    public void ResizeGpuTexture(Int2 size)
    {
        _internalRenderSize = size;
        if (_sceneGpuTexture != null)
            _sceneGpuTexture.Resize(size.X, size.Y);
    }
    
    private void CreateGpuTexture(ref GPUTexture texture, Int2 size, bool setCurrentGameRes = false, bool isUiTexture = false)
    {
        DestroyGpuTexture(ref texture);
        GPUTextureDescription desc = GPUTextureDescription.New2D(
            width: size.X,
            height: size.Y,
            format: PixelFormat,
            mipCount: 0,
            msaaLevel: MSAALevel.None
        );
        if (isUiTexture)
            desc.Flags = GPUTextureFlags.ShaderResource | GPUTextureFlags.RenderTarget;
        
        texture = new GPUTexture();
        texture.Init(ref desc);
        
        if (setCurrentGameRes)
            _currentGameRes = size;
    }

    /// <summary>
    /// Creates new render task. Current render task in use will be destroyed first.
    /// </summary>
    /// <param name="sceneRenderTask">Reference to render task variable. Will be (re-)created, so it doesn't need to be inilialized.</param>
    /// <param name="texture">Reference to target render texture</param>
    /// <param name="camera">Camera to use for scene rendering</param>
    /// <param name="order"></param>
    private void CreateSceneRenderTask(ref SceneRenderTask sceneRenderTask, ref GPUTexture texture, Camera camera, int order)
    {
        DestroySceneRenderTask(ref sceneRenderTask);
        sceneRenderTask = new SceneRenderTask
        {
            ViewMode = ViewMode.Default,
            ViewLayersMask = camera.RenderLayersMask,
            Enabled = false,
            Camera = camera,
            Output = texture,
            Order = order,
            ActorsSource = ActorsSources.ScenesAndCustomActors,
        };
    }


    /// <summary>
    /// Manages continuous render task
    /// </summary>
    private void UpdateRenderTask()
    {
        if (_sceneRenderTask)
        {
            _sceneRenderTask.Camera = SceneCamera;
        }
    }

    /// <summary>
    /// Destroys the scene render task
    /// </summary>
    /// <param name="SceneRenderTask">Reference of the SceneRenderTask to destroy</param>
    private void DestroySceneRenderTask(ref SceneRenderTask task)
    {
        if (task)
        {
            task.Enabled = false;
            Destroy(task);
            task = null;
        }
    }
    
    /// <summary>
    /// Destroys the GPU texture
    /// </summary>
    /// <param name="texture">The texture to destroy</param>
    private void DestroyGpuTexture(ref GPUTexture texture)
    {
        if (texture)
        {
            texture.ReleaseGPU();
            Destroy(texture);
            texture = null;
        }
    }
    
    /// <summary>
    /// Called when script is disabled. Cleans up resources.
    /// </summary>
    public override void OnDisable()
    {
        // Destroy Scene Rendering Resources
        DestroySceneRenderTask(ref _sceneRenderTask);
        DestroyGpuTexture(ref _sceneGpuTexture); 
    }

    /// <summary>
    /// Called every fixed framerate frame (after FixedUpdate) if object is enabled
    /// </summary>
    public override void OnLateFixedUpdate()
    {
        UpdateRenderTask();
    }
}