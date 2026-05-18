using AcidicVoid.FlaxPsx.Rendering;
using FlaxEngine;

namespace FlaxPsx;

/// <summary>
/// PsxParticleSystem Script.
/// </summary>
[ExecuteInEditMode]
[Category("Flax PSX")]
[RequireActor(typeof(ParticleEffect))]
public class PsxParticleSystem : Script
{
    public PostProcessing PsxPostProcessing;
    /// <inheritdoc/>
    public override void OnEnable()
    {
        if (Actor is ParticleEffect particleEffect)
            particleEffect.CustomViewRenderTask = PsxPostProcessing.SceneRenderTask;
    }
}
