using System;
using System.Drawing;
using System.Windows.Forms;

namespace PinDesk
{
    // Camada escura por cima de todas as telas; destaca a janela embaixo do mouse e
    // devolve a que foi clicada. Esc ou botão direito cancela.
    internal sealed class PickerForm : Form
    {
        const int EscHotkeyId = 2;
        static Cursor pinCursor;

        readonly PinManager pins;
        readonly HintForm hint = new HintForm();
        IntPtr hover = IntPtr.Zero;
        Rectangle hoverRect = Rectangle.Empty;
        bool done;

        public event Action<IntPtr> Finished;

        public PickerForm(PinManager pins)
        {
            this.pins = pins;
            Text = "PinDesk";
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            Bounds = SystemInformation.VirtualScreen;
            ShowInTaskbar = false;
            TopMost = true;
            BackColor = Color.Black;
            Opacity = 0.25;
            DoubleBuffered = true;
            KeyPreview = true;
            Cursor = PinCursor;
        }

        static Cursor PinCursor
        {
            get
            {
                if (pinCursor == null) pinCursor = CreatePinCursor();
                return pinCursor;
            }
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= Native.WS_EX_TOOLWINDOW;
                return cp;
            }
        }

        // Esc como atalho global enquanto escolhe: funciona mesmo se o Windows não der o foco
        // pra cá (acontece ao clicar no ícone dentro da setinha ^ da bandeja).
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Native.RegisterHotKey(Handle, EscHotkeyId, 0, (uint)Keys.Escape);
        }

        protected override void OnHandleDestroyed(EventArgs e)
        {
            Native.UnregisterHotKey(Handle, EscHotkeyId);
            base.OnHandleDestroyed(e);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Native.WM_HOTKEY && m.WParam.ToInt32() == EscHotkeyId)
            {
                Finish(IntPtr.Zero);
                return;
            }
            base.WndProc(ref m);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            if (done) return;
            Rectangle vs = SystemInformation.VirtualScreen;
            Native.SetWindowPos(Handle, Native.HWND_TOPMOST, vs.X, vs.Y, vs.Width, vs.Height, 0);
            Native.SetForegroundWindow(Handle);
            Activate();
            hint.Show(this);
            UpdateHover(true);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            UpdateHover(false);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button == MouseButtons.Left) Finish(WinUtil.WindowAt(Cursor.Position));
            else if (e.Button == MouseButtons.Right) Finish(IntPtr.Zero);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Escape) Finish(IntPtr.Zero);
        }

        void Finish(IntPtr picked)
        {
            if (done) return;
            done = true;
            hint.Close();
            Close();
            if (Finished != null) Finished(picked);
        }

        void UpdateHover(bool force)
        {
            Point pt = Cursor.Position;
            IntPtr h = WinUtil.WindowAt(pt);
            if (h != hover || force)
            {
                hover = h;
                Rectangle next = Rectangle.Empty;
                if (h != IntPtr.Zero)
                {
                    next = WinUtil.GetFrame(h);
                    next.Offset(-Left, -Top);
                }
                InvalidateArea(hoverRect);
                InvalidateArea(next);
                hoverRect = next;

                string title = h == IntPtr.Zero ? "" : WinUtil.GetTitle(h);
                if (h == IntPtr.Zero)
                    hint.SetText("Clique na janela que quer fixar", "Esc ou botão direito cancela");
                else if (pins.IsPinned(h))
                    hint.SetText("Desafixar: " + title, "Clique pra desafixar  ·  Esc cancela");
                else
                    hint.SetText("Fixar: " + title, "Clique pra fixar  ·  Esc cancela");
            }
            hint.MoveNear(pt);
        }

        void InvalidateArea(Rectangle r)
        {
            if (r.IsEmpty) return;
            r.Inflate(4, 4);
            Invalidate(r);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (hoverRect.IsEmpty) return;
            using (var fill = new SolidBrush(Color.FromArgb(0, 120, 215)))
                e.Graphics.FillRectangle(fill, hoverRect);
            using (var pen = new Pen(Color.FromArgb(120, 210, 255), 6))
            {
                Rectangle r = hoverRect;
                r.Inflate(-3, -3);
                e.Graphics.DrawRectangle(pen, r);
            }
        }

        // Cursor em forma de alfinete com o "clique" na ponta da agulha.
        static Cursor CreatePinCursor()
        {
            try
            {
                int size = Math.Max(32, Native.GetSystemMetrics(Native.SM_CXSMICON) * 2);
                using (Bitmap bmp = IconArt.CreateBitmap(size))
                {
                    IntPtr hIcon = bmp.GetHicon();
                    Native.ICONINFO info;
                    if (!Native.GetIconInfo(hIcon, out info))
                    {
                        Native.DestroyIcon(hIcon);
                        return Cursors.Cross;
                    }
                    PointF tip = IconArt.TipPoint(new RectangleF(0, 0, size, size));
                    info.fIcon = false;
                    info.xHotspot = Math.Max(0, (int)tip.X);
                    info.yHotspot = Math.Min(size - 1, (int)tip.Y);
                    IntPtr hCursor = Native.CreateIconIndirect(ref info);
                    Native.DeleteObject(info.hbmColor);
                    Native.DeleteObject(info.hbmMask);
                    Native.DestroyIcon(hIcon);
                    return hCursor == IntPtr.Zero ? Cursors.Cross : new Cursor(hCursor);
                }
            }
            catch (Exception ex)
            {
                Program.Log(ex);
                return Cursors.Cross;
            }
        }
    }

    // Balãozinho que segue o mouse dizendo qual janela vai ser fixada.
    internal sealed class HintForm : Form
    {
        readonly Font titleFont = new Font("Segoe UI", 10f, FontStyle.Bold);
        readonly Font subFont = new Font("Segoe UI", 8.5f);
        string line1 = "";
        string line2 = "";

        const TextFormatFlags Flags = TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix |
                                      TextFormatFlags.EndEllipsis | TextFormatFlags.Left;

        public HintForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            TopMost = true;
            BackColor = Color.FromArgb(32, 32, 32);
            DoubleBuffered = true;
            Opacity = 0.97;
            Location = new Point(-32000, -32000);
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
                cp.ExStyle |= Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE |
                              Native.WS_EX_LAYERED | Native.WS_EX_TRANSPARENT;
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            int pref = Native.DWMWCP_ROUNDSMALL;
            Native.DwmSetWindowAttribute(Handle, Native.DWMWA_WINDOW_CORNER_PREFERENCE, ref pref, 4);
        }

        public void SetText(string title, string subtitle)
        {
            line1 = title;
            line2 = subtitle;
            int pad = titleFont.Height / 2;
            int maxWidth = titleFont.Height * 28;
            Size s1 = TextRenderer.MeasureText(line1, titleFont, Size.Empty, Flags);
            Size s2 = TextRenderer.MeasureText(line2, subFont, Size.Empty, Flags);
            int w = Math.Min(maxWidth, Math.Max(s1.Width, s2.Width)) + pad * 2;
            int h = s1.Height + s2.Height + pad * 2;
            Size = new Size(w, h);
            Invalidate();
        }

        public void MoveNear(Point pt)
        {
            Rectangle area = Screen.FromPoint(pt).WorkingArea;
            int gap = titleFont.Height;
            int x = pt.X + gap;
            int y = pt.Y + gap;
            if (x + Width > area.Right) x = pt.X - Width - gap / 2;
            if (y + Height > area.Bottom) y = pt.Y - Height - gap / 2;
            Location = new Point(x, y);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            int pad = titleFont.Height / 2;
            int h1 = TextRenderer.MeasureText(line1, titleFont, Size.Empty, Flags).Height;
            var r1 = new Rectangle(pad, pad, Width - pad * 2, h1);
            var r2 = new Rectangle(pad, pad + h1, Width - pad * 2, Height - pad * 2 - h1);
            TextRenderer.DrawText(e.Graphics, line1, titleFont, r1, Color.White, Flags);
            TextRenderer.DrawText(e.Graphics, line2, subFont, r2, Color.FromArgb(190, 190, 190), Flags);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                titleFont.Dispose();
                subFont.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
