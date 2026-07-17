using System.Runtime.InteropServices;
using ComputeSharp;
using ComputeSharp.Interop;
using Vortice;
using Vortice.Direct2D1;
using Vortice.DXGI;
using Vortice.Mathematics;
using YukkuriMovieMaker.Commons;
using PixelFormat = Vortice.DCommon.PixelFormat;

namespace DielectricBreakdown.Tests;

public sealed class DielectricBreakdownEffectTests
{
    private static double ValueAt(YukkuriMovieMaker.Commons.Animation animation) => animation.GetValue(0, 1, 30);

    private static DielectricBreakdownPipeline.Parameters CreateParameters(
        DielectricBreakdownQuality quality = DielectricBreakdownQuality.Balanced,
        float growth = 1f,
        float branching = 0.5f,
        float angleDegrees = 0f,
        float reachPixels = 40f,
        float thickness = 2f,
        float glow = 0.5f,
        int seed = 0)
        => new(quality, growth, branching, angleDegrees, reachPixels, thickness, glow, 0.7f, 0.8f, 1f, seed);

    [Fact]
    public void DefaultParameterValuesMatchSpecification()
    {
        var effect = new DielectricBreakdownEffect();

        Assert.Equal(100d, ValueAt(effect.Amount), 6);
        Assert.Equal(100d, ValueAt(effect.Growth), 6);
        Assert.Equal(0d, ValueAt(effect.Angle), 6);
        Assert.Equal(75d, ValueAt(effect.Reach), 6);
        Assert.Equal(50d, ValueAt(effect.Branching), 6);
        Assert.Equal(2.5d, ValueAt(effect.Thickness), 6);
        Assert.Equal(50d, ValueAt(effect.Glow), 6);
        Assert.Equal(DielectricBreakdownQuality.High, effect.Quality);
        Assert.Equal(0, effect.Seed);
        Assert.Equal(System.Windows.Media.Color.FromArgb(255, 180, 200, 255), effect.LightningColor);
    }

    [Theory]
    [InlineData(int.MinValue, 0)]
    [InlineData(-1, 0)]
    [InlineData(0, 0)]
    [InlineData(1234, 1234)]
    public void SeedClampsNegativeInputToZero(int input, int expected)
    {
        var effect = new DielectricBreakdownEffect { Seed = input };

        Assert.Equal(expected, effect.Seed);
    }

    [Fact]
    public void CreateExoVideoFiltersReturnsEmpty()
    {
        var effect = new DielectricBreakdownEffect();

        Assert.Empty(effect.CreateExoVideoFilters(0, null!));
    }

    [Theory]
    [InlineData(DielectricBreakdownQuality.Balanced, 144, 640)]
    [InlineData(DielectricBreakdownQuality.High, 192, 1024)]
    [InlineData(DielectricBreakdownQuality.Ultra, 256, 1536)]
    public void QualitySettingsMatchSpecification(DielectricBreakdownQuality quality, int resolution, int maxSteps)
    {
        var settings = DielectricBreakdownSettings.GetQuality(quality);

        Assert.Equal(resolution, settings.GridResolution);
        Assert.Equal(maxSteps, settings.MaxSteps);
    }

    [Theory]
    [InlineData(1920, 1080, 192)]
    [InlineData(1080, 1920, 192)]
    [InlineData(8, 8, 192)]
    [InlineData(4096, 16, 144)]
    [InlineData(100, 100, 256)]
    public void GridSizeCoversCanvasWithSquareCells(int width, int height, int resolution)
    {
        var (gridWidth, gridHeight, cellSize) = DielectricBreakdownSettings.GetGridSize(width, height, resolution);

        Assert.True(gridWidth >= DielectricBreakdownSettings.MinimumGridSize);
        Assert.True(gridHeight >= DielectricBreakdownSettings.MinimumGridSize);
        Assert.True(cellSize >= 1f || Math.Max(width, height) < resolution);
        Assert.True(gridWidth * cellSize >= width);
        Assert.True(gridHeight * cellSize >= height);
        Assert.True(Math.Max(gridWidth, gridHeight) <= Math.Max(resolution, DielectricBreakdownSettings.MinimumGridSize) + 1);
    }

    [Theory]
    [InlineData(0f, 1024, 128)]
    [InlineData(10f, 1024, 196)]
    [InlineData(60f, 1024, 696)]
    [InlineData(200f, 1024, 1024)]
    [InlineData(200f, 640, 640)]
    public void StepCountScalesWithReachAndRespectsCap(float reachCells, int maxSteps, int expected)
    {
        Assert.Equal(expected, DielectricBreakdownSettings.GetStepCount(reachCells, maxSteps));
    }

