using System;
using System.Collections.Generic;
using System.Windows.Forms;

namespace PinDesk
{
    // Guarda as janelas fixadas, o botão de cada uma e os hooks que fazem o botão acompanhar a janela.
    internal sealed class PinManager : IDisposable
    {
        readonly Dictionary<IntPtr, PinOverlay> pins = new Dictionary<IntPtr, PinOverlay>();
        readonly Dictionary<uint, IntPtr> moveHooks = new Dictionary<uint, IntPtr>();
        readonly Native.WinEventDelegate winEventProc;
        readonly IntPtr systemHook;
        readonly Timer timer;

        public event EventHandler Changed;

        public PinManager()
        {
            winEventProc = OnWinEvent;
            // Foco, minimizar/restaurar etc.: reposiciona os botões na hora.
            systemHook = Native.SetWinEventHook(Native.EVENT_SYSTEM_FOREGROUND, Native.EVENT_SYSTEM_MINIMIZEEND,
                IntPtr.Zero, winEventProc, 0, 0, Native.WINEVENT_OUTOFCONTEXT);

            // Rede de segurança: janelas fechadas, apps que tiram o "sempre no topo" sozinhos etc.
            timer = new Timer();
            timer.Interval = 150;
            timer.Tick += OnTick;
            timer.Start();
        }

        public int Count
        {
            get { return pins.Count; }
        }

        public List<IntPtr> Windows
        {
            get { return new List<IntPtr>(pins.Keys); }
        }

        public bool IsPinned(IntPtr h)
        {
            return pins.ContainsKey(h);
        }

        public bool Pin(IntPtr h)
        {
            if (pins.ContainsKey(h)) return true;
            if (!WinUtil.IsPinnable(h) || !WinUtil.SetTopmost(h, true)) return false;

            var overlay = new PinOverlay(h);
            overlay.UnpinRequested += delegate { Unpin(h); };
            pins.Add(h, overlay);
            WatchMoves(overlay.TargetPid);
            overlay.Show();
            RaiseChanged();
            return true;
        }

        public void Unpin(IntPtr h)
        {
            Remove(h, true);
        }

        public void UnpinAll()
        {
            foreach (IntPtr h in Windows)
                Remove(h, true);
        }

        void Remove(IntPtr h, bool restore)
        {
            PinOverlay overlay;
            if (!pins.TryGetValue(h, out overlay)) return;
            pins.Remove(h);

            if (restore && Native.IsWindow(h)) WinUtil.SetTopmost(h, false);
            UnwatchMovesIfUnused(overlay.TargetPid);
            try
            {
                overlay.Close();
                overlay.Dispose();
            }
            catch (Exception ex)
            {
                Program.Log(ex);
            }
            RaiseChanged();
        }

        // Hook só no processo da janela fixada, pra o botão seguir a janela enquanto arrasta.
        void WatchMoves(uint pid)
        {
            if (moveHooks.ContainsKey(pid)) return;
            IntPtr hook = Native.SetWinEventHook(Native.EVENT_OBJECT_LOCATIONCHANGE, Native.EVENT_OBJECT_LOCATIONCHANGE,
                IntPtr.Zero, winEventProc, pid, 0, Native.WINEVENT_OUTOFCONTEXT);
            if (hook != IntPtr.Zero) moveHooks.Add(pid, hook);
        }

        void UnwatchMovesIfUnused(uint pid)
        {
            foreach (PinOverlay o in pins.Values)
                if (o.TargetPid == pid) return;
            IntPtr hook;
            if (moveHooks.TryGetValue(pid, out hook))
            {
                Native.UnhookWinEvent(hook);
                moveHooks.Remove(pid);
            }
        }

        void OnWinEvent(IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
        {
            try
            {
                if (eventType == Native.EVENT_OBJECT_LOCATIONCHANGE)
                {
                    if (idObject != Native.OBJID_WINDOW || idChild != Native.CHILDID_SELF) return;
                    PinOverlay overlay;
                    if (pins.TryGetValue(hwnd, out overlay)) overlay.UpdatePosition();
                }
                else if (pins.Count > 0)
                {
                    foreach (PinOverlay overlay in new List<PinOverlay>(pins.Values))
                        overlay.UpdatePosition();
                }
            }
            catch (Exception ex)
            {
                Program.Log(ex);
            }
        }

        void OnTick(object sender, EventArgs e)
        {
            foreach (IntPtr h in Windows)
            {
                PinOverlay overlay = pins[h];
                // Janela fechada (ou o handle já foi reaproveitado por outro processo).
                if (!Native.IsWindow(h) || WinUtil.GetPid(h) != overlay.TargetPid)
                {
                    Remove(h, false);
                    continue;
                }
                if (!WinUtil.IsTopmost(h)) WinUtil.SetTopmost(h, true);
                overlay.UpdatePosition();
            }
        }

        void RaiseChanged()
        {
            if (Changed != null) Changed(this, EventArgs.Empty);
        }

        public void Dispose()
        {
            timer.Stop();
            timer.Dispose();
            UnpinAll();
            foreach (IntPtr hook in moveHooks.Values)
                Native.UnhookWinEvent(hook);
            moveHooks.Clear();
            if (systemHook != IntPtr.Zero) Native.UnhookWinEvent(systemHook);
        }
    }
}
