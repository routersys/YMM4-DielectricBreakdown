using System.Numerics;
using Vortice.Direct2D1;
using Vortice.Direct2D1.Effects;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Player.Video;
using YukkuriMovieMaker.Player.Video.Effects;

namespace DielectricBreakdown;

internal sealed class DielectricBreakdownEffectProcessor : VideoEffectProcessorBase
{
    private readonly IGraphicsDevicesAndContext _devices;
    private readonly DielectricBreakdownEffect _item;
    private DielectricBreakdownGpuInterop? _interop;
    private DielectricBreakdownPipeline? _pipeline;
    private DielectricBreakdownCustomEffect? _effect;
    private Crop? _outputCrop;
    private ID2D1Image? _outputCropOutput;
    private AffineTransform2D? _outputTransform;
    private ID2D1Image? _outputTransformOutput;
    private bool _isFirst = true;
    private bool _hasOutput;
    private bool _hasOutputOffset;
    private bool _hasCropRect;
    private Vector2 _outputOffset;
    private Vector4 _cropRect;
    private Parameters _parameters;

    public DielectricBreakdownEffectProcessor(IGraphicsDevicesAndContext devices, DielectricBreakdownEffect item)
        : base(devices)
    {
        _devices = devices;
        _item = item;
    }

    public override DrawDescription Update(EffectDescription effectDescription)
    {
        if (IsPassThroughEffect || _effect is null || _outputCrop is null || _outputTransform is null || _outputTransformOutput is null || _interop is null || _pipeline is null || input is null)
            return effectDescription.DrawDescription;

        var frame = effectDescription.ItemPosition.Frame;
        var length = effectDescription.ItemDuration.Frame;
        var fps = effectDescription.FPS;
        var parameters = new Parameters(
            (float)(_item.Amount.GetValue(frame, length, fps) / 100.0),
            (float)(_item.Growth.GetValue(frame, length, fps) / 100.0),
            _item.Quality,
            (float)_item.Angle.GetValue(frame, length, fps),
            (float)(_item.Reach.GetValue(frame, length, fps) / 100.0),
            (float)(_item.Branching.GetValue(frame, length, fps) / 100.0),
            (float)_item.Thickness.GetValue(frame, length, fps),
            (float)(_item.Glow.GetValue(frame, length, fps) / 100.0),
            _item.LightningColor,
            _item.Seed);

        if (_isFirst || _parameters.Amount != parameters.Amount)
            _effect.Amount = parameters.Amount;

        if (parameters.Amount <= 0f || parameters.Growth <= 0f)
        {
            _effect.Amount = 0f;
            _parameters = parameters;
            _isFirst = true;
            return effectDescription.DrawDescription;
        }

        var bounds = _devices.DeviceContext.GetImageLocalBounds(input);
        var widthValue = Math.Ceiling((double)bounds.Right - bounds.Left);
        var heightValue = Math.Ceiling((double)bounds.Bottom - bounds.Top);
        if (!double.IsFinite(widthValue) || !double.IsFinite(heightValue) ||
            !float.IsFinite(bounds.Left) || !float.IsFinite(bounds.Top) ||
            widthValue <= 0d || heightValue <= 0d)
        {
            _effect.Amount = 0f;
            _isFirst = true;
            return effectDescription.DrawDescription;
        }

        var reach = Math.Clamp(parameters.Reach, 0.01f, 4f);
        var glow = Math.Clamp(parameters.Glow, 0f, 1f);
        var thickness = Math.Clamp(parameters.Thickness, 0.05f, 100f);
        var longSide = Math.Max(widthValue, heightValue);
        var marginLimit = (DielectricBreakdownSettings.MaximumCanvasSize - longSide) / 2d;
        if (marginLimit < 16d)
        {
            _effect.Amount = 0f;
            _isFirst = true;
            return effectDescription.DrawDescription;
        }
        var padding = glow * 60d + thickness + 8d;
        var reachPixels = Math.Min(reach * longSide, Math.Max(marginLimit - padding, 1d));
        var margin = (int)Math.Ceiling(reachPixels + padding);
        var canvasWidthValue = widthValue + margin * 2d;
        var canvasHeightValue = heightValue + margin * 2d;
        if (canvasWidthValue * canvasHeightValue > int.MaxValue)
        {
            _effect.Amount = 0f;
            _isFirst = true;
            return effectDescription.DrawDescription;
        }
        var canvasWidth = (int)canvasWidthValue;
        var canvasHeight = (int)canvasHeightValue;
        var itemWidth = (int)widthValue;
        var itemHeight = (int)heightValue;

        _interop.EnsureSource(itemWidth, itemHeight);
        _interop.RenderInput(input, new Vortice.RawRectF(bounds.Left, bounds.Top, bounds.Left + itemWidth, bounds.Top + itemHeight));

        var pipelineParameters = new DielectricBreakdownPipeline.Parameters(
            parameters.Quality,
            Math.Clamp(parameters.Growth, 0f, 1f),
            Math.Clamp(parameters.Branching, 0f, 1f),
            parameters.Angle,
            (float)reachPixels,
            thickness,
            glow,
            parameters.Color.R / 255f,
            parameters.Color.G / 255f,
            parameters.Color.B / 255f,
            Math.Max(parameters.Seed, 0));

        _interop.BeginCompute();
        try
        {
            _pipeline.Simulate(
                _interop.SourceTexture,
                canvasWidth,
                canvasHeight,
                margin,
                margin,
                itemWidth,
                itemHeight,
                in pipelineParameters);
        }
        finally
        {
            _interop.EndCompute();
        }

        if (!_pipeline.TryGetVisibleBounds(canvasWidth, canvasHeight, in pipelineParameters, out var rect))
        {
            _effect.Amount = 0f;
            _parameters = parameters;
            _isFirst = true;
            return effectDescription.DrawDescription;
        }

        if (!_interop.OutputCovers(rect.Width, rect.Height))
            _outputCrop.SetInput(0, null, true);
        var outputChanged = _interop.EnsureOutput(rect.Width, rect.Height);
        _interop.BeginCompute();
        try
        {
            _pipeline.RenderVisible(
                _interop.OutputTexture,
                canvasWidth,
                canvasHeight,
                rect,
                in pipelineParameters);
        }
        finally
        {
            _interop.EndCompute();
        }

        if (outputChanged || !_hasOutput)
        {
            _outputCrop.SetInput(0, _interop.OutputBitmap, true);
            _effect.SetInput(1, _outputTransformOutput, true);
        }
        var cropRect = new Vector4(0f, 0f, rect.Width, rect.Height);
        if (!_hasCropRect || _cropRect != cropRect)
        {
            _outputCrop.Rectangle = cropRect;
            _cropRect = cropRect;
            _hasCropRect = true;
        }
        var outputOffset = new Vector2(bounds.Left - margin + rect.X, bounds.Top - margin + rect.Y);
        if (!_hasOutputOffset || _outputOffset != outputOffset)
        {
            _outputTransform.TransformMatrix = Matrix3x2.CreateTranslation(outputOffset);
            _outputOffset = outputOffset;
            _hasOutputOffset = true;
        }
        _hasOutput = true;
        _parameters = parameters;
        _isFirst = false;
        return effectDescription.DrawDescription;
    }

