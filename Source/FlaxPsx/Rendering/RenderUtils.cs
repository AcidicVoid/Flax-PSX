using FlaxEngine;

namespace AcidicVoid.FlaxPsx.Rendering;

[Category(name: "Rendering")]
public class RenderUtils
{
    /// <summary>
    /// Calculates the final viewport respecting the internal rendering resolution. This is used to scale the internal lowres image to match the game's viewport. Supports integer scaling.
    /// </summary>
    /// <param name="renderSize">Internal render size: Intended to be a low resolution like 320x240 or 640x480, but also can be higher</param>
    /// <param name="targetSize">Actual target resolution: This is the size of the game's viewport, or screen size when in fullscreen mode</param>
    /// <param name="integerScaling">Use integer scaling. Delivers pixel-perfect rendering but can result in letter/pillar-boxing.</param>
    /// <returns>Calculated Viewport for final post processing</returns>
    public static Viewport CalculateDisplayViewport(Int2 renderSize, Int2 targetSize, bool integerScaling)
    {
        float aspectTarget = (float)renderSize.X / renderSize.Y;
        float aspectScreen = (float)targetSize.X / targetSize.Y;

        float targetViewportWidth, targetViewportHeight;

        if (integerScaling)
        {
            // Integer scale of render height that fits screen
            float scale = Mathf.Floor(targetSize.Y / (float)renderSize.Y);
            targetViewportHeight = renderSize.Y * scale;
            targetViewportWidth = renderSize.X * scale;
        }
        else
        {
            if (aspectScreen > aspectTarget)
            {
                // Screen is wider → pillarbox
                targetViewportHeight = targetSize.Y;
                targetViewportWidth = targetViewportHeight * aspectTarget;
            }
            else
            {
                // Screen is taller → letterbox
                targetViewportWidth = targetSize.X;
                targetViewportHeight = targetViewportWidth / aspectTarget;
            }
        }

        Int2 targetViewport = new(Mathf.RoundToInt(targetViewportWidth), Mathf.RoundToInt(targetViewportHeight));

        int offsetX = Mathf.RoundToInt((targetSize.X - targetViewport.X) * 0.5f);
        int offsetY = Mathf.RoundToInt((targetSize.Y - targetViewport.Y) * 0.5f);

        return new Viewport
        {
            X = Mathf.Floor(offsetX),
            Y = Mathf.Floor(offsetY),
            Width = Mathf.Floor(targetViewportWidth),
            Height = Mathf.Floor(targetViewportHeight)
        };
    }
}