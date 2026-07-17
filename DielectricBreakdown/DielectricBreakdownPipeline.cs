using System.Runtime.InteropServices;
using ComputeSharp;

namespace DielectricBreakdown;

internal sealed class DielectricBreakdownPipeline : IDisposable
{
    private readonly GraphicsDevice _device;
    private ReadWriteBuffer<int>? _mask;
    private ReadWriteBuffer<int>? _state;
    private ReadWriteBuffer<int>? _birth;
    private ReadWriteBuffer<int>? _parent;
    private ReadWriteBuffer<int>? _mainFlag;
    private ReadWriteBuffer<int>? _charges;
    private ReadWriteBuffer<int>? _candidates;
    private ReadWriteBuffer<int>? _candidateFlags;
    private readonly ReadWriteBuffer<int> _growthLog;
    private readonly ReadWriteBuffer<int> _parentLog;
    private readonly ReadBackBuffer<int> _scratchReadBack;
    private readonly ReadBackBuffer<int> _growthLogReadBack;
    private readonly ReadBackBuffer<int> _parentLogReadBack;
    private ReadWriteBuffer<int>? _rowCounts;
    private ReadWriteBuffer<int>? _rowOffsets;
    private ReadWriteBuffer<int>? _potential;
    private ReadWriteBuffer<float>? _intensity;
    private readonly ReadWriteBuffer<int> _scratch;
    private int _gridWidth;
    private int _gridHeight;
    private ReadWriteBuffer<int>? _jumpFloodA;
    private ReadWriteBuffer<int>? _jumpFloodB;
    private ReadWriteBuffer<int>? _glowAccum;
    private ReadWriteBuffer<float>? _glowTemp;
    private ReadWriteBuffer<float>? _glowMap;
    private int _glowCapacity;
    private ReadWriteTexture2D<Bgra32, Float4>? _packedSource;
    private ReadWriteTexture2D<Bgra32, Float4>? _packedOutput;
    private int _packedWidth;
    private int _packedHeight;

    private DielectricBreakdownPipeline(GraphicsDevice device)
    {
        _device = device;
        _scratch = device.AllocateReadWriteBuffer<int>(DielectricBreakdownSettings.ScratchLength);
        _growthLog = device.AllocateReadWriteBuffer<int>(DielectricBreakdownSettings.MaximumStepCount);
        _parentLog = device.AllocateReadWriteBuffer<int>(DielectricBreakdownSettings.MaximumStepCount);
        _scratchReadBack = device.AllocateReadBackBuffer<int>(DielectricBreakdownSettings.ScratchLength);
        _growthLogReadBack = device.AllocateReadBackBuffer<int>(DielectricBreakdownSettings.MaximumStepCount);
        _parentLogReadBack = device.AllocateReadBackBuffer<int>(DielectricBreakdownSettings.MaximumStepCount);
    }

    public static DielectricBreakdownPipeline? TryCreate()
    {
        try
        {
            return new DielectricBreakdownPipeline(GraphicsDevice.GetDefault());
        }
        catch
        {
            return null;
        }
    }

    public static DielectricBreakdownPipeline? TryCreate(GraphicsDevice device)
    {
        try
        {
            return new DielectricBreakdownPipeline(device);
        }
        catch
        {
            return null;
        }
    }

    internal void WaitForCompletion()
    {
        _device.For(1, new FillIntShader(_scratch, 0, 0));
    }

    public void Process(ReadOnlySpan<int> source, Span<int> destination, int width, int height, in Parameters parameters)
    {
        var pixelCount = checked(width * height);
        EnsureGridFor(width, height, parameters.Quality);
        EnsureGlow(pixelCount);
        EnsurePackedTextures(width, height);
        var sourceTexture = _packedSource!;
        var outputTexture = _packedOutput!;
        sourceTexture.CopyFrom(MemoryMarshal.Cast<int, Bgra32>(source[..pixelCount]));
        using (ComputeContext context = _device.CreateComputeContext())
            RecordFullPipeline(in context, sourceTexture, outputTexture, width, height, in parameters);
        outputTexture.CopyTo(MemoryMarshal.Cast<int, Bgra32>(destination[..pixelCount]));
    }

