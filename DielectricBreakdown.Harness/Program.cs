using System.Diagnostics;
using ComputeSharp;
using DielectricBreakdown;

var width = 1280;
var height = 720;
var outputDirectory = args.Length > 0 ? args[0] : Path.Combine(AppContext.BaseDirectory, "harness-output");
Directory.CreateDirectory(outputDirectory);

using var pipeline = DielectricBreakdownPipeline.TryCreate();
if (pipeline is null)
{
    Console.WriteLine("Direct3D 12 is unavailable.");
    return 1;
}

var source = CreateTestImage(width, height);
var destination = new int[source.Length];

foreach (var quality in new[] { DielectricBreakdownQuality.Balanced, DielectricBreakdownQuality.High, DielectricBreakdownQuality.Ultra })
{
    var parameters = new DielectricBreakdownPipeline.Parameters(quality, 1f, 0.5f, 0f, 200f, 2.5f, 0.5f, 0.7f, 0.8f, 1f, 7);
    pipeline.Process(source, destination, width, height, in parameters);
    pipeline.Process(source, destination, width, height, in parameters);
    var stopwatch = Stopwatch.StartNew();
    const int frames = 10;
    for (var frame = 0; frame < frames; frame++)
        pipeline.Process(source, destination, width, height, in parameters);
    stopwatch.Stop();
    Console.WriteLine($"{quality}: {stopwatch.Elapsed.TotalMilliseconds / frames:F2} ms/frame ({width}x{height})");
}

{
    var device = ComputeSharp.GraphicsDevice.GetDefault();
    using var sourceTexture = ComputeSharp.Interop.InteropServices.AllocateSharedReadWriteTexture2D<ComputeSharp.Bgra32, ComputeSharp.Float4>(device, width, height);
    using var outputTexture = ComputeSharp.Interop.InteropServices.AllocateSharedReadWriteTexture2D<ComputeSharp.Bgra32, ComputeSharp.Float4>(device, width, height);
    var pixels = new ComputeSharp.Bgra32[source.Length];
    for (var index = 0; index < source.Length; index++)
        pixels[index].PackedValue = unchecked((uint)source[index]);
    sourceTexture.CopyFrom(pixels);
    foreach (var quality in new[] { DielectricBreakdownQuality.Balanced, DielectricBreakdownQuality.High, DielectricBreakdownQuality.Ultra })
    {
        var parameters = new DielectricBreakdownPipeline.Parameters(quality, 1f, 0.5f, 0f, 200f, 2.5f, 0.5f, 0.7f, 0.8f, 1f, 7);
        pipeline.Process(sourceTexture, outputTexture, width, height, in parameters);
        pipeline.WaitForCompletion();
        var stopwatch = Stopwatch.StartNew();
        const int frames = 20;
        for (var frame = 0; frame < frames; frame++)
            pipeline.Process(sourceTexture, outputTexture, width, height, in parameters);
        pipeline.WaitForCompletion();
        stopwatch.Stop();
        outputTexture.CopyTo(pixels);
        Console.WriteLine($"shared {quality}: {stopwatch.Elapsed.TotalMilliseconds / frames:F2} ms/frame ({width}x{height})");
    }
}

{
    var device = ComputeSharp.GraphicsDevice.GetDefault();
    const int canvas = 4096;
    const int item = 400;
    var margin = (canvas - item) / 2;
    var bigPixels = new ComputeSharp.Bgra32[item * item];
    for (var y = 0; y < 120; y++)
        for (var x = 0; x < item; x++)
            bigPixels[y * item + x].PackedValue = 0xFFB0B0B8u;
    using var bigSource = device.AllocateReadWriteTexture2D<ComputeSharp.Bgra32, ComputeSharp.Float4>(item, item);
    bigSource.CopyFrom(bigPixels);
    using var fullSource = device.AllocateReadWriteTexture2D<ComputeSharp.Bgra32, ComputeSharp.Float4>(canvas, canvas);
    using var fullOutput = device.AllocateReadWriteTexture2D<ComputeSharp.Bgra32, ComputeSharp.Float4>(canvas, canvas);
    var parameters = new DielectricBreakdownPipeline.Parameters(DielectricBreakdownQuality.High, 1f, 0.5f, 0f, 1600f, 2.5f, 0.5f, 0.7f, 0.8f, 1f, 7);

    pipeline.Process(fullSource, fullOutput, canvas, canvas, in parameters);
    pipeline.WaitForCompletion();
    var stopwatch = Stopwatch.StartNew();
    const int fullFrames = 5;
    for (var frame = 0; frame < fullFrames; frame++)
        pipeline.Process(fullSource, fullOutput, canvas, canvas, in parameters);
    pipeline.WaitForCompletion();
    stopwatch.Stop();
    Console.WriteLine($"full canvas {canvas}x{canvas}: {stopwatch.Elapsed.TotalMilliseconds / fullFrames:F2} ms/frame");

    pipeline.Simulate(bigSource, canvas, canvas, margin, margin, item, item, in parameters);
    if (pipeline.TryGetVisibleBounds(canvas, canvas, in parameters, out var rect))
    {
        using var rectOutput = device.AllocateReadWriteTexture2D<ComputeSharp.Bgra32, ComputeSharp.Float4>(rect.Width, rect.Height);
        pipeline.RenderVisible(rectOutput, canvas, canvas, rect, in parameters);
        pipeline.WaitForCompletion();
        stopwatch.Restart();
        const int rectFrames = 10;
        for (var frame = 0; frame < rectFrames; frame++)
        {
            pipeline.Simulate(bigSource, canvas, canvas, margin, margin, item, item, in parameters);
            pipeline.TryGetVisibleBounds(canvas, canvas, in parameters, out rect);
            pipeline.RenderVisible(rectOutput, canvas, canvas, rect, in parameters);
        }
        pipeline.WaitForCompletion();
        stopwatch.Stop();
        Console.WriteLine($"visible rect {rect.Width}x{rect.Height} at ({rect.X},{rect.Y}): {stopwatch.Elapsed.TotalMilliseconds / rectFrames:F2} ms/frame");
    }
}