    protected override ID2D1Image? CreateEffect(IGraphicsDevicesAndContext devices)
    {
        var interop = DielectricBreakdownGpuInterop.TryCreate(devices);
        if (interop is null)
            return null;
        var pipeline = DielectricBreakdownPipeline.TryCreate(interop.Device);
        if (pipeline is null)
        {
            interop.Dispose();
            return null;
        }

        DielectricBreakdownCustomEffect? effect = null;
        Crop? outputCrop = null;
        ID2D1Image? outputCropOutput = null;
        AffineTransform2D? outputTransform = null;
        ID2D1Image? outputTransformOutput = null;
        ID2D1Image? output = null;
        try
        {
            effect = new DielectricBreakdownCustomEffect(devices);
            if (!effect.IsEnabled)
            {
                effect.Dispose();
                pipeline.Dispose();
                interop.Dispose();
                return null;
            }
            outputCrop = new Crop(devices.DeviceContext);
            outputCropOutput = outputCrop.Output;
            outputTransform = new AffineTransform2D(devices.DeviceContext)
            {
                BorderMode = BorderMode.Hard,
            };
            outputTransform.SetInput(0, outputCropOutput, true);
            outputTransformOutput = outputTransform.Output;
            output = effect.Output;
            _interop = interop;
            _pipeline = pipeline;
            _effect = effect;
            _outputCrop = outputCrop;
            _outputCropOutput = outputCropOutput;
            _outputTransform = outputTransform;
            _outputTransformOutput = outputTransformOutput;
            disposer.Collect(effect);
            disposer.Collect(outputCrop);
            disposer.Collect(outputCropOutput);
            disposer.Collect(outputTransform);
            disposer.Collect(outputTransformOutput);
            disposer.Collect(output);
            return output;
        }
        catch
        {
            output?.Dispose();
            outputTransformOutput?.Dispose();
            outputTransform?.Dispose();
            outputCropOutput?.Dispose();
            outputCrop?.Dispose();
            effect?.Dispose();
            pipeline.Dispose();
            interop.Dispose();
            throw;
        }
    }

    protected override void setInput(ID2D1Image? inputImage)
    {
        _effect?.SetInput(0, inputImage, true);
        if (!_hasOutput)
            _effect?.SetInput(1, inputImage, true);
    }

    protected override void ClearEffectChain()
    {
        _effect?.SetInput(0, null, true);
        _effect?.SetInput(1, null, true);
        _outputCrop?.SetInput(0, null, true);
        _isFirst = true;
        _hasOutput = false;
        _hasOutputOffset = false;
        _hasCropRect = false;
    }

    protected override void Dispose(bool disposing)
    {
        try
        {
            if (disposing)
            {
                ClearEffectChain();
                _interop?.WaitForIdle();
                _pipeline?.Dispose();
                _pipeline = null;
                _interop?.Dispose();
                _interop = null;
            }
        }
        finally
        {
            base.Dispose(disposing);
        }
    }

    private readonly record struct Parameters(
        float Amount,
        float Growth,
        DielectricBreakdownQuality Quality,
        float Angle,
        float Reach,
        float Branching,
        float Thickness,
        float Glow,
        System.Windows.Media.Color Color,
        int Seed);
}