    public void Process(
        ReadWriteTexture2D<Bgra32, Float4> source,
        ReadWriteTexture2D<Bgra32, Float4> destination,
        int width,
        int height,
        in Parameters parameters)
    {
        EnsureGridFor(width, height, parameters.Quality);
        EnsureGlow(checked(width * height));
        using ComputeContext context = _device.CreateComputeContext();
        RecordFullPipeline(in context, source, destination, width, height, in parameters);
        context.Submit();
    }

    internal void ProcessSharedAndWait(
        ReadWriteTexture2D<Bgra32, Float4> source,
        ReadWriteTexture2D<Bgra32, Float4> destination,
        int width,
        int height,
        in Parameters parameters)
    {
        EnsureGridFor(width, height, parameters.Quality);
        EnsureGlow(checked(width * height));
        using ComputeContext context = _device.CreateComputeContext();
        RecordFullPipeline(in context, source, destination, width, height, in parameters);
    }

    internal void Simulate(
        ReadWriteTexture2D<Bgra32, Float4> source,
        int canvasWidth,
        int canvasHeight,
        int sourceOffsetX,
        int sourceOffsetY,
        int sourceWidth,
        int sourceHeight,
        in Parameters parameters)
    {
        EnsureGridFor(canvasWidth, canvasHeight, parameters.Quality);
        var derived = Derive(canvasWidth, canvasHeight, in parameters);
        using (ComputeContext context = _device.CreateComputeContext())
            RecordGrowthStage(in context, source, sourceOffsetX, sourceOffsetY, sourceWidth, sourceHeight, in derived, in parameters);
        _scratchReadBack.CopyFrom(_scratch);
        _growthLogReadBack.CopyFrom(_growthLog);
        _parentLogReadBack.CopyFrom(_parentLog);
    }

    internal bool TryGetVisibleBounds(int canvasWidth, int canvasHeight, in Parameters parameters, out PixelRect rect)
    {
        rect = default;
        var derived = Derive(canvasWidth, canvasHeight, in parameters);
        var scratch = _scratchReadBack.Span;
        var contact = scratch[1];
        var total = contact != derived.Sentinel ? contact : scratch[4];
        var visible = parameters.Growth * total;
        if (total <= 0 || visible <= 0f)
            return false;

        var growthLog = _growthLogReadBack.Span;
        var parentLog = _parentLogReadBack.Span;
        var minX = int.MaxValue;
        var minY = int.MaxValue;
        var maxX = int.MinValue;
        var maxY = int.MinValue;
        for (var step = 0; step < total && step < visible; step++)
        {
            var cell = growthLog[step];
            if (cell < 0)
                continue;
            var parent = parentLog[step];
            minX = Math.Min(minX, Math.Min(cell % _gridWidth, parent % _gridWidth));
            maxX = Math.Max(maxX, Math.Max(cell % _gridWidth, parent % _gridWidth));
            minY = Math.Min(minY, Math.Min(cell / _gridWidth, parent / _gridWidth));
            maxY = Math.Max(maxY, Math.Max(cell / _gridWidth, parent / _gridWidth));
        }
        if (minX == int.MaxValue)
            return false;

        var padding = (int)MathF.Ceiling(derived.Thickness) + (derived.GlowRadius + 2) * 4;
        var left = Math.Clamp(((int)(minX * derived.CellSize) - padding) & ~3, 0, canvasWidth);
        var top = Math.Clamp(((int)(minY * derived.CellSize) - padding) & ~3, 0, canvasHeight);
        var right = Math.Clamp((int)MathF.Ceiling((maxX + 1) * derived.CellSize) + padding, 0, canvasWidth);
        var bottom = Math.Clamp((int)MathF.Ceiling((maxY + 1) * derived.CellSize) + padding, 0, canvasHeight);
        var width = Math.Min((right - left + 3) & ~3, canvasWidth - left);
        var height = Math.Min((bottom - top + 3) & ~3, canvasHeight - top);
        if (width <= 0 || height <= 0)
            return false;

        rect = new PixelRect(left, top, width, height);
        return true;
    }

