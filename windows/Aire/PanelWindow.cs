// El panel que se abre encima del reloj, abajo a la derecha (equivalente al panel de la barra de menús del Mac).

using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using MideaKit;

namespace Aire;

internal sealed class PanelWindow : Window
{
    private enum Picker { None, Mode, Fan }

    private readonly AireModel _m;
    private readonly Action _quit;
    private readonly StackPanel _content = new();
    private readonly TranslateTransform _slide = new();
    private string? _builtView;
    private bool _showSettings;
    private bool _suppressHide;
    private Picker _picker = Picker.None;

    public DateTime LastHidden { get; private set; } = DateTime.MinValue;

    // Elementos de la vista de control (se crean una vez y se actualizan)
    private TextBlock? _roomText, _bigTemp, _alertText;
    private Border? _powerButton, _pickerRow, _alertRow;
    private TextBlock? _powerIcon;
    private ACUnitView? _unit;
    private FeaturePill? _turbo;
    private TemperatureSlider? _slider;
    private ControlCard? _modeCard, _fanCard;
    private StackPanel? _chips;
    private Panel? _bigTempRow;

    // Pie
    private readonly TextBlock _statusIcon = Theme.Icon(Theme.GlyphCheck, 12, Theme.Muted);
    private readonly TextBlock _statusText = Theme.Label("", 12, Theme.Muted);
    private readonly Border _settingsCard = new();
    private readonly TextBlock _settingsButton = Theme.Icon(Theme.GlyphSettings, 14, Theme.Muted);
    private readonly TextBlock _message = new()
    {
        FontSize = 12,
        Foreground = Theme.Ember,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 0, 0, 12),
        FontFamily = Theme.Text,
    };
    private readonly PillToggle _beepToggle = new();
    private readonly PillToggle _startupToggle = new();
    private readonly Border _forgetButton;

    public PanelWindow(AireModel model, Action quit)
    {
        _m = model;
        _quit = quit;

        Title = "Aire";
        Width = 370;
        SizeToContent = SizeToContent.Height;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        Topmost = true;
        ShowActivated = true;
        FontFamily = Theme.Text;
        UseLayoutRounding = true;

        var root = new StackPanel();
        root.Children.Add(_content);
        root.Children.Add(_message);
        _forgetButton = Theme.LinkButton("Olvidar este aire", () => _m.Forget());
        root.Children.Add(BuildFooter());

        Content = new Border
        {
            Child = root,
            Background = Theme.Background,
            BorderBrush = Theme.Solid(Colors.White, 26),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(18),
            Margin = new Thickness(0, 0, 0, 0),
            RenderTransform = _slide,
        };

        Deactivated += (_, _) =>
        {
            if (!_suppressHide) HidePanel();
        };
        SizeChanged += (_, _) => Reposition();
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) HidePanel();
        };

        _m.Changed += Render;
        Render();
    }

    // MARK: Mostrar y ocultar

    public void ShowPanel()
    {
        Render();
        if (!IsVisible)
        {
            Opacity = 0;
            Show();
        }
        Reposition();
        Activate();
        Focus();
        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(140)));
        _slide.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(10, 0, TimeSpan.FromMilliseconds(180)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        _ = _m.RefreshAsync();
    }

    public void HidePanel()
    {
        if (!IsVisible) return;
        LastHidden = DateTime.UtcNow;
        Hide();
    }

    /// Abajo a la derecha, justo encima de la barra de tareas.
    private void Reposition()
    {
        var area = SystemParameters.WorkArea;
        Left = area.Right - ActualWidth - 12;
        Top = area.Bottom - ActualHeight - 12;
    }

    // MARK: Pintado

    private void Render()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.InvokeAsync(Render);
            return;
        }

        var phase = _m.Phase;
        var readyWithStatus = phase == Phase.Ready && _m.Status is not null;
        // Solo se reconstruye la vista cuando cambia de pantalla (así no se pierde lo que escribes)
        var view = phase switch
        {
            Phase.Ready when readyWithStatus => "controls",
            Phase.Ready => _m.Online ? "connecting" : "offline",
            Phase.NeedsLogin => "login:" + _m.Found?.Id,
            _ => phase.ToString(),
        };
        if (view != _builtView)
        {
            _content.Children.Clear();
            _builtView = view;
            switch (phase)
            {
                case Phase.Searching:
                    _content.Children.Add(TitleRow());
                    _content.Children.Add(Spaced(Theme.Label("Buscando el aire en tu red…", 13, Theme.Muted)));
                    break;
                case Phase.NotFound:
                    _content.Children.Add(TitleRow());
                    _content.Children.Add(NotFoundView());
                    break;
                case Phase.NeedsLogin:
                    _content.Children.Add(TitleRow());
                    _content.Children.Add(LoginView());
                    break;
                case Phase.Ready when readyWithStatus:
                    BuildControls();
                    break;
                case Phase.Ready:
                    _content.Children.Add(TitleRow());
                    _content.Children.Add(Spaced(Wrap(_m.Online
                        ? "Conectando…"
                        : "No responde. Comprueba que el PC está en la misma red que el aire.")));
                    break;
            }
        }
        if (readyWithStatus) UpdateControls(_m.Status!.Value);
        else if (phase == Phase.NeedsLogin) UpdateLoginBusy();

        _message.Text = _m.Message ?? "";
        _message.Visibility = string.IsNullOrEmpty(_m.Message) ? Visibility.Collapsed : Visibility.Visible;
        UpdateFooter();
    }

    private static FrameworkElement Spaced(FrameworkElement e)
    {
        e.Margin = new Thickness(0, 0, 0, 14);
        return e;
    }

    private static TextBlock Wrap(string text, double size = 13, Brush? color = null)
    {
        var t = Theme.Label(text, size, color ?? Theme.Muted);
        t.TextWrapping = TextWrapping.Wrap;
        t.TextTrimming = TextTrimming.None;
        return t;
    }

    private static FrameworkElement TitleRow()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 14) };
        row.Children.Add(Theme.EmojiIcon("❄", 14));
        var title = Theme.Label("Aire", 15, weight: FontWeights.SemiBold);
        title.Margin = new Thickness(8, 0, 0, 0);
        row.Children.Add(title);
        return row;
    }

    // MARK: Sin aire / inicio de sesión

    private FrameworkElement NotFoundView()
    {
        var stack = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };
        var text = Wrap("No se encontró ningún aire Midea. Comprueba que el PC está en la misma red que el aire y que el firewall de Windows deja pasar a Aire.");
        text.Margin = new Thickness(0, 0, 0, 10);
        stack.Children.Add(text);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        buttons.Children.Add(Theme.TextButton("Buscar de nuevo", () => _ = _m.DiscoverAsync(), primary: true));
        var import = Theme.TextButton("Importar config.json", ImportConfig);
        import.Margin = new Thickness(8, 0, 0, 0);
        buttons.Children.Add(import);
        stack.Children.Add(buttons);
        return stack;
    }

    private TextBox? _email;
    private PasswordBox? _password;
    private Border? _connect;
    private TextBlock? _connectLabel;

    private FrameworkElement LoginView()
    {
        var d = _m.Found!;
        var stack = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };

        var found = new TextBlock { FontSize = 13, Foreground = Theme.Cream, FontFamily = Theme.Text, TextWrapping = TextWrapping.Wrap };
        found.Inlines.Add("Encontrado ");
        found.Inlines.Add(new System.Windows.Documents.Run(d.Name) { FontWeight = FontWeights.SemiBold });
        found.Inlines.Add($" en {d.Ip}");
        stack.Children.Add(found);

        var info = Wrap("La primera vez hace falta tu cuenta de SmartHome para obtener la clave del aire. La contraseña no se guarda; después todo funciona en local.", 12);
        info.Margin = new Thickness(0, 6, 0, 12);
        stack.Children.Add(info);

        _email = new TextBox();
        _password = new PasswordBox();
        stack.Children.Add(Field("Correo", _email));
        stack.Children.Add(Field("Contraseña", _password));
        _password.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) Connect();
        };
        _email.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) _password.Focus();
        };

        var row = new DockPanel { Margin = new Thickness(0, 4, 0, 0) };
        _connect = Theme.TextButton("Conectar", Connect, primary: true);
        _connectLabel = (TextBlock)_connect.Child;
        DockPanel.SetDock(_connect, Dock.Right);
        row.Children.Add(_connect);
        var import = Theme.LinkButton("Importar config.json", ImportConfig);
        import.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(import);
        stack.Children.Add(row);

        Dispatcher.InvokeAsync(() => _email.Focus(), System.Windows.Threading.DispatcherPriority.Input);
        return stack;
    }

    private static FrameworkElement Field(string label, Control input)
    {
        var stack = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
        var l = Theme.Label(label, 11, Theme.Muted);
        l.Margin = new Thickness(0, 0, 0, 4);
        stack.Children.Add(l);
        input.Background = Theme.Field;
        input.Foreground = Theme.Cream;
        input.BorderBrush = Theme.Stroke;
        input.BorderThickness = new Thickness(1);
        input.Padding = new Thickness(8, 6, 8, 6);
        input.FontSize = 13;
        input.FontFamily = Theme.Text;
        if (input is TextBox t) t.CaretBrush = Theme.Cream;
        if (input is PasswordBox p) p.CaretBrush = Theme.Cream;
        stack.Children.Add(input);
        return stack;
    }

    private void UpdateLoginBusy()
    {
        if (_connectLabel is null || _connect is null) return;
        _connectLabel.Text = _m.Busy ? "Conectando…" : "Conectar";
        _connect.IsEnabled = !_m.Busy;
    }

    private void Connect()
    {
        if (_email is null || _password is null || _m.Busy) return;
        var email = _email.Text;
        var pwd = _password.Password;
        if (email.Trim() == "" || pwd == "") return;
        _password.Clear(); // no se queda en la ventana
        _ = _m.LoginAsync(email, pwd);
    }

    private void ImportConfig()
    {
        _suppressHide = true;
        try
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Elige un config.json con ip, id, token y key",
                Filter = "Configuración (*.json)|*.json|Todos los archivos|*.*",
            };
            if (dialog.ShowDialog(this) == true)
            {
                string json;
                try { json = File.ReadAllText(dialog.FileName); }
                catch (Exception) { return; }
                _ = _m.ImportConfigAsync(json);
            }
        }
        finally
        {
            _suppressHide = false;
            Activate();
        }
    }

    // MARK: Controles del aire

    private void BuildControls()
    {
        // Sala y encendido
        var top = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
        _powerIcon = Theme.Icon(Theme.GlyphPower, 17);
        _powerButton = new Border
        {
            Width = 44,
            Height = 44,
            CornerRadius = new CornerRadius(22),
            Child = _powerIcon,
            BorderThickness = new Thickness(3),
        };
        Theme.Clickable(_powerButton, () => _m.Change(s => s with { Power = !s.Power }));
        DockPanel.SetDock(_powerButton, Dock.Right);
        top.Children.Add(_powerButton);
        var room = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        room.Children.Add(Theme.EmojiIcon("\U0001F321", 13));
        _roomText = Theme.Label("", 14);
        _roomText.Margin = new Thickness(6, 0, 0, 0);
        room.Children.Add(_roomText);
        top.Children.Add(room);
        _content.Children.Add(top);

        _unit = new ACUnitView();
        _content.Children.Add(_unit);

        // Temperatura y turbo
        var tempRow = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        _turbo = new FeaturePill("⚡", "Turbo", () => _m.Change(s => s with { Turbo = !s.Turbo })) { Width = 150 };
        _turbo.VerticalAlignment = VerticalAlignment.Center;
        DockPanel.SetDock(_turbo, Dock.Right);
        tempRow.Children.Add(_turbo);
        var big = new StackPanel { Orientation = Orientation.Horizontal };
        _bigTemp = Theme.Label("", 56, weight: FontWeights.SemiBold, family: Theme.Display);
        big.Children.Add(_bigTemp);
        var unit = Theme.Label("°C", 22, Theme.Muted, FontWeights.Medium, Theme.Display);
        unit.VerticalAlignment = VerticalAlignment.Top;
        unit.Margin = new Thickness(4, 14, 0, 0);
        big.Children.Add(unit);
        _bigTempRow = big;
        tempRow.Children.Add(big);
        _content.Children.Add(tempRow);

        // Barra de temperatura
        var sliderRow = new DockPanel();
        var minus = CircleButton.Make(Theme.GlyphMinus, () => _m.Step(-1));
        var plus = CircleButton.Make(Theme.GlyphPlus, () => _m.Step(1));
        DockPanel.SetDock(minus, Dock.Left);
        DockPanel.SetDock(plus, Dock.Right);
        minus.Margin = new Thickness(0, 0, 10, 0);
        plus.Margin = new Thickness(10, 0, 0, 0);
        sliderRow.Children.Add(minus);
        sliderRow.Children.Add(plus);
        _slider = new TemperatureSlider(v => _m.SetTarget(v));
        sliderRow.Children.Add(_slider);
        _content.Children.Add(sliderRow);

        var scale = new DockPanel { Margin = new Thickness(56, 6, 56, 14) };
        var max = Theme.Label("30°", 11, Theme.Muted);
        DockPanel.SetDock(max, Dock.Right);
        scale.Children.Add(max);
        scale.Children.Add(Theme.Label("16°", 11, Theme.Muted));
        _content.Children.Add(scale);

        // Tarjetas
        var cards = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        cards.ColumnDefinitions.Add(new ColumnDefinition());
        cards.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
        cards.ColumnDefinitions.Add(new ColumnDefinition());
        _modeCard = new ControlCard("Modo", () => TogglePicker(Picker.Mode));
        _fanCard = new ControlCard("Ventilador", () => TogglePicker(Picker.Fan));
        Grid.SetColumn(_fanCard, 2);
        cards.Children.Add(_modeCard);
        cards.Children.Add(_fanCard);
        _content.Children.Add(cards);

        _chips = new StackPanel { Orientation = Orientation.Horizontal };
        _pickerRow = Theme.CardBorder(_chips, 20);
        _pickerRow.Padding = new Thickness(6);
        _pickerRow.Margin = new Thickness(0, 0, 0, 8);
        _content.Children.Add(_pickerRow);

        // Aviso de filtro o error
        var alert = new StackPanel { Orientation = Orientation.Horizontal };
        alert.Children.Add(Theme.Icon(Theme.GlyphWarning, 13, Theme.Ember));
        _alertText = Theme.Label("", 12);
        _alertText.Margin = new Thickness(8, 0, 0, 0);
        alert.Children.Add(_alertText);
        _alertRow = Theme.CardBorder(alert, 14, Theme.Solid(Theme.EmberColor, 26));
        _alertRow.Padding = new Thickness(10);
        _alertRow.Margin = new Thickness(0, 0, 0, 8);
        _content.Children.Add(_alertRow);

        var spacer = new Border { Height = 6 };
        _content.Children.Add(spacer);
    }

    private void TogglePicker(Picker p)
    {
        _picker = _picker == p ? Picker.None : p;
        Render();
    }

    private void UpdateControls(ACStatus s)
    {
        var tint = Theme.AccentColor(s);
        var mode = Names.ModeOf(s.Mode) ?? ACMode.Cool;
        var fan = Names.FanOf(s.Fan);

        var room = "Sala";
        if (s.Indoor is { } i) room += $"  {i:0.0}°C";
        if (s.Outdoor is { } o) room += $"  ·  Fuera {o:0}°";
        _roomText!.Text = room;

        _powerButton!.Background = s.Power ? Theme.Solid(tint) : Theme.Solid(Colors.White, 15);
        _powerButton.BorderBrush = s.Power ? Theme.Solid(tint, 102) : Theme.Stroke;
        _powerIcon!.Foreground = s.Power ? Theme.Background : Theme.Cream;
        _powerButton.ToolTip = s.Power ? "Apagar" : "Encender";

        _unit!.Set(s);
        _bigTemp!.Text = s.Target % 1 == 0 ? $"{s.Target:0}" : $"{s.Target:0.0}";
        _bigTempRow!.Opacity = s.Power ? 1 : 0.45;
        _turbo!.Set(s.Turbo, tint);
        _slider!.Set(s.Target, tint);
        _modeCard!.Set(Theme.ModeEmoji(mode), mode.Name(), _picker == Picker.Mode, tint);
        _fanCard!.Set("\U0001F300", fan.Name(), _picker == Picker.Fan, tint);

        _chips!.Children.Clear();
        _pickerRow!.Visibility = _picker == Picker.None ? Visibility.Collapsed : Visibility.Visible;
        if (_picker == Picker.Mode)
            foreach (var option in Names.Modes)
                AddChip(option.Name(), s.Power && mode == option, tint, () => _m.Change(x => x with { Power = true, Mode = (byte)option }));
        else if (_picker == Picker.Fan)
            foreach (var option in Names.Fans)
                AddChip(option.Name(), fan == option, tint, () => _m.Change(x => x with { Fan = (byte)option }));

        var alert = s.FilterAlert ? "Toca limpiar el filtro del aire." : s.ErrorCode != 0 ? $"El aire indica el error {s.ErrorCode}." : null;
        _alertRow!.Visibility = alert is null ? Visibility.Collapsed : Visibility.Visible;
        _alertText!.Text = alert ?? "";
    }

    private void AddChip(string title, bool selected, Color tint, Action onClick)
    {
        var count = _picker == Picker.Mode ? Names.Modes.Length : Names.Fans.Length;
        var width = (370 - 36 - 12 - 2 - 6 * (count - 1)) / count;
        var label = Theme.Label(title, 12, selected ? Theme.Background : Theme.Cream, selected ? FontWeights.SemiBold : FontWeights.Normal);
        label.TextTrimming = TextTrimming.None;
        var fit = new Viewbox { Child = label, StretchDirection = StretchDirection.DownOnly, Stretch = Stretch.Uniform };
        var chip = new Border
        {
            Child = fit,
            Width = width,
            Height = 30,
            Padding = new Thickness(4, 7, 4, 7),
            CornerRadius = new CornerRadius(15),
            Background = selected ? Theme.Solid(tint) : Theme.Solid(Colors.White, 15),
            Margin = new Thickness(_chips!.Children.Count == 0 ? 0 : 6, 0, 0, 0),
        };
        _chips.Children.Add(Theme.Clickable(chip, onClick));
    }

    // MARK: Pie: estado y ajustes

    private FrameworkElement BuildFooter()
    {
        var stack = new StackPanel();

        var settings = new StackPanel();
        settings.Children.Add(SettingRow("Pitido del aire al cambiar algo", _beepToggle, () => _m.Beep = !_m.Beep));
        settings.Children.Add(SettingRow("Iniciar con Windows", _startupToggle, () =>
        {
            Storage.LaunchAtLogin = !Storage.LaunchAtLogin;
            UpdateFooter();
        }));
        var buttons = new DockPanel { Margin = new Thickness(0, 4, 0, 0) };
        var quit = Theme.LinkButton("Salir de Aire", _quit);
        DockPanel.SetDock(quit, Dock.Right);
        buttons.Children.Add(quit);
        buttons.Children.Add(_forgetButton);
        _forgetButton.HorizontalAlignment = HorizontalAlignment.Left;
        settings.Children.Add(buttons);
        _settingsCard.Child = settings;
        _settingsCard.CornerRadius = new CornerRadius(14);
        _settingsCard.Background = Theme.Card;
        _settingsCard.BorderBrush = Theme.Stroke;
        _settingsCard.BorderThickness = new Thickness(1);
        _settingsCard.Padding = new Thickness(12, 10, 12, 10);
        _settingsCard.Margin = new Thickness(0, 0, 0, 12);
        stack.Children.Add(_settingsCard);

        var bar = new DockPanel();
        var gear = new Border { Child = _settingsButton, Width = 28, Height = 24, Background = Brushes.Transparent };
        Theme.Clickable(gear, () =>
        {
            _showSettings = !_showSettings;
            UpdateFooter();
        }, "Ajustes");
        DockPanel.SetDock(gear, Dock.Right);
        bar.Children.Add(gear);
        var status = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        status.Children.Add(_statusIcon);
        _statusText.Margin = new Thickness(6, 0, 0, 0);
        status.Children.Add(_statusText);
        bar.Children.Add(status);
        stack.Children.Add(bar);
        return stack;
    }

    private static FrameworkElement SettingRow(string label, PillToggle toggle, Action onClick)
    {
        var row = new DockPanel { Margin = new Thickness(0, 3, 0, 3), Background = Brushes.Transparent };
        toggle.Width = 40;
        toggle.Height = 22;
        DockPanel.SetDock(toggle, Dock.Right);
        row.Children.Add(toggle);
        row.Children.Add(Theme.Label(label, 12));
        return Theme.Clickable(row, onClick);
    }

    private void UpdateFooter()
    {
        _settingsCard.Visibility = _showSettings ? Visibility.Visible : Visibility.Collapsed;
        _settingsButton.Foreground = _showSettings ? Theme.Cream : Theme.Muted;
        _beepToggle.Set(_m.Beep, Theme.OliveColor);
        _startupToggle.Set(Storage.LaunchAtLogin, Theme.OliveColor);
        _forgetButton.Visibility = _m.IsReady ? Visibility.Visible : Visibility.Collapsed;

        if (_m.Syncing > 0)
        {
            _statusIcon.Text = ""; // Sync
            _statusText.Text = "Sincronizando…";
        }
        else if (_m.IsReady)
        {
            _statusIcon.Text = _m.Online ? Theme.GlyphCheck : Theme.GlyphWarning;
            _statusText.Text = _m.Online ? "Conectado" : "Sin conexión";
        }
        else
        {
            _statusIcon.Text = "";
            _statusText.Text = "";
        }
    }
}
