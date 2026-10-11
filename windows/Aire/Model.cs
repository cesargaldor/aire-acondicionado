// Estado de la app y órdenes al aire (port de ACModel en App/AireApp.swift).
// No depende de WPF: se puede compilar y probar en cualquier sistema.

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MideaKit;

namespace Aire;

public interface IAireStore
{
    DeviceConfig? LoadDevice();
    bool SaveDevice(DeviceConfig config);
    void DeleteDevice();
    bool Beep { get; set; }
}

public enum Phase { Searching, NotFound, NeedsLogin, Ready }

public sealed class AireModel
{
    private readonly IAireStore _store;
    private MideaAC? _device;
    private double? _pendingTarget;
    private CancellationTokenSource? _debounce;
    private bool _beep;

    public Phase Phase { get; private set; } = Phase.Searching;
    public DiscoveredDevice? Found { get; private set; }
    public ACStatus? Status { get; private set; }
    public bool Online { get; private set; }
    public bool Busy { get; private set; }
    public string? Message { get; private set; }
    public int Syncing { get; private set; }

    public bool Beep
    {
        get => _beep;
        set
        {
            _beep = value;
            _store.Beep = value;
            OnChanged();
        }
    }

    public bool IsReady => Phase == Phase.Ready;

    /// Se lanza en el hilo de la interfaz cada vez que cambia algo que se muestra.
    public event Action? Changed;

    public AireModel(IAireStore store)
    {
        _store = store;
        _beep = store.Beep;
    }

    public void Start()
    {
        if (_store.LoadDevice() is { NeedsKeys: false } saved) Use(saved);
        else _ = DiscoverAsync();
        _ = PollAsync();
    }

    private async Task PollAsync()
    {
        while (true)
        {
            await Task.Delay(TimeSpan.FromSeconds(30));
            await RefreshAsync();
        }
    }

    private void OnChanged() => Changed?.Invoke();

    private void Use(DeviceConfig config)
    {
        _device?.Dispose();
        _device = new MideaAC(config);
        Phase = Phase.Ready;
        OnChanged();
        _ = RefreshAsync();
    }

    // MARK: Configuración

    public async Task DiscoverAsync()
    {
        Phase = Phase.Searching;
        Message = null;
        OnChanged();
        try
        {
            var found = await Setup.DiscoverAsync();
            var d = found.FirstOrDefault(x => x.Type == 0xAC) ?? found.FirstOrDefault();
            if (d is null)
            {
                Phase = Phase.NotFound;
            }
            else if (d.Version == 3)
            {
                Found = d;
                Phase = Phase.NeedsLogin;
            }
            else
            {
                var config = new DeviceConfig(d);
                _store.SaveDevice(config);
                Use(config);
                return;
            }
        }
        catch (Exception e)
        {
            Message = Describe(e);
            Phase = Phase.NotFound;
        }
        OnChanged();
    }

    public async Task LoginAsync(string email, string password)
    {
        if (Found is not { } d) return;
        Busy = true;
        Message = null;
        OnChanged();
        try
        {
            var config = await Setup.ObtainKeysAsync(d, email.Trim(), password);
            _store.SaveDevice(config);
            Busy = false;
            Use(config);
            return;
        }
        catch (MideaException e) when (e.Kind == MideaErrorKind.Cloud)
        {
            Message = e.Code == 3102 ? "Correo o contraseña incorrectos." : $"Midea: {e.Message} ({e.Code})";
        }
        catch (Exception e)
        {
            Message = Describe(e);
        }
        Busy = false;
        OnChanged();
    }