    internal void RenderVisible(
        ReadWriteTexture2D<Bgra32, Float4> output,
        int canvasWidth,
        int canvasHeight,
        PixelRect rect,
        in Parameters parameters)
    {
        EnsureGlow(checked(rect.Width * rect.Height));
        var derived = Derive(canvasWidth, canvasHeight, in parameters);
        using ComputeContext context = _device.CreateComputeContext();
        RecordRenderStage(in context, output, rect, in derived, in parameters);
    }

    private void RecordFullPipeline(
        in ComputeContext context,
        ReadWriteTexture2D<Bgra32, Float4> source,
        ReadWriteTexture2D<Bgra32, Float4> output,
        int width,
        int height,
        in Parameters parameters)
    {
        var derived = Derive(width, height, in parameters);
        RecordGrowthStage(in context, source, 0, 0, width, height, in derived, in parameters);
        RecordRenderStage(in context, output, new PixelRect(0, 0, width, height), in derived, in parameters);
    }

    private void RecordGrowthStage(
        in ComputeContext context,
        ReadWriteTexture2D<Bgra32, Float4> source,
        int sourceOffsetX,
        int sourceOffsetY,
        int sourceWidth,
        int sourceHeight,
        in DerivedValues derived,
        in Parameters parameters)
    {
        var gridWidth = _gridWidth;
        var gridHeight = _gridHeight;
        var mask = _mask!;
        var state = _state!;
        var birth = _birth!;
        var parent = _parent!;
        var charges = _charges!;
        var candidates = _candidates!;
        var candidateFlags = _candidateFlags!;
        var rowCounts = _rowCounts!;
        var rowOffsets = _rowOffsets!;
        var potential = _potential!;

        context.For(1, new InitScratchShader(_scratch, derived.Sentinel));
        context.For(gridWidth, gridHeight, new SilhouetteShader(
            source, mask, sourceOffsetX, sourceOffsetY, sourceWidth, sourceHeight, gridWidth, gridHeight, derived.CellSize, 0.05f));
        context.Barrier(mask);
        context.Barrier(_scratch);
        context.For(gridWidth, gridHeight, new ElectrodeInitShader(mask, state, birth, parent, _scratch, gridWidth, gridHeight, derived.DirectionX, derived.DirectionY, DielectricBreakdownSettings.ProjectionQuantScale));
        context.Barrier(state);
        context.For(gridHeight, new ChargeRowCountShader(state, rowCounts, gridWidth, gridHeight));
        context.Barrier(rowCounts);
        context.For(1, new ChargeRowOffsetShader(rowCounts, rowOffsets, _scratch, gridHeight));
        context.Barrier(rowOffsets);
        context.Barrier(_scratch);
        context.For(gridHeight, new ChargeFillShader(state, rowOffsets, charges, gridWidth, gridHeight));
        context.Barrier(charges);
        context.For(gridWidth, gridHeight, new InitialPotentialShader(charges, _scratch, potential, gridWidth, gridHeight, DielectricBreakdownSettings.ChargeRadius));
        context.Barrier(potential);

        context.For(DielectricBreakdownSettings.GrowthThreadCount, new GrowthShader(
            state, birth, parent, potential, _scratch, candidates, candidateFlags, _growthLog, _parentLog,
            gridWidth, gridHeight, derived.MaxSteps, derived.Sentinel, parameters.Seed, derived.ReachQuantized, derived.Eta,
            DielectricBreakdownSettings.ChargeRadius, DielectricBreakdownSettings.FieldBias,
            derived.DirectionX, derived.DirectionY, derived.ProjectionMin, derived.ProjectionInverseRange, DielectricBreakdownSettings.ProjectionQuantScale));
        context.Barrier(state);
        context.Barrier(birth);
        context.Barrier(parent);
        context.Barrier(potential);
        context.Barrier(_scratch);
        context.Barrier(_growthLog);
        context.Barrier(_parentLog);
    }

