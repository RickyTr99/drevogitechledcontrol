using System.Threading;
using System.Windows;
using DrevogitechLedControl.Core;

namespace DrevogitechLedControl
{
    public partial class App : Application
    {
        public static Controller Controller { get; private set; }

        protected override void OnStartup(StartupEventArgs e)
        {
            AppSettings.MigrateLegacyFolder();

            // "--apply": re-apply the saved lighting without UI (used at Windows logon).
            if (e.Args.Any(a => a.Equals("--apply", StringComparison.OrdinalIgnoreCase)))
            {
                ApplySavedAndExit();
                return;
            }

            base.OnStartup(e);
            Controller.RefreshStartupPath();
            Controller = new Controller(AppSettings.Load());
            new MainWindow().Show();
        }

        void ApplySavedAndExit()
        {
            var s = AppSettings.Load();
            // Devices may not be enumerated yet right after logon: retry for ~30 seconds.
            bool kbDone = false, mouseDone = false;
            for (int attempt = 0; attempt < 15 && !(kbDone && mouseDone); attempt++)
            {
                if (!kbDone)
                {
                    try { Drevo.Apply(s.Keyboard); kbDone = true; } catch (Exception) { }
                }
                if (!mouseDone)
                {
                    try
                    {
                        using var m = G502.Open();
                        var zones = Controller.MouseZones(s);
                        m.SetZoneLive(G502.ZoneDpi, zones.dpi);
                        m.SetZoneLive(G502.ZoneLogo, zones.logo);
                        mouseDone = true;
                    }
                    catch (Exception) { }
                }
                if (!(kbDone && mouseDone)) Thread.Sleep(2000);
            }
            Shutdown(0);
        }

        protected override void OnExit(ExitEventArgs e)
        {
            if (Controller != null)
            {
                Controller.Settings.Save();
                Controller.Dispose();
            }
            base.OnExit(e);
        }
    }
}
