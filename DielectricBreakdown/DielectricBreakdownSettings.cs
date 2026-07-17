namespace DielectricBreakdown;

internal static class DielectricBreakdownSettings
{
    public const float ChargeRadius = 0.5f;
    public const float FieldBias = 0.5f;
    public const float MinimumEta = 2.0f;
    public const float MaximumEta = 6.0f;
    public const float ProjectionQuantScale = 256f;
    public const float LeaderIntensity = 0.55f;
    public const float SideIntensity = 0.4f;
    public const float SideDecayPerHop = 0.985f;
    public const float TipBoost = 1.2f;
    public const float FlashAmplitude = 1.8f;
    public const float FlashTauFraction = 0.06f;
    public const int MinimumGridSize = 4;
    public const int MaximumCanvasSize = 8192;
    public const int ScratchLength = 14;
    public const int ScratchChargeCounter = 0;
    public const int ScratchChargeCount = 1;
    public const int ScratchContactStep = 2;
    public const int ScratchContactCell = 3;
    public const int ScratchStepMax0 = 4;
    public const int ScratchStepMax1 = 5;
    public const int ScratchChosen0 = 6;
    public const int ScratchChosen1 = 7;
    public const int ScratchSilhouetteMaxProjection = 8;
    public const int ScratchGrownSteps = 9;

    public static QualitySettings GetQuality(DielectricBreakdownQuality quality)
        => quality switch
        {
            DielectricBreakdownQuality.Balanced => new QualitySettings(144, 640),
            DielectricBreakdownQuality.Ultra => new QualitySettings(256, 1536),
            _ => new QualitySettings(192, 1024),
        };

    public static (int Width, int Height, float CellSize) GetGridSize(int width, int height, int resolution)
    {
        var longSide = Math.Max(Math.Max(width, height), 1);
        var cellSize = longSide / (float)Math.Max(Math.Min(resolution, longSide), MinimumGridSize);
        var gridWidth = Math.Max((int)Math.Ceiling(width / cellSize), MinimumGridSize);
        var gridHeight = Math.Max((int)Math.Ceiling(height / cellSize), MinimumGridSize);
        return (gridWidth, gridHeight, cellSize);
    }

    public static int GetStepCount(float reachCells, int maxSteps)
        => Math.Clamp((int)(reachCells * 10f) + 96, 128, maxSteps);

    public static float GetEta(float branching)
        => MaximumEta - Math.Clamp(branching, 0f, 1f) * (MaximumEta - MinimumEta);

    public static int GetJumpFloodPassCount(int width, int height)
    {
        var maxSide = Math.Max(Math.Max(width, height), 1);
        var count = 0;
        var step = 1;
        while (step < maxSide)
        {
            step <<= 1;
            count++;
        }
        return Math.Max(count, 1);
    }

    internal readonly record struct QualitySettings(int GridResolution, int MaxSteps);
}
