namespace DielectricBreakdown;

internal static class ShaderResourceUri
{
    public static Uri Get(string shaderName) => new($"pack://application:,,,/DielectricBreakdown;component/Shaders/{shaderName}.cso", UriKind.Absolute);
}
