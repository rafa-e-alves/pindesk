using System;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace PinDesk
{
    internal static class Program
    {
        const string MutexName = @"Local\PinDesk.SingleInstance";
        const string ShowEventName = @"Local\PinDesk.Show";

        [STAThread]
        static void Main(string[] args)
        {
            bool createdNew;
            using (var mutex = new Mutex(true, MutexName, out createdNew))
            {
                if (!createdNew)
                {
                    // Já tem um rodando: avisa ele e sai.
                    try
                    {
                        using (EventWaitHandle show = EventWaitHandle.OpenExisting(ShowEventName))
                            show.Set();
                    }
                    catch (WaitHandleCannotBeOpenedException) { }
                    return;
                }

                using (var show = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName))
                {
                    EnableDpiAwareness();
                    Application.EnableVisualStyles();
                    Application.SetCompatibleTextRenderingDefault(false);
                    Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
                    Application.ThreadException += (s, e) => Log(e.Exception);

                    bool fromStartup = Array.IndexOf(args, "--startup") >= 0;
                    Application.Run(new TrayApp(fromStartup, show));
                }
                GC.KeepAlive(mutex);
            }
        }

        // Coordenadas reais em pixels em todos os monitores, mesmo com escala diferente.
        static void EnableDpiAwareness()
        {
            try
            {
                if (Native.SetProcessDpiAwarenessContext(new IntPtr(-4))) return; // PER_MONITOR_AWARE_V2
            }
            catch (EntryPointNotFoundException) { }
            Native.SetProcessDPIAware();
        }

        public static void Log(Exception ex)
        {
            try
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PinDesk");
                Directory.CreateDirectory(dir);
                File.AppendAllText(Path.Combine(dir, "erros.log"), DateTime.Now.ToString("s") + "  " + ex + Environment.NewLine);
            }
            catch (Exception) { }
        }
    }
}
