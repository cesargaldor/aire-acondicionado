// Controles del panel: interruptores, tarjetas, barra de temperatura y el aire dibujado.

using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using MideaKit;

namespace Aire;

/// Interruptor en forma de píldora.
internal sealed class PillToggle : Border
{
    private readonly Border _knob;

    public PillToggle()
    {
        Width = 46;
        Height = 24;
        CornerRadius = new CornerRadius(12);
        _knob = new Border
        {
            Width = 24,
            Height = 18,
            CornerRadius = new CornerRadius(9),
            Background = Theme.Cream,
            Margin = new Thickness(3),
            VerticalAlignment = VerticalAlignment.Center,
        };
        Child = _knob;
        Set(false, Theme.OliveColor);
    }

    public void Set(bool on, Color tint)
    {
        Background = on ? Theme.Solid(tint, 217) : Theme.Solid(Colors.White, 20);
        _knob.HorizontalAlignment = on ? HorizontalAlignment.Right : HorizontalAlignment.Left;
    }
}

/// Fila con icono, texto e interruptor (Turbo, ajustes...).
internal sealed class FeaturePill : Border
{
    private readonly TextBlock _icon;
    private readonly PillToggle _toggle = new();

    public FeaturePill(string emoji, string title, Action onClick)
    {
        _icon = Theme.EmojiIcon(emoji, 14);
        var row = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(_toggle, Dock.Right);
        row.Children.Add(_toggle);
        _icon.Margin = new Thickness(0, 0, 8, 0);
        row.Children.Add(_icon);
        row.Children.Add(Theme.Label(title, 13, weight: FontWeights.Medium));
        Child = row;
        Padding = new Thickness(11, 8, 9, 8);
        CornerRadius = new CornerRadius(14);
        BorderBrush = Theme.Stroke;
        BorderThickness = new Thickness(1);
        Theme.Clickable(this, onClick);
    }

    public void Set(bool on, Color tint)
    {
        _toggle.Set(on, tint);
        _icon.Opacity = on ? 1 : 0.55;
        Background = on ? Theme.Solid(tint, 31) : Theme.Card;
    }
}

/// Tarjeta de Modo / Ventilador con su valor y una flecha.
internal sealed class ControlCard : Border
{
    private readonly TextBlock _icon;
    private readonly TextBlock _value;
    private readonly TextBlock _chevron;

    public ControlCard(string title, Action onClick)
    {
        _icon = Theme.EmojiIcon("", 18);
        _chevron = Theme.Icon(Theme.GlyphChevronDown, 10, Theme.Muted);
        _value = Theme.Label("", 13, weight: FontWeights.SemiBold);

        var top = new DockPanel();
        DockPanel.SetDock(_chevron, Dock.Right);
        _chevron.VerticalAlignment = VerticalAlignment.Top;
        top.Children.Add(_chevron);
        _icon.HorizontalAlignment = HorizontalAlignment.Left;
        top.Children.Add(_icon);

        var stack = new StackPanel();
        stack.Children.Add(top);
        stack.Children.Add(new Border { Height = 12 });
        stack.Children.Add(Theme.Label(title, 11, Theme.Muted));
        stack.Children.Add(_value);

        Child = stack;
        Padding = new Thickness(10);
        MinHeight = 84;
        CornerRadius = new CornerRadius(14);
        BorderBrush = Theme.Stroke;
        BorderThickness = new Thickness(1);
        Theme.Clickable(this, onClick);
    }

    public void Set(string emoji, string value, bool open, Color tint)
    {
        _icon.Text = emoji;
        _value.Text = value;
        _chevron.Text = open ? Theme.GlyphChevronUp : Theme.GlyphChevronDown;
        Background = open ? Theme.Solid(tint, 31) : Theme.Card;
    }
}

/// Botón redondo con un glifo (+, −).
internal static class CircleButton
{
    public static Border Make(string glyph, Action onClick, double size = 40)
    {
        var b = new Border
        {
            Width = size,
            Height = size,
            CornerRadius = new CornerRadius(size / 2),
            Background = Theme.Solid(Colors.White, 15),
            BorderBrush = Theme.Stroke,
            BorderThickness = new Thickness(1),
            Child = Theme.Icon(glyph, 14),
        };
        return Theme.Clickable(b, onClick);
    }
}

