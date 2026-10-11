// El icono junto al reloj: muestra la temperatura y abre el panel al hacer clic.

using System;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using Microsoft.Win32;
using MideaKit;
using SD = System.Drawing;
using WF = System.Windows.Forms;

namespace Aire;

internal sealed class Tray : IDisposable
{
    private readonly AireModel _m;
    private readonly PanelWindow _panel;
    private readonly WF.NotifyIcon _icon;
    private readonly WF.ToolStripMenuItem _powerItem;
    private SD.Icon? _current;
    private string _lastKey = "";

    public Tray(AireModel model, PanelWindow panel, Action quit)
    {
        _m = model;
        _panel = panel;

        var menu = new WF.ContextMenuStrip();
        var open = new WF.ToolStripMenuItem("Abrir Aire", null, (_, _) => _panel.ShowPanel());
        open.Font = new SD.Font(open.Font, SD.FontStyle.Bold);
        _powerItem = new WF.ToolStripMenuItem("Encender", null, (_, _) => _m.Change(s => s with { Power = !s.Power }));
        menu.Items.Add(open);
        menu.Items.Add(_powerItem);
        menu.Items.Add(new WF.ToolStripSeparator());
        menu.Items.Add(new WF.ToolStripMenuItem("Salir", null, (_, _) => quit()));
        menu.Opening += (_, _) =>
        {
            _powerItem.Enabled = _m.IsReady && _m.Status is not null;
            _powerItem.Text = _m.Status is { Power: true } ? "Apagar" : "Encender";
        };

        _icon = new WF.NotifyIcon { ContextMenuStrip = menu, Text = "Aire", Visible = true };
        _icon.MouseUp += (_, e) =>
        {
            if (e.Button != WF.MouseButtons.Left) return;
            // El clic en el icono quita el foco al panel y lo cierra justo antes: no lo reabras
            if (_panel.IsVisible) _panel.HidePanel();
            else if ((DateTime.UtcNow - _panel.LastHidden).TotalMilliseconds > 350) _panel.ShowPanel();
        };

        var dispatcher = Dispatcher.CurrentDispatcher;
        _m.Changed += () => dispatcher.InvokeAsync(Update);
        SystemEvents.UserPreferenceChanged += OnThemeChanged;
        Update();
    }

    private void OnThemeChanged(object? sender, UserPreferenceChangedEventArgs e)
    {
        _lastKey = "";
        System.Windows.Application.Current?.Dispatcher.InvokeAsync(Update);
    }

    private void Update()
    {
        var s = _m.Status;
        var on = _m.IsReady && _m.Online && s is { Power: true };
        var light = LightTaskbar();
        var key = $"{_m.IsReady}|{_m.Online}|{on}|{s?.Target}|{s?.Mode}|{light}";

        // Texto al pasar el ratón (máximo 63 caracteres)
        string tip;
        if (!_m.IsReady) tip = _m.Phase == Phase.NeedsLogin ? "Aire · haz clic para conectar" : "Aire · buscando el aire";
        else if (!_m.Online || s is null) tip = "Aire · sin conexión";
        else
        {
            var mode = Names.ModeOf(s.Value.Mode)?.Name() ?? "";
            tip = s.Value.Power ? $"Aire · {s.Value.Target:0}° · {mode}" : "Aire · apagado";
            if (s.Value.Indoor is { } i) tip += $" · sala {i:0.0}°";
        }
        _icon.Text = tip.Length > 63 ? tip[..63] : tip;

        if (key == _lastKey) return;
        _lastKey = key;
        var icon = Render(on ? s : null, _m.IsReady && _m.Online, light);
        _icon.Icon = icon;
        _current?.Dispose();
        _current = icon;
    }

