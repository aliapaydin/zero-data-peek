using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using Windows.ApplicationModel.Activation;
using ZeroDataPeek.Services;

namespace ZeroDataPeek;

public partial class App : Application
{
    private MainWindow? _window;

    public App()
    {
        InitializeComponent();

        // Beklenmeyen hatalar uygulamayı kapatmasın; günlüğe yazılsın.
        UnhandledException += (_, e) =>
        {
            CrashLog.Write("UI", e.Exception);
            e.Handled = true;
        };
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            CrashLog.Write("Task", e.Exception);
            e.SetObserved();
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            CrashLog.Write("AppDomain", e.ExceptionObject as Exception);
    }

    protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        // ANSI kod sayfaları (Windows-1254 ile kaydedilmiş Türkçe dosyalar) için.
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
        Loc.Initialize();

        var paths = new List<string>();
        try
        {
            paths = ActivationPaths(AppInstance.GetCurrent().GetActivatedEventArgs());
        }
        catch (Exception ex)
        {
            CrashLog.Write("Etkinleştirme", ex);
        }
        if (paths.Count == 0) paths = ExistingFiles(Environment.GetCommandLineArgs().Skip(1));

        _window = new MainWindow(paths);
        _window.Activate();

        // Sonradan açılan örneklerin istekleri buraya düşer (bkz. Program.cs).
        AppInstance.GetCurrent().Activated += OnRedirected;
    }

    private void OnRedirected(object? sender, AppActivationArguments e)
    {
        List<string> paths;
        try
        {
            paths = ActivationPaths(e);
        }
        catch (Exception ex)
        {
            CrashLog.Write("Yönlendirme", ex);
            paths = new List<string>();
        }
        var window = _window;
        window?.DispatcherQueue.TryEnqueue(() => window.OpenFromActivation(paths));
    }

    /// <summary>Çift tıklanan dosyalar (dosya ilişkilendirmesi) ya da komut satırındaki dosya yolları.</summary>
    private static List<string> ActivationPaths(AppActivationArguments activation)
    {
        if (activation.Kind == ExtendedActivationKind.File && activation.Data is IFileActivatedEventArgs files)
        {
            return files.Files.Select(f => f.Path).Where(p => !string.IsNullOrEmpty(p)).ToList();
        }
        if (activation.Kind == ExtendedActivationKind.Launch && activation.Data is ILaunchActivatedEventArgs launch)
        {
            return ExistingFiles(SplitCommandLine(launch.Arguments ?? string.Empty));
        }
        return new List<string>();
    }

    private static List<string> ExistingFiles(IEnumerable<string> candidates) =>
        candidates
            .Where(p => !string.IsNullOrWhiteSpace(p) && !p.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            .Where(p =>
            {
                try
                {
                    return File.Exists(p);
                }
                catch (Exception)
                {
                    return false;
                }
            })
            .ToList();

    /// <summary>Komut satırını tırnakları gözeterek parçalara ayırır.</summary>
    private static List<string> SplitCommandLine(string commandLine)
    {
        var parts = new List<string>();
        var current = new System.Text.StringBuilder();
        bool quoted = false;
        foreach (char c in commandLine)
        {
            if (c == '"')
            {
                quoted = !quoted;
            }
            else if (char.IsWhiteSpace(c) && !quoted)
            {
                if (current.Length > 0)
                {
                    parts.Add(current.ToString());
                    current.Clear();
                }
            }
            else
            {
                current.Append(c);
            }
        }
        if (current.Length > 0) parts.Add(current.ToString());
        return parts;
    }
}
