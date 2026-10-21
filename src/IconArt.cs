using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

namespace PinDesk
{
    // Desenha o alfinete do app: ícone da bandeja, cursor, botão de desafixar e o .ico do exe.
    // Só depende de System.Drawing, pra o build.ps1 conseguir gerar o .ico via Add-Type.
    public static class IconArt
    {
        public static readonly Color Red = Color.FromArgb(229, 57, 53);
        public static readonly Color RedDark = Color.FromArgb(140, 20, 20);
        public static readonly Color Steel = Color.FromArgb(170, 170, 178);

        // O alfinete é desenhado em pé numa caixa unitária centrada na origem e depois girado 45°,
        // ficando com a cabeça em cima à direita e a ponta embaixo à esquerda.
        static Matrix PinMatrix(RectangleF b)
        {
            float s = Math.Min(b.Width, b.Height) * 1.05f;
            var m = new Matrix();
            m.Translate(b.X + b.Width / 2f, b.Y + b.Height / 2f);
            m.Rotate(45f);
            m.Scale(s, s);
            m.Translate(0f, 0.09f);
            return m;
        }

        public static void DrawPin(Graphics g, RectangleF bounds, Color body, Color needle, Color outline)
        {
            GraphicsState state = g.Save();
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            using (Matrix m = PinMatrix(bounds))
                g.MultiplyTransform(m);

            using (var pen = new Pen(needle, 0.075f))
            {
                pen.EndCap = LineCap.Round;
                g.DrawLine(pen, 0f, 0.10f, 0f, 0.46f);
            }

            using (var path = new GraphicsPath(FillMode.Winding))
            {
                AddRoundRect(path, -0.21f, -0.47f, 0.42f, 0.14f, 0.06f);  // cabeça
                AddRoundRect(path, -0.115f, -0.36f, 0.23f, 0.37f, 0.03f); // corpo
                AddRoundRect(path, -0.31f, -0.02f, 0.62f, 0.14f, 0.06f);  // base
                if (outline.A > 0)
                {
                    using (var pen = new Pen(outline, 0.09f))
                    {
                        pen.LineJoin = LineJoin.Round;
                        g.DrawPath(pen, path);
                    }
                }
                using (var brush = new SolidBrush(body))
                    g.FillPath(brush, path);
            }

            g.Restore(state);
        }

        // Ponta da agulha, usada como hotspot do cursor.
        public static PointF TipPoint(RectangleF bounds)
        {
            using (Matrix m = PinMatrix(bounds))
            {
                var pts = new[] { new PointF(0f, 0.47f) };
                m.TransformPoints(pts);
                return pts[0];
            }
        }

        public static Bitmap CreateBitmap(int size)
        {
            var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.Transparent);
                DrawPin(g, new RectangleF(0, 0, size, size), Red, Steel, RedDark);
            }
            return bmp;
        }

        public static Icon CreateIcon(int size)
        {
            using (Bitmap bmp = CreateBitmap(size))
                return Icon.FromHandle(bmp.GetHicon());
        }

        public static void SaveIco(string path)
        {
            int[] sizes = { 16, 20, 24, 32, 40, 48, 64, 128, 256 };
            var images = new List<byte[]>();
            foreach (int size in sizes)
                using (Bitmap bmp = CreateBitmap(size))
                    images.Add(ToDib(bmp));

            using (FileStream fs = File.Create(path))
            using (var w = new BinaryWriter(fs))
            {
                w.Write((ushort)0);
                w.Write((ushort)1);
                w.Write((ushort)sizes.Length);
                int offset = 6 + 16 * sizes.Length;
                for (int i = 0; i < sizes.Length; i++)
                {
                    byte dim = (byte)(sizes[i] >= 256 ? 0 : sizes[i]);
                    w.Write(dim);
                    w.Write(dim);
                    w.Write((byte)0);
                    w.Write((byte)0);
                    w.Write((ushort)1);
                    w.Write((ushort)32);
                    w.Write(images[i].Length);
                    w.Write(offset);
                    offset += images[i].Length;
                }
                foreach (byte[] img in images)
                    w.Write(img);
            }
        }

        // Entrada de .ico no formato DIB 32bpp (BGRA de baixo pra cima + máscara AND vazia).
        static byte[] ToDib(Bitmap bmp)
        {
            int s = bmp.Width;
            int maskRow = ((s + 31) / 32) * 4;
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms))
            {
                w.Write(40);
                w.Write(s);
                w.Write(s * 2);
                w.Write((ushort)1);
                w.Write((ushort)32);
                w.Write(0);
                w.Write(s * s * 4 + maskRow * s);
                w.Write(0);
                w.Write(0);
                w.Write(0);
                w.Write(0);

                BitmapData data = bmp.LockBits(new Rectangle(0, 0, s, s), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                try
                {
                    var row = new byte[s * 4];
                    for (int y = s - 1; y >= 0; y--)
                    {
                        Marshal.Copy(data.Scan0 + y * data.Stride, row, 0, row.Length);
                        w.Write(row);
                    }
                }
                finally
                {
                    bmp.UnlockBits(data);
                }

                w.Write(new byte[maskRow * s]);
                w.Flush();
                return ms.ToArray();
            }
        }

        static void AddRoundRect(GraphicsPath path, float x, float y, float w, float h, float r)
        {
            float d = r * 2f;
            path.StartFigure();
            path.AddArc(x, y, d, d, 180, 90);
            path.AddArc(x + w - d, y, d, d, 270, 90);
            path.AddArc(x + w - d, y + h - d, d, d, 0, 90);
            path.AddArc(x, y + h - d, d, d, 90, 90);
            path.CloseFigure();
        }
    }
}