    private static bool LightTaskbar()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return k?.GetValue("SystemUsesLightTheme") is int v && v == 1;
        }
        catch (Exception) { return false; }
    }

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr handle);

    /// Encendido: la temperatura con una raya del color del modo. Apagado: el dibujo del aire.
    private static SD.Icon Render(ACStatus? on, bool connected, bool lightTaskbar)
    {
        var size = Math.Max(16, WF.SystemInformation.SmallIconSize.Width) * 2;
        using var bmp = new SD.Bitmap(size, size);
        using (var g = SD.Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SD.Drawing2D.SmoothingMode.AntiAlias;
            g.TextRenderingHint = SD.Text.TextRenderingHint.AntiAliasGridFit;
            g.Clear(SD.Color.Transparent);
            var ink = lightTaskbar ? SD.Color.FromArgb(30, 30, 30) : SD.Color.FromArgb(240, 235, 217);
            if (!connected) ink = SD.Color.FromArgb(130, ink);

            if (on is { } s)
            {
                var text = ((int)Math.Round(s.Target)).ToString();
                var fontSize = size * 0.72f;
                SD.SizeF measured;
                SD.Font font;
                while (true)
                {
                    font = new SD.Font("Segoe UI", fontSize, SD.FontStyle.Bold, SD.GraphicsUnit.Pixel);
                    measured = g.MeasureString(text, font, size * 2, SD.StringFormat.GenericTypographic);
                    if (measured.Width <= size * 0.98f || fontSize < 6) break;
                    font.Dispose();
                    fontSize -= 1;
                }
                using (font)
                using (var brush = new SD.SolidBrush(ink))
                {
                    var x = (size - measured.Width) / 2;
                    var y = (size * 0.86f - measured.Height) / 2;
                    g.DrawString(text, font, brush, x, y, SD.StringFormat.GenericTypographic);
                }
                var c = Theme.AccentColor(s);
                using var bar = new SD.SolidBrush(SD.Color.FromArgb(c.R, c.G, c.B));
                var h = Math.Max(2, size / 9f);
                FillRounded(g, bar, size * 0.18f, size - h - 1, size * 0.64f, h, h / 2);
            }
            else
            {
                // La unidad de aire y tres rayas de aire
                using var brush = new SD.SolidBrush(ink);
                FillRounded(g, brush, size * 0.08f, size * 0.2f, size * 0.84f, size * 0.36f, size * 0.09f);
                using var slot = new SD.SolidBrush(lightTaskbar ? SD.Color.White : SD.Color.FromArgb(19, 18, 15));
                FillRounded(g, slot, size * 0.18f, size * 0.44f, size * 0.64f, size * 0.05f, size * 0.025f);
                using var pen = new SD.Pen(ink, Math.Max(1.5f, size / 14f)) { StartCap = SD.Drawing2D.LineCap.Round, EndCap = SD.Drawing2D.LineCap.Round };
                foreach (var f in new[] { 0.3f, 0.5f, 0.7f })
                {
                    var dx = (f - 0.5f) * size * 0.25f;
                    g.DrawLine(pen, size * f, size * 0.66f, size * f + dx, size * 0.88f);
                }
            }
        }
        var handle = bmp.GetHicon();
        try
        {
            using var temp = SD.Icon.FromHandle(handle);
            return (SD.Icon)temp.Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    private static void FillRounded(SD.Graphics g, SD.Brush brush, float x, float y, float w, float h, float r)
    {
        using var path = new SD.Drawing2D.GraphicsPath();
        var d = r * 2;
        path.AddArc(x, y, d, d, 180, 90);
        path.AddArc(x + w - d, y, d, d, 270, 90);
        path.AddArc(x + w - d, y + h - d, d, d, 0, 90);
        path.AddArc(x, y + h - d, d, d, 90, 90);
        path.CloseFigure();
        g.FillPath(brush, path);
    }

    public void ShowBalloon(string title, string text)
    {
        _icon.BalloonTipTitle = title;
        _icon.BalloonTipText = text;
        _icon.ShowBalloonTip(5000);
    }

    public void Dispose()
    {
        SystemEvents.UserPreferenceChanged -= OnThemeChanged;
        _icon.Visible = false;
        _icon.Dispose();
        _current?.Dispose();
    }
}
