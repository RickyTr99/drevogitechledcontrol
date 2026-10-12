using System.Threading;
using System.Windows.Media;
using Microsoft.Win32;

namespace DrevogitechLedControl.Core
{
    /// <summary>
    /// Owns settings and device access. All USB traffic runs on one background thread;
    /// lighting requests are "latest wins" so dragging a slider never queues up stale commands.
    /// </summary>
    public sealed class Controller : IDisposable
    {
        public AppSettings Settings { get; }

        public event Action<string, bool> Status;          // message, isError
        public event Action MouseStateChanged;

        readonly Thread worker;
        readonly AutoResetEvent wake = new AutoResetEvent(false);
        readonly object gate = new object();
        readonly Queue<Action> jobs = new Queue<Action>();
        KeyboardSettings pendingKeyboard;
        (ZoneSettings dpi, ZoneSettings logo)? pendingMouseLight;
        DateTime persistLedAt = DateTime.MaxValue;
        volatile bool stopping;

        G502 mouse;
        public MouseProfile MouseDeviceProfile { get; private set; }
        public string MouseName => mouse?.Name;
        public int MouseDpiMin => mouse?.DpiMin ?? 100;
        public int MouseDpiMax => mouse?.DpiMax ?? 25600;
        public int MouseDpiStep => mouse?.DpiStep ?? 50;

        public Controller(AppSettings settings)
        {
            Settings = settings;
            worker = new Thread(Loop) { IsBackground = true, Name = "Dispositivi" };
            worker.Start();
        }

        // ---------------- Public API (UI thread) ----------------

        public void ApplyKeyboard()
        {
            lock (gate) pendingKeyboard = Settings.Keyboard.Clone();
            if (Settings.SyncMouse) ApplyMouseLighting();
            wake.Set();
        }

        public void ApplyMouseLighting()
        {
            var zones = MouseZones(Settings);
            lock (gate)
            {
                pendingMouseLight = (zones.dpi.Clone(), zones.logo.Clone());
                persistLedAt = DateTime.UtcNow.AddSeconds(4);
            }
            wake.Set();
        }

        public void ApplyAll()
        {
            lock (gate) pendingKeyboard = Settings.Keyboard.Clone();
            ApplyMouseLighting();
        }

        /// <summary>Runs work on the device thread and returns its result.</summary>
        public Task<T> Run<T>(Func<T> work)
        {
            var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (gate)
                jobs.Enqueue(() =>
                {
                    try { tcs.SetResult(work()); }
                    catch (Exception ex) { tcs.SetException(ex); }
                });
            wake.Set();
            return tcs.Task;
        }

        public Task<bool> ConnectMouseAsync() => Run(() => EnsureMouse());

        public Task SaveMouseProfileAsync(MouseProfile edited) => Run(() =>
        {
            if (!EnsureMouse()) throw new InvalidOperationException("Mouse non collegato");
            var p = edited.Clone();
            var zones = MouseZones(Settings);
            p.SetLed(G502.StoredLedDpi, G502.EncodeStoredLed(zones.dpi));
            p.SetLed(G502.StoredLedLogo, G502.EncodeStoredLed(zones.logo));
            mouse.SaveProfile(p, reload: true);
            MouseDeviceProfile = p;
            lock (gate) persistLedAt = DateTime.MaxValue;
            // reloading the profile resets live lighting to the stored one, which is now identical
            ApplyMouseZones(zones.dpi, zones.logo);
            return true;
        });

        public Task<bool?> GetDpiIndicatorAlwaysOnAsync() => Run(() => EnsureMouse() ? mouse.DpiIndicatorAlwaysOn() : null);

        public Task SetDpiIndicatorAlwaysOnAsync(bool on) => Run(() =>
        {
            if (!EnsureMouse()) throw new InvalidOperationException("Mouse non collegato");
            mouse.SetDpiIndicatorAlwaysOn(on);
            // The switch re-initializes the LEDs from the stored profile; re-apply the live state as well.
            var zones = MouseZones(Settings);
            ApplyMouseZones(zones.dpi, zones.logo);
            return true;
        });

        public Task RestoreMouseBackupAsync() => Run(() =>
        {
            if (!EnsureMouse()) throw new InvalidOperationException("Mouse non collegato");
            mouse.RestoreBackup();
            MouseDeviceProfile = mouse.ReadActiveProfile();
            return true;
        });

        // ---------------- Sync logic ----------------

        /// <summary>Mouse zone settings, either set by hand or derived from the keyboard.</summary>
        public static (ZoneSettings dpi, ZoneSettings logo) MouseZones(AppSettings s)
        {
            if (!s.SyncMouse) return (s.MouseDpi, s.MouseLogo);
            var k = s.Keyboard;
            var z = new ZoneSettings { Brightness = BalancedBrightness(k.Brightness, s.SyncBrightnessBalance), Speed = k.Speed, Color = k.Color1 };
            switch (k.Mode)
            {
                case EffectMode.Static:
                case EffectMode.Stream:
                    z.Effect = k.Rainbow ? MouseEffect.Cycle : MouseEffect.Fixed;
                    break;
                case EffectMode.Breath:
                    z.Effect = MouseEffect.Breathing;
                    break;
                case EffectMode.PerKey:
                    z.Effect = MouseEffect.Fixed;
                    z.Color = DominantColor(k.Keys.Values);
                    break;
                default:
                    z.Effect = MouseEffect.Fixed;
                    break;
            }
            return (z, z.Clone());
        }