foreach (var growth in new[] { 0.25f, 0.5f, 0.75f, 0.95f, 1f })
{
    var parameters = new DielectricBreakdownPipeline.Parameters(DielectricBreakdownQuality.High, growth, 0.5f, 0f, 200f, 2.5f, 0.5f, 0.7f, 0.8f, 1f, 7);
    pipeline.Process(source, destination, width, height, in parameters);
    Console.WriteLine($"growth={growth:F2} litPixels={CountLit(destination)}");
    WriteBmp(Path.Combine(outputDirectory, $"growth{(int)(growth * 100):D3}.bmp"), Composite(source, destination), width, height);
}

foreach (var branching in new[] { 0f, 0.5f, 1f })
{
    var parameters = new DielectricBreakdownPipeline.Parameters(DielectricBreakdownQuality.High, 1f, branching, 0f, 200f, 2.5f, 0.5f, 0.7f, 0.8f, 1f, 7);
    pipeline.Process(source, destination, width, height, in parameters);
    Console.WriteLine($"branching={branching:F2} litPixels={CountLit(destination)}");
    WriteBmp(Path.Combine(outputDirectory, $"branching{(int)(branching * 100):D3}.bmp"), Composite(source, destination), width, height);
}

foreach (var angle in new[] { 90f, 180f, 270f })
{
    var parameters = new DielectricBreakdownPipeline.Parameters(DielectricBreakdownQuality.High, 1f, 0.5f, angle, 200f, 2.5f, 0.5f, 0.7f, 0.8f, 1f, 7);
    pipeline.Process(source, destination, width, height, in parameters);
    WriteBmp(Path.Combine(outputDirectory, $"angle{(int)angle:D3}.bmp"), Composite(source, destination), width, height);
}

WriteBmp(Path.Combine(outputDirectory, "source.bmp"), source, width, height);
Console.WriteLine($"images written to {outputDirectory}");
return 0;

static int[] CreateTestImage(int width, int height)
{
    var pixels = new int[width * height];
    var left = width / 2 - 100;
    var top = height / 4 - 40;
    for (var y = top; y < top + 80; y++)
    {
        for (var x = left; x < left + 200; x++)
        {
            if (x < 0 || x >= width || y < 0 || y >= height)
                continue;
            pixels[y * width + x] = unchecked((int)0xFFB0B0B8);
        }
    }
    return pixels;
}

static int CountLit(int[] pixels)
{
    var count = 0;
    foreach (var pixel in pixels)
    {
        if (((pixel >> 24) & 255) > 8)
            count++;
    }
    return count;
}

static int[] Composite(int[] source, int[] lightning)
{
    var result = new int[source.Length];
    for (var index = 0; index < source.Length; index++)
    {
        var s = source[index];
        var l = lightning[index];
        var sa = (s >> 24) & 255;
        var la = (l >> 24) & 255;
        var a = sa + la - sa * la / 255;
        var r = Screen((s >> 16) & 255, (l >> 16) & 255);
        var g = Screen((s >> 8) & 255, (l >> 8) & 255);
        var b = Screen(s & 255, l & 255);
        result[index] = Math.Min(a, 255) << 24 | r << 16 | g << 8 | b;
    }
    return result;

    static int Screen(int s, int l) => Math.Min(s + l - s * l / 255, 255);
}

static void WriteBmp(string path, int[] pixels, int width, int height)
{
    var stride = width * 3;
    var padding = (4 - stride % 4) % 4;
    var dataSize = (stride + padding) * height;
    using var stream = new FileStream(path, FileMode.Create, FileAccess.Write);
    using var writer = new BinaryWriter(stream);
    writer.Write((byte)'B');
    writer.Write((byte)'M');
    writer.Write(54 + dataSize);
    writer.Write(0);
    writer.Write(54);
    writer.Write(40);
    writer.Write(width);
    writer.Write(height);
    writer.Write((short)1);
    writer.Write((short)24);
    writer.Write(0);
    writer.Write(dataSize);
    writer.Write(2835);
    writer.Write(2835);
    writer.Write(0);
    writer.Write(0);
    var pad = new byte[padding];
    for (var y = height - 1; y >= 0; y--)
    {
        for (var x = 0; x < width; x++)
        {
            var pixel = pixels[y * width + x];
            writer.Write((byte)(pixel & 255));
            writer.Write((byte)((pixel >> 8) & 255));
            writer.Write((byte)((pixel >> 16) & 255));
        }
        writer.Write(pad);
    }
}
