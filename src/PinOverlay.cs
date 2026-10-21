using System;
using System.Drawing;
using System.Windows.Forms;

namespace PinDesk
{
    // Botãozinho vermelho com o alfinete que fica na barra de título da janela fixada.
    // Clicar nele desafixa a janela.
    internal sealed class PinOverlay : Form
    {
        static readonly Color Normal = Color.FromArgb(209, 52, 56);
        static readonly Color Hot = Color.FromArgb(150, 30, 34);

        public readonly IntPtr Target;
        public readonly uint TargetPid;
        public event EventHandler UnpinRequested;

        readonly ToolTip tip;
        bool hot;
        bool shown;
        Rectangle lastBounds;

        public PinOverlay(IntPtr target)
        {
            Target = target;
            TargetPid = WinUtil.GetPid(target);

            Text = "PinDesk";
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            TopMost = true;
            Size = new Size(30, 20);
            Location = new Point(-32000, -32000);
            BackColor = Normal;
            DoubleBuffered = true;
            Cursor = Cursors.Hand;

            tip = new ToolTip();
            tip.ShowAlways = true;
            tip.SetToolTip(this, "Desafixar janela");
        }

        protected override bool ShowWithoutActivation
        {
            get { return true; }
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE;
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            int pref = Native.DWMWCP_ROUNDSMALL;
            Native.DwmSetWindowAttribute(Handle, Native.DWMWA_WINDOW_CORNER_PREFERENCE, ref pref, 4);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            shown = true;
            UpdatePosition();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(hot ? Hot : Normal);
            float pad = Height * 0.12f;
            float side = Height - pad * 2;
            var r = new RectangleF((Width - side) / 2f, pad, side, side);
            IconArt.DrawPin(e.Graphics, r, Color.White, Color.White, Color.Empty);
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            hot = true;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            hot = false;
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Left) return;
            // Adia pra fora do evento, já que quem trata vai fechar este form.
            BeginInvoke(new Action(() =>
            {
                if (UnpinRequested != null) UnpinRequested(this, EventArgs.Empty);
            }));
        }

        // Posiciona o botão à esquerda dos botões minimizar/maximizar/fechar da janela alvo.
        public void UpdatePosition()
        {
            if (IsDisposed || !IsHandleCreated || !Native.IsWindow(Target)) return;

            bool visible = Native.IsWindowVisible(Target) && !Native.IsIconic(Target) && !WinUtil.IsCloaked(Target);
            Rectangle frame = Rectangle.Empty;
            if (visible)
            {
                frame = WinUtil.GetFrame(Target);
                if (IsFullscreen(frame)) visible = false;
            }

            if (!visible)
            {
                if (shown)
                {
                    Native.ShowWindow(Handle, Native.SW_HIDE);
                    shown = false;
                }
                return;
            }

            float s = WinUtil.GetScale(Target);
            int w = (int)Math.Round(30 * s);
            int h = (int)Math.Round(20 * s);
            int x = frame.Right - (int)Math.Round(146 * s) - w;
            int minX = frame.Left + (int)Math.Round(8 * s);
            if (x < minX) x = minX;
            int y = frame.Top + (int)Math.Round(6 * s);

            var bounds = new Rectangle(x, y, w, h);
            if (bounds != lastBounds)
            {
                Native.SetWindowPos(Handle, IntPtr.Zero, x, y, w, h, Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
                if (bounds.Size != lastBounds.Size) Invalidate();
                lastBounds = bounds;
            }

            if (!shown)
            {
                Native.ShowWindow(Handle, Native.SW_SHOWNOACTIVATE);
                shown = true;
            }

            SyncZOrder();
        }

        // Mantém o botão logo acima da janela alvo (e não acima de tudo), assim ele some
        // atrás de outra janela fixada que estiver na frente.
        void SyncZOrder()
        {
            IntPtr prev = Native.GetWindow(Target, Native.GW_HWNDPREV);
            if (prev == Handle) return;
            Native.SetWindowPos(Handle, prev == IntPtr.Zero ? Native.HWND_TOPMOST : prev, 0, 0, 0, 0,
                Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE | Native.SWP_NOOWNERZORDER);
        }

        static bool IsFullscreen(Rectangle frame)
        {
            Rectangle screen = Screen.FromRectangle(frame).Bounds;
            return frame.Left <= screen.Left && frame.Top <= screen.Top &&
                   frame.Right >= screen.Right && frame.Bottom >= screen.Bottom;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) tip.Dispose();
            base.Dispose(disposing);
        }
    }
}