    [Fact]
    public void EtaMappingIsMonotonicAndBounded()
    {
        Assert.Equal(DielectricBreakdownSettings.MaximumEta, DielectricBreakdownSettings.GetEta(0f), 5);
        Assert.Equal(DielectricBreakdownSettings.MinimumEta, DielectricBreakdownSettings.GetEta(1f), 5);
        Assert.True(DielectricBreakdownSettings.GetEta(0.25f) > DielectricBreakdownSettings.GetEta(0.75f));
        Assert.Equal(DielectricBreakdownSettings.MaximumEta, DielectricBreakdownSettings.GetEta(-5f), 5);
        Assert.Equal(DielectricBreakdownSettings.MinimumEta, DielectricBreakdownSettings.GetEta(5f), 5);
    }

    [Theory]
    [InlineData(1, 1, 1)]
    [InlineData(2, 2, 1)]
    [InlineData(3, 3, 2)]
    [InlineData(256, 128, 8)]
    [InlineData(257, 16, 9)]
    public void JumpFloodPassCountCoversLongSide(int width, int height, int expected)
    {
        Assert.Equal(expected, DielectricBreakdownSettings.GetJumpFloodPassCount(width, height));
    }

    [Fact]
    public void TransparentInputYieldsTransparentOutput()
    {
        using var pipeline = DielectricBreakdownPipeline.TryCreate();
        if (pipeline is null)
        {
            Assert.Skip("Direct3D 12 is unavailable.");
            return;
        }

        const int width = 64;
        const int height = 64;
        var source = new int[width * height];
        var destination = new int[source.Length];
        Array.Fill(destination, -1);
        var parameters = CreateParameters();

        pipeline.Process(source, destination, width, height, in parameters);

        Assert.All(destination, pixel => Assert.Equal(0, pixel));
    }

    [Fact]
    public void FullyOpaqueInputYieldsTransparentOutput()
    {
        using var pipeline = DielectricBreakdownPipeline.TryCreate();
        if (pipeline is null)
        {
            Assert.Skip("Direct3D 12 is unavailable.");
            return;
        }

        const int width = 64;
        const int height = 64;
        var source = new int[width * height];
        Array.Fill(source, unchecked((int)0xFFFFFFFF));
        var destination = new int[source.Length];
        Array.Fill(destination, -1);
        var parameters = CreateParameters();

        pipeline.Process(source, destination, width, height, in parameters);

        Assert.All(destination, pixel => Assert.Equal(0, pixel));
    }

    [Fact]
    public void GrowthZeroYieldsTransparentOutput()
    {
        using var pipeline = DielectricBreakdownPipeline.TryCreate();
        if (pipeline is null)
        {
            Assert.Skip("Direct3D 12 is unavailable.");
            return;
        }

        const int width = 96;
        const int height = 96;
        var source = CreateSquareSource(width, height, 32, 16, 32, 32);
        var destination = new int[source.Length];
        Array.Fill(destination, -1);
        var parameters = CreateParameters(growth: 0f);

        pipeline.Process(source, destination, width, height, in parameters);

        Assert.All(destination, pixel => Assert.Equal(0, pixel));
    }

    [Fact]
    public void FullGrowthProducesLightning()
    {
        using var pipeline = DielectricBreakdownPipeline.TryCreate();
        if (pipeline is null)
        {
            Assert.Skip("Direct3D 12 is unavailable.");
            return;
        }

        const int width = 128;
        const int height = 128;
        var source = CreateSquareSource(width, height, 48, 16, 32, 32);
        var destination = new int[source.Length];
        var parameters = CreateParameters(reachPixels: 48f);

        pipeline.Process(source, destination, width, height, in parameters);

        Assert.True(CountLitPixels(destination) > 0);
    }

    [Fact]
    public void GpuPipelineIsDeterministic()
    {
        using var pipeline = DielectricBreakdownPipeline.TryCreate();
        if (pipeline is null)
        {
            Assert.Skip("Direct3D 12 is unavailable.");
            return;
        }

        const int width = 128;
        const int height = 128;
        var source = CreateSquareSource(width, height, 48, 16, 32, 32);
        var first = new int[source.Length];
        var second = new int[source.Length];
        var parameters = CreateParameters(branching: 0.8f, glow: 0.6f, seed: 42);

        pipeline.Process(source, first, width, height, in parameters);
        pipeline.Process(source, second, width, height, in parameters);

        Assert.Equal(first, second);
    }

