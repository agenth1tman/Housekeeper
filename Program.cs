using Housekeeper.Core;
using Housekeeper.Ui;
using Housekeeper.Views;

namespace Housekeeper;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        var options = LaunchOptions.Parse(args);
        AppPaths.Initialize(options.SandboxDirectory);

        using var instance = new SingleInstance(AppPaths.InstanceName);
        if (!instance.IsFirst)
        {
            // A copy is already running, usually hidden in the notification area. Bring that one
            // forward instead of refusing to open. A second copy started by Windows has nothing to do.
            if (!options.StartedWithWindows)
                instance.ActivateFirst();
            return;
        }

        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => ReportCrash(e.Exception, canContinue: true);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => ReportCrash(e.ExceptionObject as Exception, canContinue: false);

        ApplicationConfiguration.Initialize();

        // The fonts load on another thread while the settings are read; the window waits for them.
        _ = Task.Run(() => AppFonts.Body);
        var settings = AppSettings.Load();
        ActivityLog.Info($"Housekeeper {AppInfo.Version} started{(options.StartedWithWindows ? " with Windows" : "")}{(AppPaths.IsSandbox ? $" in sandbox {AppPaths.DataDirectory}" : "")}.");
        StartupEntry.Repair(settings.OpenAtStartup, removeLegacyEntry: settings.ImportedFromVersion1);

        using var form = new MainForm(settings);
        instance.ActivationRequested += form.ShowFromAnotherInstance;
        Application.Run(new ShellContext(form, startInTray: options.StartedWithWindows));
        ActivityLog.Info("Housekeeper closed.");
    }

    private static void ReportCrash(Exception? exception, bool canContinue)
    {
        if (exception is null)
            return;
        ActivityLog.Crash(exception);
        var message = canContinue
            ? $"Something went wrong: {exception.Message}\n\nHousekeeper will keep running. Details are in {AppPaths.ActivityLogFile}."
            : $"Housekeeper has to close: {exception.Message}\n\nDetails are in {AppPaths.ActivityLogFile}.";
        MessageBox.Show(message, "Housekeeper", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }
}

/// <summary>
/// Keeps the app alive while the window is hidden in the notification area, and ends it when
/// the window closes.
/// </summary>
internal sealed class ShellContext : ApplicationContext
{
    public ShellContext(MainForm form, bool startInTray)
    {
        form.FormClosed += (_, _) => ExitThread();
        if (startInTray)
            form.StartInTray();
        else
            form.Show();
    }
}
