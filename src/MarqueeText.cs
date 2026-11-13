using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace RadioPlayer;

/// <summary>
/// One line of text that, when it is too long to fit, scrolls slowly to its end, rests, and scrolls
/// back. Text that fits is simply shown. Font and colour come from the TextElement.* properties
/// set on it (or inherited), as with a TextBlock.
/// </summary>
public sealed class MarqueeText : FrameworkElement
{
    private const double PixelsPerSecond = 25;
    private static readonly TimeSpan Rest = TimeSpan.FromSeconds(2.5); // pause at each end

    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(MarqueeText),
        new FrameworkPropertyMetadata(
            "",
            FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsArrange,
            (d, e) => ((MarqueeText)d).OnTextChanged((string?)e.NewValue)));

    private readonly TextBlock _text = new();
    private readonly TranslateTransform _shift = new();
    private double _overflow = double.NaN; // how far the text scrolls; 0 when it fits, NaN when not yet known

    public MarqueeText()
    {
        ClipToBounds = true;
        _text.RenderTransform = _shift;
        AddVisualChild(_text);
    }

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    private void OnTextChanged(string? text)
    {
        _text.Text = text ?? "";
        _overflow = double.NaN; // new text starts again from its beginning
    }

    protected override int VisualChildrenCount => 1;

    protected override Visual GetVisualChild(int index) => _text;

    protected override Size MeasureOverride(Size availableSize)
    {
        _text.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var full = _text.DesiredSize;
        return new Size(Math.Min(full.Width, availableSize.Width), Math.Min(full.Height, availableSize.Height));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var full = _text.DesiredSize;
        _text.Arrange(new Rect(0, 0, Math.Max(full.Width, finalSize.Width), Math.Max(full.Height, finalSize.Height)));
        Scroll(full.Width - finalSize.Width);
        return finalSize;
    }

    private void Scroll(double overflow)
    {
        if (overflow < 1) overflow = 0;
        if (Math.Abs(overflow - _overflow) < 0.5) return; // already scrolling this far (false for NaN)
        _overflow = overflow;

        if (overflow == 0)
        {
            _shift.BeginAnimation(TranslateTransform.XProperty, null);
            _shift.X = 0;
            return;
        }

        // rest → scroll to the end → rest → scroll back, forever
        var travel = TimeSpan.FromSeconds(overflow / PixelsPerSecond);
        var at = Rest;
        var animation = new DoubleAnimationUsingKeyFrames { RepeatBehavior = RepeatBehavior.Forever };
        animation.KeyFrames.Add(new DiscreteDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        animation.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(at)));
        animation.KeyFrames.Add(new LinearDoubleKeyFrame(-overflow, KeyTime.FromTimeSpan(at += travel)));
        animation.KeyFrames.Add(new LinearDoubleKeyFrame(-overflow, KeyTime.FromTimeSpan(at += Rest)));
        animation.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(at + travel)));
        Timeline.SetDesiredFrameRate(animation, 30);
        _shift.BeginAnimation(TranslateTransform.XProperty, animation);
    }
}