    [Fact]
    public void DifferentSeedsProduceDifferentPaths()
    {
        using var pipeline = DielectricBreakdownPipeline.TryCreate();
        if (pipeline is null)
        {
            Assert.Skip("Direct3D 12 is unavailable.");
            return;
        }

        const int width = 128;
        const int height = 128;
        var source = CreateSquareSource(width, height, 48, 16, 32, 32);
        var first = new int[source.Length];
        var second = new int[source.Length];

        var parametersA = CreateParameters(seed: 1);
        var parametersB = CreateParameters(seed: 2);
        pipeline.Process(source, first, width, height, in parametersA);
        pipeline.Process(source, second, width, height, in parametersB);

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void OutputAlphaStaysPremultipliedAndBounded()
    {
        using var pipeline = DielectricBreakdownPipeline.TryCreate();
        if (pipeline is null)
        {
            Assert.Skip("Direct3D 12 is unavailable.");
            return;
        }

        const int width = 128;
        const int height = 128;
        var source = CreateSquareSource(width, height, 48, 16, 32, 32);
        var destination = new int[source.Length];
        var parameters = CreateParameters(glow: 1f, thickness: 4f);

        pipeline.Process(source, destination, width, height, in parameters);

        foreach (var pixel in destination)
        {
            var alpha = (pixel >> 24) & 255;
            Assert.InRange((pixel >> 16) & 255, 0, alpha);
            Assert.InRange((pixel >> 8) & 255, 0, alpha);
            Assert.InRange(pixel & 255, 0, alpha);
        }
    }

    [Fact]
    public void GrowthIncreasesLitArea()
    {
        using var pipeline = DielectricBreakdownPipeline.TryCreate();
        if (pipeline is null)
        {
            Assert.Skip("Direct3D 12 is unavailable.");
            return;
        }

        const int width = 128;
        const int height = 128;
        var source = CreateSquareSource(width, height, 48, 16, 32, 32);
        var partial = new int[source.Length];
        var full = new int[source.Length];

        var partialParameters = CreateParameters(growth: 0.3f, reachPixels: 56f);
        var fullParameters = CreateParameters(growth: 1f, reachPixels: 56f);
        pipeline.Process(source, partial, width, height, in partialParameters);
        pipeline.Process(source, full, width, height, in fullParameters);

        Assert.True(CountLitPixels(full) > CountLitPixels(partial));
    }

    [Fact]
    public void DirectionBiasGrowsTowardTarget()
    {
        using var pipeline = DielectricBreakdownPipeline.TryCreate();
        if (pipeline is null)
        {
            Assert.Skip("Direct3D 12 is unavailable.");
            return;
        }

        const int width = 160;
        const int height = 160;
        var source = CreateSquareSource(width, height, 64, 24, 32, 32);
        var destination = new int[source.Length];
        var parameters = CreateParameters(reachPixels: 72f, glow: 0f);

        pipeline.Process(source, destination, width, height, in parameters);

        var litBelow = 0;
        var litAbove = 0;
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                if (((destination[y * width + x] >> 24) & 255) < 32)
                    continue;
                if (y > 56)
                    litBelow++;
                else if (y < 20)
                    litAbove++;
            }
        }