/// Barra de temperatura de 16 a 30 °C que se arrastra con el ratón.
internal sealed class TemperatureSlider : FrameworkElement
{
    private const double Min = 16, Max = 30, Knob = 30;
    private double _value = 24;
    private Color _tint = Theme.OliveColor;
    private readonly Action<double> _onChange;

    public TemperatureSlider(Action<double> onChange)
    {
        _onChange = onChange;
        Height = 48;
        Cursor = Cursors.Hand;
        Focusable = true;
    }

    public void Set(double value, Color tint)
    {
        _value = value;
        _tint = tint;
        InvalidateVisual();
    }

    private double KnobX(double width) => 6 + (width - Knob - 12) * ((_value - Min) / (Max - Min));

    protected override void OnRender(DrawingContext dc)
    {
        var w = ActualWidth;
        var h = ActualHeight;
        if (w <= Knob + 12) return;
        var track = new Rect(0, 0, w, h);
        dc.DrawRoundedRectangle(Theme.Solid(Colors.Black, 89), new Pen(Theme.Stroke, 1), track, h / 2, h / 2);

        var x = KnobX(w);
        var fill = new LinearGradientBrush(Color.FromArgb(38, _tint.R, _tint.G, _tint.B),
            Color.FromArgb(191, _tint.R, _tint.G, _tint.B), 0);
        dc.DrawRoundedRectangle(fill, null, new Rect(0, 0, Math.Min(w, x + Knob + 6), h), h / 2, h / 2);

        var knob = new Rect(x, 6, Knob, h - 12);
        dc.DrawRoundedRectangle(Theme.Solid(Colors.Black, 70), null, new Rect(knob.X, knob.Y + 2, knob.Width, knob.Height), 9, 9);
        dc.DrawRoundedRectangle(Theme.Cream, null, knob, 9, 9);
    }

    private void Drag(Point p)
    {
        var f = (p.X - 6 - Knob / 2) / (ActualWidth - Knob - 12);
        _onChange(Min + Math.Clamp(f, 0, 1) * (Max - Min));
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        CaptureMouse();
        Drag(e.GetPosition(this));
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (IsMouseCaptured) Drag(e.GetPosition(this));
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        ReleaseMouseCapture();
        e.Handled = true;
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        _onChange(_value + (e.Delta > 0 ? 1 : -1));
        e.Handled = true;
    }
}

/// Las líneas de aire saliendo de la máquina, animadas mientras está encendida.
internal sealed class AirflowView : FrameworkElement
{
    private static readonly Stopwatch Clock = Stopwatch.StartNew();
    private bool _active;
    private Color _color = Theme.OliveColor;
    private double _strength;
    private SwingMode _swing;
    private bool _hooked;
    private TimeSpan _lastFrame;

    public AirflowView()
    {
        IsHitTestVisible = false;
        IsVisibleChanged += (_, _) => UpdateHook();
        Effect = new BlurEffect { Radius = 1.2 };
    }

    public void Set(bool active, Color color, double strength, SwingMode swing)
    {
        _active = active;
        _color = color;
        _strength = strength;
        _swing = swing;
        UpdateHook();
        InvalidateVisual();
    }

    private void UpdateHook()
    {
        var want = _active && IsVisible;
        if (want == _hooked) return;
        _hooked = want;
        if (want) CompositionTarget.Rendering += OnFrame;
        else CompositionTarget.Rendering -= OnFrame;
    }

