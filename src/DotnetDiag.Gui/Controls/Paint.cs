using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Shapes;
using DotnetDiag.Gui.Theming;

namespace DotnetDiag.Gui.Controls;

/// <summary>
/// Attached properties that color an element with a <see cref="Tone"/>. They bind to the theme's resources,
/// so a data-bound row repaints when the theme changes.
/// </summary>
public static class Paint
{
    /// <summary>Text color of a TextBlock.</summary>
    public static readonly AttachedProperty<Tone> TextProperty =
        AvaloniaProperty.RegisterAttached<TextBlock, Tone>("Text", typeof(Paint));

    /// <summary>A Border as a badge: faint tinted background with a border in the tone.</summary>
    public static readonly AttachedProperty<Tone> BadgeProperty =
        AvaloniaProperty.RegisterAttached<Border, Tone>("Badge", typeof(Paint));

    /// <summary>A Border filled solidly with the tone (severity stripes, swatches).</summary>
    public static readonly AttachedProperty<Tone> FillProperty =
        AvaloniaProperty.RegisterAttached<Border, Tone>("Fill", typeof(Paint));

    /// <summary>Only the border of a Border, e.g. the outline of a hint card.</summary>
    public static readonly AttachedProperty<Tone> OutlineProperty =
        AvaloniaProperty.RegisterAttached<Border, Tone>("Outline", typeof(Paint));

    /// <summary>Stroke of a shape, e.g. an icon.</summary>
    public static readonly AttachedProperty<Tone> StrokeProperty =
        AvaloniaProperty.RegisterAttached<Shape, Tone>("Stroke", typeof(Paint));

    static Paint()
    {
        TextProperty.Changed.AddClassHandler<TextBlock>((block, e) =>
            Bind(block, TextBlock.ForegroundProperty, e.GetNewValue<Tone>(), ThemeManager.Text));
        BadgeProperty.Changed.AddClassHandler<Border>((border, e) =>
        {
            Bind(border, Border.BackgroundProperty, e.GetNewValue<Tone>(), ThemeManager.Soft);
            Bind(border, Border.BorderBrushProperty, e.GetNewValue<Tone>());
        });
        FillProperty.Changed.AddClassHandler<Border>((border, e) =>
            Bind(border, Border.BackgroundProperty, e.GetNewValue<Tone>()));
        OutlineProperty.Changed.AddClassHandler<Border>((border, e) =>
            Bind(border, Border.BorderBrushProperty, e.GetNewValue<Tone>()));
        StrokeProperty.Changed.AddClassHandler<Shape>((shape, e) =>
            Bind(shape, Shape.StrokeProperty, e.GetNewValue<Tone>()));
    }

    public static Tone GetText(TextBlock element) => element.GetValue(TextProperty);

    public static void SetText(TextBlock element, Tone value) => element.SetValue(TextProperty, value);

    public static Tone GetBadge(Border element) => element.GetValue(BadgeProperty);

    public static void SetBadge(Border element, Tone value) => element.SetValue(BadgeProperty, value);

    public static Tone GetFill(Border element) => element.GetValue(FillProperty);

    public static void SetFill(Border element, Tone value) => element.SetValue(FillProperty, value);

    public static Tone GetOutline(Border element) => element.GetValue(OutlineProperty);

    public static void SetOutline(Border element, Tone value) => element.SetValue(OutlineProperty, value);

    public static Tone GetStroke(Shape element) => element.GetValue(StrokeProperty);

    public static void SetStroke(Shape element, Tone value) => element.SetValue(StrokeProperty, value);

    /// <summary>Colors a text run, which is not a control and so cannot carry the attached properties.</summary>
    public static void Foreground(Run run, Tone tone) => Bind(run, TextElement.ForegroundProperty, tone, ThemeManager.Text);

    /// <summary>Binds <paramref name="property"/> to a theme resource by key.</summary>
    /// <remarks>
    /// The lookup goes through the element itself, like DynamicResource does, so the subscription lives and dies
    /// with the element. Observing the application's resources instead would keep every painted row alive forever.
    /// </remarks>
    public static void BindResource(StyledElement target, AvaloniaProperty property, string key) =>
        target.Bind(property, target.GetResourceObservable(key));

    private static void Bind(StyledElement target, AvaloniaProperty property, Tone tone, string variant = ThemeManager.Solid)
    {
        if (tone == Tone.None)
        {
            target.ClearValue(property);
        }
        else
        {
            BindResource(target, property, ThemeManager.BrushKey(tone, variant));
        }
    }
}