    private void RecordRenderStage(
        in ComputeContext context,
        ReadWriteTexture2D<Bgra32, Float4> output,
        PixelRect rect,
        in DerivedValues derived,
        in Parameters parameters)
    {
        var gridWidth = _gridWidth;
        var gridHeight = _gridHeight;
        var state = _state!;
        var birth = _birth!;
        var parent = _parent!;
        var mainFlag = _mainFlag!;
        var intensity = _intensity!;
        var jumpFloodA = _jumpFloodA!;
        var jumpFloodB = _jumpFloodB!;
        var glowAccum = _glowAccum!;
        var glowTemp = _glowTemp!;
        var glowMap = _glowMap!;
        var glowOffsetX = rect.X / 4;
        var glowOffsetY = rect.Y / 4;
        var glowWidth = (rect.Width + 3) / 4;
        var glowHeight = (rect.Height + 3) / 4;

        context.For(gridWidth, gridHeight, new FillIntPlaneShader(mainFlag, gridWidth, gridHeight, 0));
        context.Barrier(mainFlag);
        context.For(1, new MainChannelShader(parent, birth, mainFlag, _scratch, derived.Sentinel, derived.MaxWalk));
        context.Barrier(mainFlag);
        context.For(gridWidth, gridHeight, new IntensityShader(
            state, birth, parent, mainFlag, intensity, gridWidth, gridHeight, derived.MaxWalk,
            DielectricBreakdownSettings.LeaderIntensity, DielectricBreakdownSettings.SideIntensity, DielectricBreakdownSettings.SideDecayPerHop));
        context.Barrier(intensity);

        context.For(glowWidth, glowHeight, new FillIntPlaneShader(glowAccum, glowWidth, glowHeight, 0));
        context.Barrier(glowAccum);
        context.For(gridWidth, gridHeight, new GlowDepositShader(
            state, birth, parent, mainFlag, intensity, _scratch, glowAccum,
            gridWidth, gridHeight, glowOffsetX, glowOffsetY, glowWidth, glowHeight, derived.Sentinel, derived.CellSize, parameters.Growth,
            DielectricBreakdownSettings.TipBoost, derived.InverseTipTau,
            DielectricBreakdownSettings.FlashAmplitude, derived.InverseFlashTau));
        context.Barrier(glowAccum);
        context.For(glowWidth, glowHeight, new GlowBlurHorizontalShader(glowAccum, glowTemp, glowWidth, glowHeight, derived.GlowRadius));
        context.Barrier(glowTemp);
        context.For(glowWidth, glowHeight, new GlowBlurVerticalShader(glowTemp, glowMap, glowWidth, glowHeight, derived.GlowRadius));
        context.Barrier(glowMap);

        context.For(gridWidth, gridHeight, new JumpFloodSeedShader(
            state, birth, _scratch, jumpFloodA, gridWidth, gridHeight, derived.Sentinel, parameters.Growth));
        context.Barrier(jumpFloodA);

        var reading = jumpFloodA;
        var writing = jumpFloodB;
        var stepSize = 1;
        var maxSide = Math.Max(gridWidth, gridHeight);
        while (stepSize < maxSide)
            stepSize <<= 1;
        stepSize >>= 1;
        while (stepSize >= 1)
        {
            context.For(gridWidth, gridHeight, new JumpFloodPassShader(reading, writing, gridWidth, gridHeight, stepSize));
            context.Barrier(writing);
            (reading, writing) = (writing, reading);
            stepSize >>= 1;
        }

        context.For(rect.Width, rect.Height, new RenderShader(
            reading, state, birth, parent, mainFlag, intensity, _scratch, glowMap, output,
            rect.X, rect.Y, rect.Width, rect.Height, gridWidth, gridHeight,
            glowOffsetX, glowOffsetY, glowWidth, glowHeight, derived.Sentinel, derived.CellSize, parameters.Growth,
            derived.Thickness, derived.GlowStrength,
            DielectricBreakdownSettings.TipBoost, derived.InverseTipTau,
            DielectricBreakdownSettings.FlashAmplitude, derived.InverseFlashTau,
            parameters.ColorR, parameters.ColorG, parameters.ColorB));
    }