    private void OnFrame(object? sender, EventArgs e)
    {
        // Unos 30 fotogramas por segundo bastan y gastan poco
        if (e is RenderingEventArgs r)
        {
            if (r.RenderingTime - _lastFrame < TimeSpan.FromMilliseconds(32)) return;
            _lastFrame = r.RenderingTime;
        }
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        if (!_active || ActualWidth <= 0) return;
        var t = Clock.Elapsed.TotalSeconds;
        var w = ActualWidth;
        var h = ActualHeight;
        const int lines = 22;
        var vertical = _swing is SwingMode.Vertical or SwingMode.Both;
        var horizontal = _swing is SwingMode.Horizontal or SwingMode.Both;
        var reach = h * (0.5 + 0.5 * _strength) * (vertical ? 0.8 + 0.2 * Math.Sin(t * 1.1) : 1);
        var sway = horizontal ? Math.Sin(t * 0.8) * 18 : 0;

        for (var i = 0; i < lines; i++)
        {
            var f = i / (double)(lines - 1);
            var x0 = w * (0.1 + 0.8 * f);
            var geo = new StreamGeometry();
            using (var ctx = geo.Open())
            {
                for (var s = 0; s <= 14; s++)
                {
                    var k = s / 14.0;
                    var wobble = Math.Sin(k * 5 + t * (2 + 3 * _strength) + i * 1.7) * 3 * k;
                    var p = new Point(x0 + (f - 0.5) * 40 * k + sway * k + wobble, k * reach);
                    if (s == 0) ctx.BeginFigure(p, false, false);
                    else ctx.LineTo(p, true, true);
                }
            }
            geo.Freeze();
            var pulse = 0.55 + 0.45 * Math.Sin(t * 2.4 + i * 0.9);
            var brush = new LinearGradientBrush(
                Color.FromArgb((byte)(140 * pulse), _color.R, _color.G, _color.B),
                Color.FromArgb(0, _color.R, _color.G, _color.B),
                new Point(0, 0), new Point(0, 1));
            brush.Freeze();
            dc.DrawGeometry(null, new Pen(brush, 1.6), geo);
        }
    }
}

/// El aire dibujado, echando aire cuando está encendido.
internal sealed class ACUnitView : Grid
{
    private readonly AirflowView _air = new() { Width = 210, Height = 90 };
    private readonly TextBlock _display;

    public ACUnitView()
    {
        Height = 140;
        _air.VerticalAlignment = VerticalAlignment.Top;
        _air.Margin = new Thickness(0, 50, 0, 0);
        Children.Add(_air);

        var body = new Grid { Width = 200, Height = 54, VerticalAlignment = VerticalAlignment.Top };
        body.Children.Add(new Border
        {
            CornerRadius = new CornerRadius(12),
            Background = new LinearGradientBrush(Color.FromRgb(0xFA, 0xFA, 0xFA), Color.FromRgb(0xD6, 0xD1, 0xBF), 90),
        });
        body.Children.Add(new Border
        {
            Height = 5,
            CornerRadius = new CornerRadius(3),
            Background = Theme.Solid(Colors.Black, 140),
            Margin = new Thickness(14, 0, 14, 7),
            VerticalAlignment = VerticalAlignment.Bottom,
        });
        _display = new TextBlock
        {
            FontSize = 8,
            FontWeight = FontWeights.Bold,
            FontFamily = new FontFamily("Consolas"),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 9, 26, 0),
        };
        body.Children.Add(_display);
        body.Effect = new DropShadowEffect { Color = Colors.Black, Opacity = 0.5, BlurRadius = 20, ShadowDepth = 6, Direction = 270 };
        // Ligera perspectiva, como en la versión de Mac
        body.RenderTransformOrigin = new Point(0.5, 0.5);
        body.RenderTransform = new SkewTransform(0, -3);
        Children.Add(body);
    }

    public void Set(ACStatus s)
    {
        var accent = Theme.AccentColor(s);
        var strength = Names.FanOf(s.Fan) switch
        {
            FanSpeed.Silent => 0.25,
            FanSpeed.Low => 0.4,
            FanSpeed.Medium => 0.6,
            FanSpeed.Max => 1,
            _ => 0.8,
        };
        _air.Set(s.Power, accent, s.Power ? strength : 0, Names.SwingOf(s.Swing));
        _display.Text = s.DisplayOn && s.Power ? ((int)Math.Round(s.Target)).ToString() : "";
        _display.Foreground = Theme.Solid(accent, 230);
        Opacity = s.Power ? 1 : 0.55;
    }
}
