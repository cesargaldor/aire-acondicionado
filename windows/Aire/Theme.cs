// Colores y piezas visuales compartidas (mismo estilo que la versión de Mac).

using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MideaKit;

namespace Aire;

internal static class Theme
{
    public static readonly Color BackgroundColor = Color.FromRgb(0x13, 0x12, 0x0F);
    public static readonly Color CreamColor = Color.FromRgb(0xF0, 0xEB, 0xD9);
    public static readonly Color OliveColor = Color.FromRgb(0xB3, 0xA3, 0x70);
    public static readonly Color EmberColor = Color.FromRgb(0xE6, 0x8F, 0x4D);

    public static readonly Brush Background = Freeze(new SolidColorBrush(BackgroundColor));
    public static readonly Brush Cream = Freeze(new SolidColorBrush(CreamColor));
    public static readonly Brush Muted = Freeze(new SolidColorBrush(Color.FromArgb(140, 0xF0, 0xEB, 0xD9)));
    public static readonly Brush Card = Freeze(new SolidColorBrush(Color.FromArgb(12, 255, 255, 255)));
    public static readonly Brush CardHover = Freeze(new SolidColorBrush(Color.FromArgb(22, 255, 255, 255)));
    public static readonly Brush Stroke = Freeze(new SolidColorBrush(Color.FromArgb(18, 255, 255, 255)));
    public static readonly Brush Field = Freeze(new SolidColorBrush(Color.FromRgb(0x22, 0x20, 0x1B)));
    public static readonly Brush Ember = Freeze(new SolidColorBrush(EmberColor));

    public static readonly FontFamily Text = new("Segoe UI Variable Text, Segoe UI");
    public static readonly FontFamily Display = new("Segoe UI Variable Display, Segoe UI");
    public static readonly FontFamily Icons = new("Segoe Fluent Icons, Segoe MDL2 Assets");
    public static readonly FontFamily Emoji = new("Segoe UI Emoji, Segoe UI Symbol");

    // Glifos de Segoe MDL2 / Fluent Icons (existen en Windows 10 y 11)
    public const string GlyphPower = "";
    public const string GlyphPlus = "";
    public const string GlyphMinus = "";
    public const string GlyphSettings = "";
    public const string GlyphChevronDown = "";
    public const string GlyphChevronUp = "";
    public const string GlyphCheck = "";
    public const string GlyphWarning = "";

    public static Color AccentColor(ACStatus s) => s.Mode == (byte)ACMode.Heat ? EmberColor : OliveColor;
    public static Brush Accent(ACStatus s) => Solid(AccentColor(s));

    public static Brush Solid(Color c, byte alpha = 255) => Freeze(new SolidColorBrush(Color.FromArgb(alpha, c.R, c.G, c.B)));

    public static string ModeEmoji(ACMode m) => m switch
    {
        ACMode.Auto => "Ⓐ",      // Ⓐ
        ACMode.Cool => "❄",      // ❄
        ACMode.Dry => "\U0001F4A7",   // 💧
        ACMode.Heat => "\U0001F525",  // 🔥
        ACMode.Fan => "\U0001F4A8",   // 💨
        _ => "❄",
    };

    private static T Freeze<T>(T f) where T : Freezable
    {
        f.Freeze();
        return f;
    }

    // MARK: Fábricas de elementos

    public static TextBlock Label(string text, double size = 13, Brush? color = null, FontWeight? weight = null,
        FontFamily? family = null) => new()
    {
        Text = text,
        FontSize = size,
        Foreground = color ?? Cream,
        FontWeight = weight ?? FontWeights.Normal,
        FontFamily = family ?? Text,
        VerticalAlignment = VerticalAlignment.Center,
        TextTrimming = TextTrimming.CharacterEllipsis,
    };

    public static TextBlock Icon(string glyph, double size = 14, Brush? color = null) => new()
    {
        Text = glyph,
        FontFamily = Icons,
        FontSize = size,
        Foreground = color ?? Cream,
        VerticalAlignment = VerticalAlignment.Center,
        HorizontalAlignment = HorizontalAlignment.Center,
    };

    public static TextBlock EmojiIcon(string text, double size = 16) => new()
    {
        Text = text,
        FontFamily = Emoji,
        FontSize = size,
        Foreground = Cream,
        VerticalAlignment = VerticalAlignment.Center,
    };

    public static Border CardBorder(UIElement? child = null, double radius = 14, Brush? fill = null) => new()
    {
        Child = child,
        CornerRadius = new CornerRadius(radius),
        Background = fill ?? Card,
        BorderBrush = Stroke,
        BorderThickness = new Thickness(1),
    };

    /// Convierte un borde en un botón: cursor de mano, realce al pasar y clic.
    public static T Clickable<T>(T element, Action onClick, string? tooltip = null) where T : FrameworkElement
    {
        element.Cursor = Cursors.Hand;
        if (tooltip is not null) element.ToolTip = tooltip;
        var pressed = false;
        element.MouseLeftButtonDown += (_, e) =>
        {
            pressed = true;
            element.CaptureMouse();
            e.Handled = true;
        };
        element.MouseLeftButtonUp += (_, e) =>
        {
            if (!pressed) return;
            pressed = false;
            element.ReleaseMouseCapture();
            if (element.IsMouseOver) onClick();
            e.Handled = true;
        };
        element.MouseEnter += (_, _) => element.Opacity = 0.88;
        element.MouseLeave += (_, _) => element.Opacity = 1;
        return element;
    }

    public static Border TextButton(string text, Action onClick, bool primary = false)
    {
        var b = new Border
        {
            Child = Label(text, 12.5, primary ? Background : Cream, primary ? FontWeights.SemiBold : FontWeights.Normal),
            Background = primary ? Solid(OliveColor) : Card,
            BorderBrush = Stroke,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12, 6, 12, 6),
        };
        return Clickable(b, onClick);
    }

    public static Border LinkButton(string text, Action onClick) =>
        Clickable(new Border { Child = Label(text, 12, Muted), Background = Brushes.Transparent, Padding = new Thickness(0, 2, 0, 2) }, onClick);
}