        /// <summary>
        /// Maps the keyboard brightness to the mouse. The keyboard's hardware levels look brighter than the
        /// mouse at the same percentage, so an exponent (from 2.5 at balance 0 to 0.4 at 100, 1 in the middle)
        /// lifts or lowers the mouse curve; the top and bottom (off / full) stay aligned.
        /// </summary>
        public static int BalancedBrightness(int keyboardPercent, int balance)
        {
            if (keyboardPercent <= 0) return 0;
            if (keyboardPercent >= 100) return 100;
            double g = 2.5 * Math.Pow(0.16, Math.Clamp(balance, 0, 100) / 100.0);
            // brightness values are perceptual (squared later), hence g / 2
            return Math.Clamp((int)Math.Round(100 * Math.Pow(keyboardPercent / 100.0, g / 2)), 1, 100);
        }

        /// <summary>Most used non-black key color.</summary>
        public static Color DominantColor(IEnumerable<Color> colors)
        {
            var best = colors.Where(c => c.R + c.G + c.B > 0).GroupBy(c => c).OrderByDescending(g => g.Count()).FirstOrDefault();
            return best?.Key ?? Colors.White;
        }

        // ---------------- Device thread ----------------

        void Loop()
        {
            while (!stopping)
            {
                wake.WaitOne(500);
                while (true)
                {
                    KeyboardSettings kb; (ZoneSettings dpi, ZoneSettings logo)? ml; Action job = null; bool persist = false;
                    lock (gate)
                    {
                        kb = pendingKeyboard; pendingKeyboard = null;
                        ml = pendingMouseLight; pendingMouseLight = null;
                        if (jobs.Count > 0) job = jobs.Dequeue();
                        if (kb == null && ml == null && DateTime.UtcNow >= persistLedAt)
                        {
                            persist = true;
                            persistLedAt = DateTime.MaxValue;
                        }
                    }
                    if (kb == null && ml == null && job == null && !persist) break;

                    if (kb != null)
                    {
                        try { Drevo.Apply(kb); Report("Tastiera aggiornata", false); }
                        catch (Exception ex) { Report("Tastiera: " + ex.Message, true); }
                    }
                    if (ml != null)
                    {
                        try { if (EnsureMouse()) ApplyMouseZones(ml.Value.dpi, ml.Value.logo); }
                        catch (Exception ex) { DropMouse(); Report("Mouse: " + ex.Message, true); }
                    }
                    if (persist) PersistLedsToMouse();
                    job?.Invoke();
                }
            }
        }

        void ApplyMouseZones(ZoneSettings dpi, ZoneSettings logo)
        {
            mouse.SetZoneLive(G502.ZoneDpi, dpi);
            mouse.SetZoneLive(G502.ZoneLogo, logo);
        }

        /// <summary>Stores the current lighting in the mouse so it survives unplugging (debounced to spare flash writes).</summary>
        void PersistLedsToMouse()
        {
            try
            {
                if (!EnsureMouse() || MouseDeviceProfile == null) return;
                var zones = MouseZones(Settings);
                var p = MouseDeviceProfile.Clone();
                p.SetLed(G502.StoredLedDpi, G502.EncodeStoredLed(zones.dpi));
                p.SetLed(G502.StoredLedLogo, G502.EncodeStoredLed(zones.logo));
                if (p.Raw.SequenceEqual(MouseDeviceProfile.Raw)) return;
                mouse.SaveProfile(p, reload: false);
                MouseDeviceProfile = p;
                Report("Luci salvate nella memoria del mouse", false);
            }
            catch (Exception ex) { DropMouse(); Report("Mouse: " + ex.Message, true); }
        }

        bool EnsureMouse()
        {
            if (mouse != null) return true;
            if (!G502.IsConnected) return false;
            try
            {
                mouse = G502.Open();
                MouseDeviceProfile = mouse.ReadActiveProfile();
                MouseStateChanged?.Invoke();
                return true;
            }
            catch (Exception ex)
            {
                DropMouse();
                Report("Mouse: " + ex.Message, true);
                return false;
            }
        }

        void DropMouse()
        {
            mouse?.Dispose();
            mouse = null;
            MouseDeviceProfile = null;
            MouseStateChanged?.Invoke();
        }

        void Report(string msg, bool error) => Status?.Invoke(msg, error);

        public void Dispose()
        {
            // flush a pending lighting save before quitting
            bool flush;
            lock (gate) flush = persistLedAt != DateTime.MaxValue;
            if (flush) Run(() => { PersistLedsToMouse(); return true; }).Wait(3000);
            stopping = true;
            wake.Set();
            worker.Join(2000);
            mouse?.Dispose();
        }

        // ---------------- Windows startup ----------------

        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string RunValue = "DrevogitechLedControl", LegacyRunValue = "DrevoControl";

        /// <summary>Points an existing startup entry at this executable (e.g. after a rename).</summary>
        public static void RefreshStartupPath()
        {
            using (var k = Registry.CurrentUser.CreateSubKey(RunKey))
            {
                // entry created before the rename: replace it with the new one
                if (k.GetValue(LegacyRunValue) != null)
                {
                    k.DeleteValue(LegacyRunValue, false);
                    k.SetValue(RunValue, "");
                }
            }
            if (StartupEnabled) StartupEnabled = true;
        }

        public static bool StartupEnabled
        {
            get
            {
                using var k = Registry.CurrentUser.OpenSubKey(RunKey);
                return k?.GetValue(RunValue) != null;
            }
            set
            {
                using var k = Registry.CurrentUser.CreateSubKey(RunKey);
                if (value) k.SetValue(RunValue, $"\"{Environment.ProcessPath}\" --apply");
                else k.DeleteValue(RunValue, false);
            }
        }
    }
}
