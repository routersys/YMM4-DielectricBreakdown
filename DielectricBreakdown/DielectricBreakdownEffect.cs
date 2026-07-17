using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Windows.Media;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Controls;
using YukkuriMovieMaker.Exo;
using YukkuriMovieMaker.Player.Video;
using YukkuriMovieMaker.Plugin.Effects;

namespace DielectricBreakdown;

[VideoEffect(nameof(Texts.DielectricBreakdown), [VideoEffectCategories.Decoration, VideoEffectCategories.Animation], [nameof(Texts.TagLightning), nameof(Texts.TagElectric), nameof(Texts.TagDischarge)], IsAviUtlSupported = false, ResourceType = typeof(Texts))]
public sealed class DielectricBreakdownEffect : VideoEffectBase
{
    public override string Label => Texts.DielectricBreakdown;

    public DielectricBreakdownEffect()
    {
        DielectricBreakdownUpdateNotifier.EnsureCheckedOnce();
    }

    [Display(GroupName = nameof(Texts.BasicGroup), Name = nameof(Texts.Amount), Description = nameof(Texts.AmountDescription), Order = 0, ResourceType = typeof(Texts))]
    [AnimationSlider("F1", "%", 0, 100)]
    public Animation Amount { get; } = new Animation(100, 0, 100);

    [Display(GroupName = nameof(Texts.BasicGroup), Name = nameof(Texts.Growth), Description = nameof(Texts.GrowthDescription), Order = 1, ResourceType = typeof(Texts))]
    [AnimationSlider("F1", "%", 0, 100)]
    public Animation Growth { get; } = new Animation(100, 0, 100);

    [Display(GroupName = nameof(Texts.BasicGroup), Name = nameof(Texts.Quality), Description = nameof(Texts.QualityDescription), Order = 2, ResourceType = typeof(Texts))]
    [EnumComboBox]
    public DielectricBreakdownQuality Quality { get => _quality; set => Set(ref _quality, value); }
    private DielectricBreakdownQuality _quality = DielectricBreakdownQuality.High;

    [Display(GroupName = nameof(Texts.DischargeGroup), Name = nameof(Texts.Angle), Description = nameof(Texts.AngleDescription), Order = 10, ResourceType = typeof(Texts))]
    [AnimationSlider("F1", "°", -180, 180)]
    public Animation Angle { get; } = new Animation(0, -36000, 36000);

    [Display(GroupName = nameof(Texts.DischargeGroup), Name = nameof(Texts.Reach), Description = nameof(Texts.ReachDescription), Order = 11, ResourceType = typeof(Texts))]
    [AnimationSlider("F1", "%", 10, 200)]
    public Animation Reach { get; } = new Animation(75, 1, 400);

    [Display(GroupName = nameof(Texts.DischargeGroup), Name = nameof(Texts.Branching), Description = nameof(Texts.BranchingDescription), Order = 12, ResourceType = typeof(Texts))]
    [AnimationSlider("F1", "%", 0, 100)]
    public Animation Branching { get; } = new Animation(50, 0, 100);

    [Display(GroupName = nameof(Texts.DischargeGroup), Name = nameof(Texts.Seed), Description = nameof(Texts.SeedDescription), Order = 13, ResourceType = typeof(Texts))]
    [Range(0, int.MaxValue)]
    [DefaultValue(0)]
    [TextBoxSlider("F0", "", 0, 10000)]
    public int Seed
    {
        get => _seed;
        set => Set(ref _seed, Math.Max(value, 0));
    }
    private int _seed;

    [Display(GroupName = nameof(Texts.AppearanceGroup), Name = nameof(Texts.Thickness), Description = nameof(Texts.ThicknessDescription), Order = 20, ResourceType = typeof(Texts))]
    [AnimationSlider("F2", "px", 0.5, 10)]
    public Animation Thickness { get; } = new Animation(2.5, 0.1, 100);

    [Display(GroupName = nameof(Texts.AppearanceGroup), Name = nameof(Texts.Glow), Description = nameof(Texts.GlowDescription), Order = 21, ResourceType = typeof(Texts))]
    [AnimationSlider("F1", "%", 0, 100)]
    public Animation Glow { get; } = new Animation(50, 0, 100);

    [Display(GroupName = nameof(Texts.AppearanceGroup), Name = nameof(Texts.LightningColor), Description = nameof(Texts.LightningColorDescription), Order = 22, ResourceType = typeof(Texts))]
    [ColorPicker]
    public Color LightningColor
    {
        get => _lightningColor;
        set => Set(ref _lightningColor, value);
    }
    private Color _lightningColor = Color.FromArgb(255, 180, 200, 255);

    private IAnimatable[]? _animatables;

    public override IEnumerable<string> CreateExoVideoFilters(int keyFrameIndex, ExoOutputDescription exoOutputDescription) => [];

    public override IVideoEffectProcessor CreateVideoEffect(IGraphicsDevicesAndContext devices)
        => new DielectricBreakdownEffectProcessor(devices, this);

    protected override IEnumerable<IAnimatable> GetAnimatables()
        => _animatables ??= [Amount, Growth, Angle, Reach, Branching, Thickness, Glow];
}