    private static DerivedValues Derive(int width, int height, in Parameters parameters)
    {
        var settings = DielectricBreakdownSettings.GetQuality(parameters.Quality);
        var (gridWidth, gridHeight, cellSize) = DielectricBreakdownSettings.GetGridSize(width, height, settings.GridResolution);
        var reachCellCount = Math.Max(parameters.ReachPixels, 1f) / cellSize;
        var maxSteps = DielectricBreakdownSettings.GetStepCount(reachCellCount, settings.MaxSteps);
        var radians = parameters.AngleDegrees * (MathF.PI / 180f);
        var directionX = MathF.Sin(radians);
        var directionY = MathF.Cos(radians);
        var projectionMin = MathF.Min(0f, gridWidth * directionX) + MathF.Min(0f, gridHeight * directionY);
        var projectionMax = MathF.Max(0f, gridWidth * directionX) + MathF.Max(0f, gridHeight * directionY);
        var glowRadius = DielectricBreakdownSettings.GetGlowRadius(parameters.Glow);
        return new DerivedValues(
            cellSize,
            maxSteps,
            maxSteps + 1,
            maxSteps + 4,
            directionX,
            directionY,
            projectionMin,
            1f / MathF.Max(projectionMax - projectionMin, 1e-4f),
            DielectricBreakdownSettings.GetEta(parameters.Branching),
            (int)(reachCellCount * DielectricBreakdownSettings.ProjectionQuantScale),
            Math.Max(parameters.Thickness, 0.05f),
            glowRadius,
            parameters.Glow * 0.27f * (glowRadius + 1),
            1f / MathF.Max(0.03f * maxSteps, 1f),
            1f / MathF.Max(DielectricBreakdownSettings.FlashTauFraction * maxSteps, 1f));
    }

    private void EnsureGridFor(int width, int height, DielectricBreakdownQuality quality)
    {
        var settings = DielectricBreakdownSettings.GetQuality(quality);
        var (gridWidth, gridHeight, _) = DielectricBreakdownSettings.GetGridSize(width, height, settings.GridResolution);
        EnsureGrid(gridWidth, gridHeight);
    }

    private void EnsureGrid(int gridWidth, int gridHeight)
    {
        if (_gridWidth == gridWidth && _gridHeight == gridHeight)
            return;

        DisposeGridBuffers();
        var gridLength = gridWidth * gridHeight;
        _mask = _device.AllocateReadWriteBuffer<int>(gridLength);
        _state = _device.AllocateReadWriteBuffer<int>(gridLength);
        _birth = _device.AllocateReadWriteBuffer<int>(gridLength);
        _parent = _device.AllocateReadWriteBuffer<int>(gridLength);
        _mainFlag = _device.AllocateReadWriteBuffer<int>(gridLength);
        _charges = _device.AllocateReadWriteBuffer<int>(gridLength);
        _candidates = _device.AllocateReadWriteBuffer<int>(gridLength);
        _candidateFlags = _device.AllocateReadWriteBuffer<int>(gridLength);
        _rowCounts = _device.AllocateReadWriteBuffer<int>(gridHeight);
        _rowOffsets = _device.AllocateReadWriteBuffer<int>(gridHeight);
        _potential = _device.AllocateReadWriteBuffer<int>(gridLength);
        _intensity = _device.AllocateReadWriteBuffer<float>(gridLength);
        _jumpFloodA = _device.AllocateReadWriteBuffer<int>(gridLength);
        _jumpFloodB = _device.AllocateReadWriteBuffer<int>(gridLength);
        _gridWidth = gridWidth;
        _gridHeight = gridHeight;
    }