        Assert.True(litBelow > 0);
        Assert.True(litBelow > litAbove * 4);
    }

    [Fact]
    public void ReachLimitsCoreExtent()
    {
        using var pipeline = DielectricBreakdownPipeline.TryCreate();
        if (pipeline is null)
        {
            Assert.Skip("Direct3D 12 is unavailable.");
            return;
        }

        const int width = 192;
        const int height = 192;
        var source = CreateSquareSource(width, height, 80, 8, 32, 24);
        var shortReach = new int[source.Length];
        var longReach = new int[source.Length];

        var shortParameters = CreateParameters(reachPixels: 32f, glow: 0f);
        var longParameters = CreateParameters(reachPixels: 120f, glow: 0f);
        pipeline.Process(source, shortReach, width, height, in shortParameters);
        pipeline.Process(source, longReach, width, height, in longParameters);

        var shortMaxY = MaxLitY(shortReach, width, height);
        var longMaxY = MaxLitY(longReach, width, height);
        Assert.True(shortMaxY > 0);
        Assert.True(longMaxY > shortMaxY + 24);
        Assert.True(shortMaxY < 32 + 32 + 24);
    }

    [Fact]
    public void GpuPipelineDoesNotAllocateManagedMemoryAfterWarmup()
    {
        using var pipeline = DielectricBreakdownPipeline.TryCreate();
        if (pipeline is null)
        {
            Assert.Skip("Direct3D 12 is unavailable.");
            return;
        }

        const int width = 64;
        const int height = 64;
        var source = CreateSquareSource(width, height, 24, 8, 16, 16);
        var destination = new int[source.Length];
        var parameters = CreateParameters();
        pipeline.Process(source, destination, width, height, in parameters);
        pipeline.Process(source, destination, width, height, in parameters);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var before = GC.GetAllocatedBytesForCurrentThread();
        pipeline.Process(source, destination, width, height, in parameters);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(0, allocated);
    }

    [Fact]
    public void SharedTexturePipelineMatchesPackedBufferPipeline()
    {
        using var pipeline = DielectricBreakdownPipeline.TryCreate();
        if (pipeline is null)
        {
            Assert.Skip("Direct3D 12 is unavailable.");
            return;
        }

        const int width = 96;
        const int height = 96;
        var source = CreateSquareSource(width, height, 32, 16, 32, 32);
        var expected = new int[source.Length];
        var parameters = CreateParameters(branching: 0.6f, seed: 11);
        pipeline.Process(source, expected, width, height, in parameters);

        var device = GraphicsDevice.GetDefault();
        using var sourceTexture = InteropServices.AllocateSharedReadWriteTexture2D<Bgra32, Float4>(device, width, height);
        using var outputTexture = InteropServices.AllocateSharedReadWriteTexture2D<Bgra32, Float4>(device, width, height);
        var sourcePixels = new Bgra32[source.Length];
        for (var index = 0; index < source.Length; index++)
            sourcePixels[index].PackedValue = unchecked((uint)source[index]);
        sourceTexture.CopyFrom(sourcePixels);
        pipeline.ProcessSharedAndWait(sourceTexture, outputTexture, width, height, in parameters);
        var result = new Bgra32[source.Length];
        outputTexture.CopyTo(result);

        for (var index = 0; index < expected.Length; index++)
            Assert.Equal(unchecked((uint)expected[index]), result[index].PackedValue);
    }

    [Fact]
    public void SubmittedSharedTexturePipelineDoesNotAllocateManagedMemoryAfterWarmup()
    {
        using var pipeline = DielectricBreakdownPipeline.TryCreate();
        if (pipeline is null)
        {
            Assert.Skip("Direct3D 12 is unavailable.");
            return;
        }

        const int width = 64;
        const int height = 64;
        var device = GraphicsDevice.GetDefault();
        using var source = InteropServices.AllocateSharedReadWriteTexture2D<Bgra32, Float4>(device, width, height);
        using var destination = InteropServices.AllocateSharedReadWriteTexture2D<Bgra32, Float4>(device, width, height);
        var parameters = CreateParameters();
        for (var iteration = 0; iteration < 4; iteration++)
            pipeline.Process(source, destination, width, height, in parameters);
        pipeline.WaitForCompletion();

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var before = GC.GetAllocatedBytesForCurrentThread();
        pipeline.Process(source, destination, width, height, in parameters);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        pipeline.WaitForCompletion();

        Assert.Equal(0, allocated);
    }

    [Theory]
    [InlineData(0.3f)]
    [InlineData(1f)]
    public void VisibleBoundsCoverAllLitPixelsAndMatchFullRender(float growth)
    {
        using var pipeline = DielectricBreakdownPipeline.TryCreate();
        if (pipeline is null)
        {
            Assert.Skip("Direct3D 12 is unavailable.");
            return;
        }

        const int width = 192;
        const int height = 192;
        var source = CreateSquareSource(width, height, 80, 8, 32, 24);
        var full = new int[source.Length];
        var parameters = CreateParameters(growth: growth, reachPixels: 80f, glow: 0.6f, seed: 5);
        pipeline.Process(source, full, width, height, in parameters);

        var device = GraphicsDevice.GetDefault();
        using var sourceTexture = device.AllocateReadWriteTexture2D<Bgra32, Float4>(width, height);
        var sourcePixels = new Bgra32[source.Length];
        for (var index = 0; index < source.Length; index++)
            sourcePixels[index].PackedValue = unchecked((uint)source[index]);
        sourceTexture.CopyFrom(sourcePixels);

        pipeline.Simulate(sourceTexture, width, height, 0, 0, width, height, in parameters);
        Assert.True(pipeline.TryGetVisibleBounds(width, height, in parameters, out var rect));
        Assert.True(rect.Width > 0 && rect.Height > 0);
        Assert.True(rect.X >= 0 && rect.Y >= 0);
        Assert.True(rect.X + rect.Width <= width && rect.Y + rect.Height <= height);

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                if (full[y * width + x] == 0)
                    continue;
                Assert.InRange(x, rect.X, rect.X + rect.Width - 1);
                Assert.InRange(y, rect.Y, rect.Y + rect.Height - 1);
            }
        }

        using var outputTexture = device.AllocateReadWriteTexture2D<Bgra32, Float4>(rect.Width, rect.Height);
        pipeline.RenderVisible(outputTexture, width, height, rect, in parameters);
        var result = new Bgra32[rect.Width * rect.Height];
        outputTexture.CopyTo(result);

        for (var y = 0; y < rect.Height; y++)
        {
            for (var x = 0; x < rect.Width; x++)
            {
                var expected = unchecked((uint)full[(rect.Y + y) * width + rect.X + x]);
                Assert.Equal(expected, result[y * rect.Width + x].PackedValue);
            }
        }
    }

    [Fact]
    public void Direct2DInteropProducesLightningFromOpaqueCore()
    {
        using var devices = new GraphicsDevices();
        using var graphicsContext = devices.CreateContext();
        using var interop = DielectricBreakdownGpuInterop.TryCreate(graphicsContext);
        if (interop is null)
        {
            Assert.Skip("Direct3D 11 and Direct3D 12 sharing is unavailable.");
            return;
        }

        using var pipeline = DielectricBreakdownPipeline.TryCreate(interop.Device);
        Assert.NotNull(pipeline);

        const int width = 96;
        const int height = 96;
        var pixels = CreateSquareSource(width, height, 32, 16, 32, 32);
        var handle = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        using var inputBitmap = graphicsContext.DeviceContext.CreateBitmap(
            new SizeI(width, height),
            new BitmapProperties1(
                new PixelFormat(Format.B8G8R8A8_UNorm, Vortice.DCommon.AlphaMode.Premultiplied),
                96f,
                96f,
                BitmapOptions.None));
        try
        {
            inputBitmap.CopyFromMemory(handle.AddrOfPinnedObject(), width * sizeof(int));
        }
        finally
        {
            handle.Free();
        }

        Assert.True(interop.EnsureResources(width, height));
        var bounds = new RawRectF(0f, 0f, width, height);
        var parameters = CreateParameters(reachPixels: 40f);
        for (var iteration = 0; iteration < 2; iteration++)
        {
            interop.RenderInput(inputBitmap, bounds);
            interop.BeginCompute();
            try
            {
                pipeline!.Process(interop.SourceTexture, interop.OutputTexture, width, height, in parameters);
            }
            finally
            {
                interop.EndCompute();
            }
        }
        interop.WaitForIdle();

        using var staging = graphicsContext.DeviceContext.CreateBitmap(
            new SizeI(width, height),
            new BitmapProperties1(
                new PixelFormat(Format.B8G8R8A8_UNorm, Vortice.DCommon.AlphaMode.Premultiplied),
                96f,
                96f,
                BitmapOptions.CpuRead | BitmapOptions.CannotDraw));
        staging.CopyFromBitmap(interop.OutputBitmap);
        var mapped = staging.Map(MapOptions.Read);
        try
        {
            var lit = 0;
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var actual = Marshal.ReadInt32(mapped.Bits + (nint)(y * mapped.Pitch + x * sizeof(int)));
                    var alpha = (actual >> 24) & 255;
                    Assert.InRange((actual >> 16) & 255, 0, alpha);
                    Assert.InRange((actual >> 8) & 255, 0, alpha);
                    Assert.InRange(actual & 255, 0, alpha);
                    if (alpha > 0)
                        lit++;
                }
            }
            Assert.True(lit > 0);
        }
        finally
        {
            staging.Unmap();
        }
    }

    private static int[] CreateSquareSource(int width, int height, int left, int top, int squareWidth, int squareHeight)
    {
        var source = new int[width * height];
        for (var y = top; y < top + squareHeight; y++)
        {
            for (var x = left; x < left + squareWidth; x++)
            {
                if (x < 0 || x >= width || y < 0 || y >= height)
                    continue;
                source[y * width + x] = unchecked((int)0xFFC0C0C0);
            }
        }
        return source;
    }

    private static int CountLitPixels(int[] pixels)
    {
        var count = 0;
        foreach (var pixel in pixels)
        {
            if (((pixel >> 24) & 255) > 8)
                count++;
        }
        return count;
    }

    private static int MaxLitY(int[] pixels, int width, int height)
    {
        for (var y = height - 1; y >= 0; y--)
        {
            for (var x = 0; x < width; x++)
            {
                if (((pixels[y * width + x] >> 24) & 255) >= 32)
                    return y;
            }
        }
        return 0;
    }
}
