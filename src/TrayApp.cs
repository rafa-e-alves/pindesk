using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace PinDesk
{
    // Ícone na bandeja (a setinha ^ perto do relógio) e o menu dele.
    internal sealed class TrayApp : ApplicationContext
    {
        const string AppName = "PinDesk";
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

        readonly PinManager pins = new PinManager();
        readonly ContextMenuStrip menu = new ContextMenuStrip();
        readonly NotifyIcon tray = new NotifyIcon();
        readonly HotkeyWindow hotkey;
        readonly Font boldFont;
        readonly int iconSize;
        readonly SynchronizationContext ui;
        readonly RegisteredWaitHandle showWait;
        PickerForm picker;

        public TrayApp(bool fromStartup, EventWaitHandle showSignal)
        {
            iconSize = Math.Max(16, Native.GetSystemMetrics(Native.SM_CXSMICON));
            boldFont = new Font(menu.Font, FontStyle.Bold);
            menu.ImageScalingSize = new Size(iconSize, iconSize);
            menu.Items.Add("...");
            menu.Opening += OnMenuOpening;

            tray.Icon = IconArt.CreateIcon(iconSize);
            tray.ContextMenuStrip = menu;
            tray.MouseClick += OnTrayClick;
            pins.Changed += delegate { UpdateTrayText(); };
            UpdateTrayText();
            tray.Visible = true;

            hotkey = new HotkeyWindow(Native.MOD_CONTROL | Native.MOD_ALT, Keys.P);
            hotkey.Pressed += delegate { TogglePin(Native.GetForegroundWindow(), false); };

            ui = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
            // Abrir o exe de novo só avisa que já está rodando.
            showWait = ThreadPool.RegisterWaitForSingleObject(showSignal,
                delegate { ui.Post(delegate { ShowRunningTip(); }, null); }, null, -1, false);

            if (IsStartupEnabled()) SetStartup(true); // atualiza o caminho se o exe mudou de lugar
            if (!fromStartup) ShowRunningTip();
        }

        void ShowRunningTip()
        {
            string text = "Tô aqui na bandeja. Clica no alfinete e depois na janela que quer fixar.";
            if (hotkey.Registered) text += " Ou aperta Ctrl+Alt+P na janela ativa.";
            tray.ShowBalloonTip(4000, AppName, text, ToolTipIcon.Info);
        }

        void UpdateTrayText()
        {
            int n = pins.Count;
            tray.Text = n == 0 ? AppName + " - clique pra fixar uma janela"
                      : n == 1 ? AppName + " - 1 janela fixada"
                      : AppName + " - " + n + " janelas fixadas";
        }

        void OnTrayClick(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            // Dá um tempinho pro painel da setinha ^ fechar antes de cobrir a tela.
            var delay = new System.Windows.Forms.Timer();
            delay.Interval = 250;
            delay.Tick += delegate
            {
                delay.Dispose();
                StartPick();
            };
            delay.Start();
        }

        void StartPick()
        {
            if (picker != null) return;
            picker = new PickerForm(pins);
            picker.Finished += h =>
            {
                picker = null;
                if (h != IntPtr.Zero) TogglePin(h, true);
            };
            picker.Show();
        }

        void TogglePin(IntPtr h, bool activate)
        {
            if (pins.IsPinned(h))
            {
                pins.Unpin(h);
                return;
            }
            if (!WinUtil.IsPinnable(h)) return;
            if (!pins.Pin(h))
            {
                tray.ShowBalloonTip(5000, AppName,
                    "Não consegui fixar essa janela. Se ela foi aberta como administrador, " +
                    "abre o PinDesk como administrador também.", ToolTipIcon.Warning);
                return;
            }
            if (activate)
            {
                if (Native.IsIconic(h)) Native.ShowWindow(h, Native.SW_RESTORE);
                Native.SetForegroundWindow(h);
            }
        }

        void OnMenuOpening(object sender, CancelEventArgs e)
        {
            DisposeItems(menu.Items);

            var pick = new ToolStripMenuItem("Fixar uma janela...", null, delegate { StartPick(); });
            pick.Font = boldFont;
            menu.Items.Add(pick);

            menu.Items.Add(BuildWindowsMenu());

            int n = pins.Count;
            var unpinAll = new ToolStripMenuItem(n > 0 ? "Desafixar todas (" + n + ")" : "Desafixar todas",
                null, delegate { pins.UnpinAll(); });
            unpinAll.Enabled = n > 0;
            menu.Items.Add(unpinAll);

            menu.Items.Add(new ToolStripSeparator());

            var startup = new ToolStripMenuItem("Iniciar com o Windows", null,
                delegate { SetStartup(!IsStartupEnabled()); });
            startup.Checked = IsStartupEnabled();
            menu.Items.Add(startup);

            var shortcut = new ToolStripMenuItem(hotkey.Registered
                ? "Atalho: Ctrl+Alt+P fixa a janela ativa"
                : "Atalho Ctrl+Alt+P já é usado por outro app");
            shortcut.Enabled = false;
            menu.Items.Add(shortcut);

            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(new ToolStripMenuItem("Sair", null, delegate { Quit(); }));

            e.Cancel = false;
        }

        ToolStripMenuItem BuildWindowsMenu()
        {
            var root = new ToolStripMenuItem("Janelas abertas");
            root.DropDown.ImageScalingSize = menu.ImageScalingSize;

            List<IntPtr> pinned = pins.Windows;
            var others = new List<IntPtr>();
            foreach (IntPtr h in WinUtil.ListAppWindows())
                if (!pins.IsPinned(h)) others.Add(h);

            foreach (IntPtr h in pinned) root.DropDownItems.Add(WindowItem(h, true));
            if (pinned.Count > 0 && others.Count > 0) root.DropDownItems.Add(new ToolStripSeparator());
            foreach (IntPtr h in others) root.DropDownItems.Add(WindowItem(h, false));

            if (root.DropDownItems.Count == 0)
            {
                var empty = new ToolStripMenuItem("(nenhuma janela)");
                empty.Enabled = false;
                root.DropDownItems.Add(empty);
            }
            return root;
        }

        ToolStripMenuItem WindowItem(IntPtr h, bool isPinned)
        {
            string title = WinUtil.GetTitle(h);
            if (title.Length == 0) title = "(sem título)";
            if (title.Length > 60) title = title.Substring(0, 57) + "...";
            var item = new ToolStripMenuItem(title.Replace("&", "&&"), WinUtil.GetIconBitmap(h, iconSize),
                delegate { TogglePin(h, true); });
            item.Checked = isPinned;
            item.ToolTipText = isPinned ? "Clique pra desafixar" : "Clique pra fixar";
            return item;
        }

        static void DisposeItems(ToolStripItemCollection items)
        {
            var list = new List<ToolStripItem>();
            foreach (ToolStripItem item in items) list.Add(item);
            items.Clear();
            foreach (ToolStripItem item in list)
            {
                var menuItem = item as ToolStripMenuItem;
                if (menuItem != null) DisposeItems(menuItem.DropDownItems);
                Image img = item.Image;
                item.Image = null;
                if (img != null) img.Dispose();
                item.Dispose();
            }
        }

        static string StartupCommand
        {
            get { return "\"" + Application.ExecutablePath + "\" --startup"; }
        }

        static bool IsStartupEnabled()
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKey))
                return key != null && key.GetValue(AppName) != null;
        }

        static void SetStartup(bool enabled)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKey))
                {
                    if (enabled) key.SetValue(AppName, StartupCommand);
                    else key.DeleteValue(AppName, false);
                }
            }
            catch (Exception ex)
            {
                Program.Log(ex);
            }
        }

        void Quit()
        {
            showWait.Unregister(null);
            pins.Dispose();
            hotkey.Dispose();
            tray.Visible = false;
            tray.Dispose();
            DisposeItems(menu.Items);
            menu.Dispose();
            ExitThread();
        }
    }

    // Janela invisível só pra receber o atalho global.
    internal sealed class HotkeyWindow : NativeWindow, IDisposable
    {
        const int Id = 1;

        public event EventHandler Pressed;
        public bool Registered { get; private set; }

        public HotkeyWindow(uint modifiers, Keys key)
        {
            var cp = new CreateParams();
            cp.Parent = Native.HWND_MESSAGE;
            CreateHandle(cp);
            Registered = Native.RegisterHotKey(Handle, Id, modifiers | Native.MOD_NOREPEAT, (uint)key);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Native.WM_HOTKEY && m.WParam.ToInt32() == Id && Pressed != null)
                Pressed(this, EventArgs.Empty);
            base.WndProc(ref m);
        }

        public void Dispose()
        {
            if (Registered) Native.UnregisterHotKey(Handle, Id);
            DestroyHandle();
        }
    }
}
