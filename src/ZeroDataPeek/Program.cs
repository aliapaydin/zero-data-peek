using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;

namespace ZeroDataPeek;

/// <summary>
/// Giriş noktası. Uygulama tek örnek çalışır: ikinci kez açıldığında (örneğin Dosya Gezgini'nden
/// bir dosyaya çift tıklanınca) istek çalışan pencereye iletilir ve dosya orada yeni sekmede açılır.
/// </summary>
public static class Program
{
    private const string InstanceKey = "ZeroDataPeek.Main";

    [STAThread]
    private static int Main(string[] args)
    {
        WinRT.ComWrappersSupport.InitializeComWrappers();
        if (RedirectToRunningInstance()) return 0;

        Application.Start(p =>
        {
            var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(context);
            _ = new App();
        });
        return 0;
    }

    private static bool RedirectToRunningInstance()
    {
        try
        {
            var activation = AppInstance.GetCurrent().GetActivatedEventArgs();
            var main = AppInstance.FindOrRegisterForKey(InstanceKey);
            if (main.IsCurrent) return false;

            // STA iş parçacığını kilitlemeden beklemek için yönlendirme ayrı görevde yapılır.
            using var done = new Semaphore(0, 1);
            Task.Run(() =>
            {
                try
                {
                    main.RedirectActivationToAsync(activation).AsTask().Wait();
                }
                finally
                {
                    done.Release();
                }
            });
            done.WaitOne(TimeSpan.FromSeconds(10));
            return true;
        }
        catch (Exception)
        {
            return false; // yönlendirilemediyse ayrı pencere olarak aç
        }
    }
}
