// Aire para Windows: icono en la bandeja del sistema (junto al reloj) que controla un aire Midea
// directamente por la red local, sin servidor ni app de Midea.

using System;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace Aire;

internal sealed class WindowsStore : IAireStore
{
    public MideaKit.DeviceConfig? LoadDevice() => Storage.LoadDevice();
    public bool SaveDevice(MideaKit.DeviceConfig config) => Storage.SaveDevice(config);
    public void DeleteDevice() => Storage.DeleteDevice();

    public bool Beep
    {
        get => Storage.Beep;
        set => Storage.Beep = value;
    }
}

internal static class Program
{
    private const string MutexName = @"Local\Aire.Midea.SingleInstance";
    private const string ShowEventName = @"Local\Aire.Midea.Show";

    [STAThread]
    private static void Main()
    {
        // Una sola copia: si ya está abierta, se le pide que muestre el panel y se sale
        using var mutex = new Mutex(true, MutexName, out var first);
        if (!first)
        {
            try
            {
                using var show = EventWaitHandle.OpenExisting(ShowEventName);
                show.Set();
            }
            catch (Exception) { }
            return;
        }
        using var showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);

        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var model = new AireModel(new WindowsStore());
        Tray? tray = null;

        void Quit()
        {
            tray?.Dispose();
            app.Shutdown();
        }

        var panel = new PanelWindow(model, Quit);
        tray = new Tray(model, panel, Quit);

        // Otra copia pidió abrir el panel
        var dispatcher = Dispatcher.CurrentDispatcher;
        var listener = new Thread(() =>
        {
            while (true)
            {
                showEvent.WaitOne();
                dispatcher.InvokeAsync(panel.ShowPanel);
            }
        }) { IsBackground = true };
        listener.Start();

        app.Startup += (_, _) =>
        {
            model.Start();
            // La primera vez se abre solo para configurar
            if (Storage.LoadDevice() is null)
            {
                panel.ShowPanel();
                tray.ShowBalloon("Aire está junto al reloj",
                    "Si no ves el icono, está en la flecha ^ de la barra de tareas. Arrástralo junto al reloj para ver siempre la temperatura.");
            }
        };
        app.DispatcherUnhandledException += (_, e) =>
        {
            // Un fallo puntual no debe cerrar la app de la bandeja
            e.Handled = true;
        };
        app.Run();
    }
}
