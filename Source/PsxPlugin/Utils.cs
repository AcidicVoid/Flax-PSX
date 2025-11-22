using FlaxEngine;

namespace PsxPlugin;

public class Utils
{
    public static Int2 Float2ToInt2(Float2 val)
    {
        return new(Mathf.RoundToInt(val.X), Mathf.RoundToInt(val.Y));
    }
}