    private void EnsureGlow(int pixelCount)
    {
        var glowCapacity = (pixelCount + 15) / 16 + 64;
        if (_glowCapacity < glowCapacity)
        {
            _glowAccum?.Dispose();
            _glowTemp?.Dispose();
            _glowMap?.Dispose();
            _glowAccum = _device.AllocateReadWriteBuffer<int>(glowCapacity);
            _glowTemp = _device.AllocateReadWriteBuffer<float>(glowCapacity);
            _glowMap = _device.AllocateReadWriteBuffer<float>(glowCapacity);
            _glowCapacity = glowCapacity;
        }
    }

    private void EnsurePackedTextures(int width, int height)
    {
        if (_packedWidth == width && _packedHeight == height)
            return;

        _packedSource?.Dispose();
        _packedOutput?.Dispose();
        _packedSource = _device.AllocateReadWriteTexture2D<Bgra32, Float4>(width, height);
        _packedOutput = _device.AllocateReadWriteTexture2D<Bgra32, Float4>(width, height);
        _packedWidth = width;
        _packedHeight = height;
    }

    private void DisposeGridBuffers()
    {
        _mask?.Dispose();
        _state?.Dispose();
        _birth?.Dispose();
        _parent?.Dispose();
        _mainFlag?.Dispose();
        _charges?.Dispose();
        _candidates?.Dispose();
        _candidateFlags?.Dispose();
        _rowCounts?.Dispose();
        _rowOffsets?.Dispose();
        _potential?.Dispose();
        _intensity?.Dispose();
        _jumpFloodA?.Dispose();
        _jumpFloodB?.Dispose();
        _mask = null;
        _state = null;
        _birth = null;
        _parent = null;
        _mainFlag = null;
        _charges = null;
        _candidates = null;
        _candidateFlags = null;
        _rowCounts = null;
        _rowOffsets = null;
        _potential = null;
        _intensity = null;
        _jumpFloodA = null;
        _jumpFloodB = null;
        _gridWidth = 0;
        _gridHeight = 0;
    }

    public void Dispose()
    {
        DisposeGridBuffers();
        _glowAccum?.Dispose();
        _glowTemp?.Dispose();
        _glowMap?.Dispose();
        _glowAccum = null;
        _glowTemp = null;
        _glowMap = null;
        _glowCapacity = 0;
        _packedSource?.Dispose();
        _packedOutput?.Dispose();
        _packedSource = null;
        _packedOutput = null;
        _packedWidth = 0;
        _packedHeight = 0;
        _scratchReadBack.Dispose();
        _growthLogReadBack.Dispose();
        _parentLogReadBack.Dispose();
        _growthLog.Dispose();
        _parentLog.Dispose();
        _scratch.Dispose();
    }

    internal readonly record struct PixelRect(int X, int Y, int Width, int Height);

    private readonly record struct DerivedValues(
        float CellSize,
        int MaxSteps,
        int Sentinel,
        int MaxWalk,
        float DirectionX,
        float DirectionY,
        float ProjectionMin,
        float ProjectionInverseRange,
        float Eta,
        int ReachQuantized,
        float Thickness,
        int GlowRadius,
        float GlowStrength,
        float InverseTipTau,
        float InverseFlashTau);

    internal readonly record struct Parameters(
        DielectricBreakdownQuality Quality,
        float Growth,
        float Branching,
        float AngleDegrees,
        float ReachPixels,
        float Thickness,
        float Glow,
        float ColorR,
        float ColorG,
        float ColorB,
        int Seed);
}