    /// Importa un config.json con ip, id, token y key, sin iniciar sesión en la nube.
    public async Task ImportConfigAsync(string json)
    {
        if (ConfigImport.Parse(json) is not { } config)
        {
            Message = "Ese archivo no tiene ip, id, token y key.";
            OnChanged();
            return;
        }
        Busy = true;
        Message = null;
        OnChanged();
        try
        {
            using (var test = new MideaAC(config))
            {
                if (config.Version == 3) await test.AuthenticateAsync();
            }
            _store.SaveDevice(config);
            Busy = false;
            Use(config);
            return;
        }
        catch (Exception e)
        {
            Message = $"Ese config.json no funciona con el aire: {Describe(e)}";
        }
        Busy = false;
        OnChanged();
    }

    public void Forget()
    {
        _store.DeleteDevice();
        _device?.Dispose();
        _device = null;
        Status = null;
        Online = false;
        _ = DiscoverAsync();
    }

    // MARK: Control

    public async Task RefreshAsync()
    {
        if (_device is not { } device || _pendingTarget is not null) return;
        try
        {
            Status = await device.RefreshAsync();
            Online = true;
        }
        catch (Exception)
        {
            Online = false;
            await RelocateAsync();
        }
        OnChanged();
    }

    /// Si el aire cambió de IP (el router le dio otra), lo vuelve a encontrar por su ID.
    private async Task RelocateAsync()
    {
        if (_device is not { } device) return;
        try
        {
            var found = await Setup.DiscoverAsync();
            var d = found.FirstOrDefault(x => x.Id == device.Config.Id);
            if (d is null || d.Ip == device.Config.Ip) return;
            await device.UpdateIpAsync(d.Ip, d.Port);
            _store.SaveDevice(device.Config);
            Status = await device.RefreshAsync();
            Online = true;
        }
        catch (Exception) { }
    }

    public void Change(Func<ACSettings, ACSettings> edit)
    {
        if (_device is not { } device || Status is not { } current) return;
        var settings = edit(current.Settings) with { Beep = _beep }; // pitido del aire al recibir la orden

        // Respuesta inmediata en pantalla
        Status = current with
        {
            Power = settings.Power,
            Target = settings.Target,
            Mode = settings.Mode,
            Fan = settings.Fan,
            Swing = settings.Swing,
            Eco = settings.Eco,
            Sleep = settings.Sleep,
            Turbo = settings.Turbo,
        };
        Syncing++;
        OnChanged();
        _ = ApplyAsync(device, settings);
    }

    private async Task ApplyAsync(MideaAC device, ACSettings settings)
    {
        try
        {
            Status = await device.ApplyAsync(settings);
            Online = true;
            Message = null;
        }
        catch (Exception e)
        {
            Online = false;
            Message = $"No se pudo enviar la orden: {Describe(e)}";
        }
        Syncing--;
        OnChanged();
        if (!Online) await RefreshAsync();
    }

    /// Las pulsaciones seguidas de +/− se agrupan en una sola orden.
    public void Step(double delta)
    {
        if (Status is not { } current) return;
        SetTarget((_pendingTarget ?? current.Target) + delta);
    }

    /// Mientras se arrastra la barra solo cambia la pantalla; la orden sale al soltar o parar.
    public void SetTarget(double value)
    {
        if (Status is not { } current) return;
        var target = Math.Min(30, Math.Max(16, Math.Round(value)));
        if (target == current.Target && _pendingTarget is null) return;
        _pendingTarget = target;
        Status = current with { Target = target };
        OnChanged();

        _debounce?.Cancel();
        var cts = _debounce = new CancellationTokenSource();
        _ = Task.Delay(600, cts.Token).ContinueWith(t =>
        {
            if (t.IsCanceled) return;
            _pendingTarget = null;
            Change(s => s with { Target = target });
        }, CancellationToken.None, TaskContinuationOptions.None, CurrentScheduler());
    }

    private static TaskScheduler CurrentScheduler() =>
        SynchronizationContext.Current is null ? TaskScheduler.Current : TaskScheduler.FromCurrentSynchronizationContext();

    public static string Describe(Exception e) => e is MideaException m ? m.ToString() : e.Message;
}
