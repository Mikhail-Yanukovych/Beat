// Beat - player de musica leve para Windows (busca no YouTube, toca so o audio)
// Compila com o csc.exe que ja vem no Windows (C# 5). Veja build.bat.
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

namespace Beat
{
    class Track
    {
        public string Id;
        public string Title;
        public string Channel;
        public string Duration;
        public Track Copy()
        {
            Track t = new Track();
            t.Id = Id; t.Title = Title; t.Channel = Channel; t.Duration = Duration;
            return t;
        }
    }

    class Playlist
    {
        public string Name;
        public string Cover;   // null = primeira musica; "t:ID" = capa de outra musica; "f:arquivo" = imagem propria
        public List<Track> Items = new List<Track>();
    }

    static class Native
    {
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, string l);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int RegisterWindowMessage(string name);
        [DllImport("user32.dll")]
        public static extern bool PostMessage(IntPtr h, int msg, IntPtr w, IntPtr l);
        public static readonly int WM_BEATLINK = RegisterWindowMessage("BeatPlayerOpenLink");
        [DllImport("user32.dll")]
        public static extern bool ReleaseCapture();
        [DllImport("user32.dll", EntryPoint = "SendMessage")]
        public static extern IntPtr SendMessageI(IntPtr h, int msg, int w, int l);
        [DllImport("user32.dll")]
        public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
        [DllImport("user32.dll")]
        public static extern bool UnregisterHotKey(IntPtr hWnd, int id);
        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        public static extern int SetWindowTheme(IntPtr hWnd, string appName, string idList);
        [DllImport("dwmapi.dll")]
        public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        public static void DarkTitle(IntPtr h)
        {
            try
            {
                int v = 1;
                DwmSetWindowAttribute(h, 20, ref v, 4);
                DwmSetWindowAttribute(h, 19, ref v, 4);
            }
            catch (Exception) { }
        }

        public static void DarkScroll(IntPtr h)
        {
            try { SetWindowTheme(h, "DarkMode_Explorer", null); } catch (Exception) { }
        }
    }

    class DarkColors : ProfessionalColorTable
    {
        static readonly Color Bg = Color.FromArgb(36, 36, 36);
        static readonly Color Hot = Color.FromArgb(72, 72, 72);
        public override Color ToolStripDropDownBackground { get { return Bg; } }
        public override Color ImageMarginGradientBegin { get { return Bg; } }
        public override Color ImageMarginGradientMiddle { get { return Bg; } }
        public override Color ImageMarginGradientEnd { get { return Bg; } }
        public override Color MenuBorder { get { return Color.FromArgb(70, 70, 70); } }
        public override Color MenuItemBorder { get { return Hot; } }
        public override Color MenuItemSelected { get { return Hot; } }
        public override Color MenuItemSelectedGradientBegin { get { return Hot; } }
        public override Color MenuItemSelectedGradientEnd { get { return Hot; } }
        public override Color MenuItemPressedGradientBegin { get { return Hot; } }
        public override Color MenuItemPressedGradientMiddle { get { return Hot; } }
        public override Color MenuItemPressedGradientEnd { get { return Hot; } }
        public override Color SeparatorDark { get { return Color.FromArgb(70, 70, 70); } }
        public override Color SeparatorLight { get { return Color.FromArgb(70, 70, 70); } }
    }

    class DBListBox : ListBox
    {
        public bool AllowDrag;
        public int HandleWidth;      // >0: so arrasta pela alca (x < HandleWidth)
        public event Action<int, int> Reordered;
        int dragFrom = -1, dropAt = -1;
        Point downPt;
        bool dragging;

        public DBListBox() { this.DoubleBuffered = true; }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            dragFrom = -1; dragging = false;
            if (!AllowDrag || e.Button != MouseButtons.Left) return;
            int i = IndexFromPoint(e.Location);
            if (i >= 0 && (HandleWidth <= 0 || e.X < HandleWidth)) { dragFrom = i; downPt = e.Location; }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (dragFrom < 0 || (Control.MouseButtons & MouseButtons.Left) == 0) return;
            if (!dragging)
            {
                if (Math.Abs(e.Y - downPt.Y) < 6) return;
                dragging = true;
                Cursor = Cursors.SizeNS;
            }
            if (e.Y < 0 && TopIndex > 0) TopIndex--;
            else if (e.Y > Height && TopIndex < Items.Count - 1) TopIndex++;
            int at;
            int i = IndexFromPoint(new Point(Math.Min(Width - 2, Math.Max(2, e.X)), Math.Min(Height - 2, Math.Max(2, e.Y))));
            if (i < 0) at = Items.Count;
            else
            {
                Rectangle r = GetItemRectangle(i);
                at = e.Y > r.Top + r.Height / 2 ? i + 1 : i;
            }
            if (at != dropAt) { dropAt = at; Invalidate(); }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (dragging)
            {
                int from = dragFrom, at = dropAt;
                dragFrom = -1; dropAt = -1; dragging = false;
                Cursor = Cursors.Default;
                Invalidate();
                if (at >= 0)
                {
                    int target = at > from ? at - 1 : at;
                    if (target != from && Reordered != null) Reordered(from, target);
                }
            }
            dragFrom = -1;
        }

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            if (m.Msg == 0x000F && dragging && dropAt >= 0 && Items.Count > 0)
            {
                int y;
                if (dropAt < Items.Count) y = GetItemRectangle(dropAt).Top;
                else y = GetItemRectangle(Items.Count - 1).Bottom;
                using (Graphics g = Graphics.FromHwnd(Handle))
                using (SolidBrush b = new SolidBrush(Color.FromArgb(255, 80, 80)))
                    g.FillRectangle(b, 8, Math.Max(0, y - 1), Width - 16, 3);
            }
        }
    }

    class SlideView : Control
    {
        public Bitmap Old, New;
        public bool FromRight;
        public float T;

        public SlideView()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Color.FromArgb(14, 14, 14));
            if (Old == null || New == null) return;
            int W = Width;
            float k = 1f - (float)Math.Pow(1f - Math.Min(1f, T), 3);
            float newX = FromRight ? (1f - k) * W : -(1f - k) * W;
            float oldX = FromRight ? -k * W * 0.4f : k * W * 0.4f;
            using (ImageAttributes ia = new ImageAttributes())
            {
                ColorMatrix cm = new ColorMatrix();
                cm.Matrix33 = Math.Max(0f, 1f - k);
                ia.SetColorMatrix(cm);
                g.DrawImage(Old, new Rectangle((int)oldX, 0, W, Height), 0, 0, Old.Width, Old.Height, GraphicsUnit.Pixel, ia);
            }
            g.DrawImage(New, new Rectangle((int)newX, 0, W, Height), 0, 0, New.Width, New.Height, GraphicsUnit.Pixel);
        }
    }

    class LyricsView : Control
    {
        public string[] Lines = new string[0];
        public double[] Times = new double[0];
        public bool Synced;
        public int Cur = -1;
        public event Action<int> LineClicked;
        float scroll, target;
        const int LH = 42;
        Font fCur = new Font("Segoe UI", 19f, FontStyle.Bold);
        Font fOth = new Font("Segoe UI", 15f);
        System.Windows.Forms.Timer anim = new System.Windows.Forms.Timer();

        public LyricsView()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Color.FromArgb(24, 24, 24);
            anim.Interval = 30;
            anim.Tick += delegate
            {
                float d = target - scroll;
                if (Math.Abs(d) < 0.01f) { scroll = target; anim.Stop(); }
                else scroll += d * 0.2f;
                Invalidate();
            };
        }

        public void SetData(string[] lines, double[] times, bool synced)
        {
            Lines = lines; Times = times; Synced = synced; Cur = -1;
            Cursor = synced ? Cursors.Hand : Cursors.Default;
            scroll = 0; target = 0;
            Invalidate();
        }

        public void SetCurrent(int i)
        {
            if (i == Cur) return;
            Cur = i;
            target = Math.Max(0, i);
            anim.Start();
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (!Synced || e.Button != MouseButtons.Left || LineClicked == null) return;
            int i = (int)Math.Floor((e.Y - (Height / 2 - LH / 2)) / (float)LH + scroll);
            if (i >= 0 && i < Lines.Length && i < Times.Length) LineClicked(i);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if (Synced) return;
            target = Math.Max(0, Math.Min(Lines.Length - 1, target - e.Delta / 120f * 1.5f));
            anim.Start();
        }

        static Color Mix(Color a, Color b, float t)
        {
            return Color.FromArgb((int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(BackColor);
            int cy = Height / 2;
            for (int i = 0; i < Lines.Length; i++)
            {
                float off = i - scroll;
                int y = cy + (int)(off * LH) - LH / 2;
                if (y < -LH || y > Height) continue;
                float dist = Math.Abs(off);
                bool cur = Synced && i == Cur;
                Color c = cur ? Color.White : Mix(Color.FromArgb(150, 150, 150), BackColor, Math.Min(1f, Math.Max(0f, (dist - 1f) * 0.28f)));
                if (!Synced) c = Color.FromArgb(205, 205, 205);
                Rectangle r = new Rectangle(10, y, Width - 20, LH);
                TextRenderer.DrawText(g, Lines[i], cur ? fCur : fOth, r, c,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            }
        }
    }

    static class Gfx
    {
        // coracao desenhado (centrado em cx,cy; w = largura total) - formato classico
        public static void Heart(Graphics g, float cx, float cy, float w, Color c, bool filled)
        {
            float sc = w / 20f;
            float ox = cx - 12f * sc, oy = cy - 12.175f * sc;
            using (GraphicsPath gp = new GraphicsPath())
            {
                gp.AddLine(ox + 12f * sc, oy + 21.35f * sc, ox + 10.55f * sc, oy + 20.03f * sc);
                gp.AddBezier(ox + 10.55f * sc, oy + 20.03f * sc, ox + 5.4f * sc, oy + 15.36f * sc, ox + 2f * sc, oy + 12.28f * sc, ox + 2f * sc, oy + 8.5f * sc);
                gp.AddBezier(ox + 2f * sc, oy + 8.5f * sc, ox + 2f * sc, oy + 5.42f * sc, ox + 4.42f * sc, oy + 3f * sc, ox + 7.5f * sc, oy + 3f * sc);
                gp.AddBezier(ox + 7.5f * sc, oy + 3f * sc, ox + 9.24f * sc, oy + 3f * sc, ox + 10.91f * sc, oy + 3.81f * sc, ox + 12f * sc, oy + 5.09f * sc);
                gp.AddBezier(ox + 12f * sc, oy + 5.09f * sc, ox + 13.09f * sc, oy + 3.81f * sc, ox + 14.76f * sc, oy + 3f * sc, ox + 16.5f * sc, oy + 3f * sc);
                gp.AddBezier(ox + 16.5f * sc, oy + 3f * sc, ox + 19.58f * sc, oy + 3f * sc, ox + 22f * sc, oy + 5.42f * sc, ox + 22f * sc, oy + 8.5f * sc);
                gp.AddBezier(ox + 22f * sc, oy + 8.5f * sc, ox + 22f * sc, oy + 12.28f * sc, ox + 18.6f * sc, oy + 15.36f * sc, ox + 13.45f * sc, oy + 20.03f * sc);
                gp.CloseFigure();
                SmoothingMode old = g.SmoothingMode;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                if (filled) using (SolidBrush b = new SolidBrush(c)) g.FillPath(b, gp);
                else using (Pen pn = new Pen(c, Math.Max(1.6f, w * 0.09f))) { pn.LineJoin = LineJoin.Round; g.DrawPath(pn, gp); }
                g.SmoothingMode = old;
            }
        }

        public static GraphicsPath RoundRect(Rectangle r, int radius)
        {
            int d = radius * 2;
            GraphicsPath p = new GraphicsPath();
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        // desenha a miniatura com cantos arredondados (ou um quadrado escuro enquanto carrega)
        public static void DrawThumb(Graphics g, Rectangle r, Bitmap bmp, int radius)
        {
            GraphicsState st = g.Save();
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath gp = RoundRect(new Rectangle(r.X, r.Y, r.Width - 1, r.Height - 1), radius))
            {
                if (bmp == null)
                {
                    using (SolidBrush b = new SolidBrush(Color.FromArgb(36, 36, 36))) g.FillPath(b, gp);
                }
                else
                {
                    g.SetClip(gp, CombineMode.Replace);
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.DrawImage(bmp, r);
                }
            }
            g.Restore(st);
        }
    }

    class RoundPanel : Panel
    {
        public int Radius = 10;
        public Color FillColor = Color.FromArgb(40, 40, 40);
        public Color BorderColor = Color.Empty;

        public RoundPanel()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Parent != null ? Parent.BackColor : BackColor);
            using (GraphicsPath gp = Gfx.RoundRect(new Rectangle(0, 0, Width - 1, Height - 1), Radius))
            using (SolidBrush b = new SolidBrush(FillColor))
                g.FillPath(b, gp);
            if (!BorderColor.IsEmpty)
                using (GraphicsPath gp2 = Gfx.RoundRect(new Rectangle(0, 0, Width - 1, Height - 1), Radius))
                using (Pen pn = new Pen(BorderColor, 1f))
                    g.DrawPath(pn, gp2);
        }
    }

    class ToggleSwitch : Control
    {
        bool on;
        public event EventHandler Changed;
        public Color ClearColor = Color.FromArgb(30, 30, 30);

        public ToggleSwitch()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Size = new Size(42, 24);
            Cursor = Cursors.Hand;
        }

        public bool Checked
        {
            get { return on; }
            set { if (on != value) { on = value; Invalidate(); } }
        }

        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);
            on = !on;
            Invalidate();
            if (Changed != null) Changed(this, EventArgs.Empty);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(ClearColor);
            using (GraphicsPath gp = Gfx.RoundRect(new Rectangle(0, 0, Width - 1, Height - 1), Height / 2 - 1))
            using (SolidBrush b = new SolidBrush(on ? Color.FromArgb(255, 80, 80) : Color.FromArgb(70, 70, 70)))
                g.FillPath(b, gp);
            int d = Height - 8;
            int x = on ? Width - d - 4 : 4;
            using (SolidBrush w = new SolidBrush(Color.White)) g.FillEllipse(w, x, 4, d, d);
        }
    }

    class ThumbBox : Panel
    {
        public Bitmap Thumb;
        public bool Fit;   // true: preenche o controle inteiro; false: miniatura fixa 88x50
        public bool Edit;  // mostra um lapis ao passar o mouse
        bool hov;

        public ThumbBox() { DoubleBuffered = true; ResizeRedraw = true; }

        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); if (Edit) { hov = true; Invalidate(); } }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); if (hov) { hov = false; Invalidate(); } }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(BackColor);
            Rectangle r = Fit ? new Rectangle(0, 0, Width, Height) : new Rectangle(0, 0, 88, 50);
            int rad = Fit ? (Edit ? 6 : 10) : 6;
            Gfx.DrawThumb(e.Graphics, r, Thumb, rad);
            if (Edit && hov)
            {
                Graphics g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (GraphicsPath gp = Gfx.RoundRect(new Rectangle(0, 0, Width - 1, Height - 1), rad))
                using (SolidBrush b = new SolidBrush(Color.FromArgb(150, 0, 0, 0)))
                    g.FillPath(b, gp);
                using (Font f = new Font("Segoe UI Symbol", 13f))
                {
                    TextRenderer.DrawText(g, "\u270E", f, new Rectangle(0, 0, Width, Height), Color.White,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                }
            }
        }
    }

    // painel com rolagem que recebe foco (para a roda do mouse funcionar)
    class ScrollPanel : Panel
    {
        public ScrollPanel()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.Selectable, true);
            TabStop = true;
            AutoScroll = true;
        }

        protected override Point ScrollToControl(Control activeControl) { return DisplayRectangle.Location; }
    }

    // Botao desenhado por nos: texto/icone, redondo, retangulo arredondado ou sublinhado.
    class IconButton : Button
    {
        public bool Filled;
        public int Radius;          // >0: retangulo arredondado; 0: circulo (quando Filled)
        public bool Underline;
        public string Glyph;        // "play", "pause", "prev", "next" (desenhados) ou null (usa o texto)
        public bool Outline;        // contorno arredondado
        public Color OutlineColor = Color.FromArgb(62, 62, 62);
        public bool Star;           // desenha uma estrelinha antes do texto
        public Color UnderlineColor = Color.FromArgb(255, 80, 80);
        public Color ClearColor = Color.Empty;   // cor de fundo atras do botao (quando o pai nao pinta o fundo)
        bool hover;

        public IconButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            TabStop = false;
            Cursor = Cursors.Hand;
        }

        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hover = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hover = false; Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(!ClearColor.IsEmpty ? ClearColor : (Parent != null ? Parent.BackColor : Color.Black));
            if (Filled)
            {
                Color c = hover ? ControlPaint.Light(BackColor, 0.25f) : BackColor;
                using (SolidBrush b = new SolidBrush(c))
                {
                    if (Radius > 0)
                    {
                        using (GraphicsPath gp = Gfx.RoundRect(new Rectangle(0, 0, Width - 1, Height - 1), Radius))
                            g.FillPath(b, gp);
                    }
                    else g.FillEllipse(b, 1, 1, Width - 3, Height - 3);
                }
            }
            if (Outline)
            {
                using (Pen pn = new Pen(OutlineColor, 1f))
                using (GraphicsPath gp2 = Gfx.RoundRect(new Rectangle(0, 0, Width - 1, Height - 1), Radius > 0 ? Radius : 8))
                    g.DrawPath(pn, gp2);
            }
            Color fc = ForeColor;
            if (!Filled && hover) fc = ControlPaint.Light(ForeColor, 0.6f);
            if (Glyph != null) DrawGlyph(g, fc);
            else if (Star) DrawStarText(g, fc);
            else
            {
                TextRenderer.DrawText(g, Text, Font, ClientRectangle, fc,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                    TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
            }
            if (Underline)
            {
                using (SolidBrush b = new SolidBrush(UnderlineColor))
                    g.FillRectangle(b, 8, Height - 3, Width - 16, 3);
            }
        }

        void DrawStarText(Graphics g, Color fc)
        {
            Size ts = TextRenderer.MeasureText(g, Text, Font, new Size(1000, 100), TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
            float total = 14f + 7f + ts.Width;
            float x0 = (Width - total) / 2f;
            float cx = x0 + 7f, cy = Height / 2f, r = 7f, k = r * 0.30f;
            using (SolidBrush b = new SolidBrush(fc))
            {
                g.FillPolygon(b, new PointF[] {
                    new PointF(cx, cy - r), new PointF(cx + k, cy - k), new PointF(cx + r, cy), new PointF(cx + k, cy + k),
                    new PointF(cx, cy + r), new PointF(cx - k, cy + k), new PointF(cx - r, cy), new PointF(cx - k, cy - k) });
            }
            Rectangle tr = new Rectangle((int)(x0 + 21f), 0, ts.Width + 4, Height);
            TextRenderer.DrawText(g, Text, Font, tr, fc,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
        }

        void DrawGlyph(Graphics g, Color fc)
        {
            float s = Math.Min(Width, Height);
            float cx = Width / 2f, cy = Height / 2f;
            using (SolidBrush b = new SolidBrush(fc))
            {
                if (Glyph == "play")
                {
                    // triangulo centrado pelo centroide (nao pela caixa), para parecer no meio do circulo
                    float th = s * 0.40f, tw = th * 0.9f;
                    g.FillPolygon(b, new PointF[] {
                        new PointF(cx - tw / 3f, cy - th / 2f),
                        new PointF(cx - tw / 3f, cy + th / 2f),
                        new PointF(cx + 2f * tw / 3f, cy) });
                }
                else if (Glyph == "pause")
                {
                    float bw = s * 0.10f, bh = s * 0.36f, gap = s * 0.10f;
                    g.FillRectangle(b, cx - gap / 2f - bw, cy - bh / 2f, bw, bh);
                    g.FillRectangle(b, cx + gap / 2f, cy - bh / 2f, bw, bh);
                }
                else if (Glyph == "mini")
                {
                    float k = Math.Min(Width, Height) / 40f * 1.05f;
                    float ox = (Width - 40f * k) / 2f, oy = (Height - 40f * k) / 2f;
                    using (GraphicsPath gp = Gfx.RoundRect(new Rectangle((int)(ox + 9.5f * k), (int)(oy + 11.5f * k), (int)(21f * k), (int)(16f * k)), Math.Max(2, (int)(3.5f * k))))
                    using (Pen pn = new Pen(fc, Math.Max(1.5f, 2.4f * k)))
                        g.DrawPath(pn, gp);
                    using (GraphicsPath gp2 = Gfx.RoundRect(new Rectangle((int)(ox + 18f * k), (int)(oy + 19f * k), (int)(13f * k), (int)(9.5f * k)), Math.Max(1, (int)(2.5f * k))))
                        g.FillPath(b, gp2);
                }
                else if (Glyph == "heart" || Glyph == "hearton")
                {
                    Gfx.Heart(g, cx, cy + 0.5f, s * 0.50f, fc, Glyph == "hearton");
                }
                else if (Glyph == "plus")
                {
                    // cruz desenhada (centrada de verdade, sem depender da fonte)
                    float L = (float)Math.Round(s * 0.17f);
                    float px = (float)Math.Round(cx), py = (float)Math.Round(cy);
                    using (Pen pn = new Pen(fc, 2f))
                    {
                        pn.StartCap = LineCap.Flat;
                        pn.EndCap = LineCap.Flat;
                        g.DrawLine(pn, px - L, py, px + L, py);
                        g.DrawLine(pn, px, py - L, px, py + L);
                    }
                }
                else if (Glyph == "back")
                {
                    float L = s * 0.22f;
                    using (Pen pn = new Pen(fc, 2.4f))
                    {
                        pn.StartCap = LineCap.Round;
                        pn.EndCap = LineCap.Round;
                        g.DrawLine(pn, cx - L, cy, cx + L, cy);
                        g.DrawLine(pn, cx - L, cy, cx - L + L * 0.85f, cy - L * 0.85f);
                        g.DrawLine(pn, cx - L, cy, cx - L + L * 0.85f, cy + L * 0.85f);
                    }
                }
                else if (Glyph == "next" || Glyph == "prev")
                {
                    float th = s * 0.36f, tw = th * 0.9f, bw = s * 0.07f, gap = s * 0.03f;
                    float total = tw + gap + bw;
                    float x0 = cx - total / 2f;
                    if (Glyph == "next")
                    {
                        g.FillPolygon(b, new PointF[] {
                            new PointF(x0, cy - th / 2f),
                            new PointF(x0, cy + th / 2f),
                            new PointF(x0 + tw, cy) });
                        g.FillRectangle(b, x0 + tw + gap, cy - th / 2f, bw, th);
                    }
                    else
                    {
                        g.FillRectangle(b, x0, cy - th / 2f, bw, th);
                        g.FillPolygon(b, new PointF[] {
                            new PointF(x0 + total, cy - th / 2f),
                            new PointF(x0 + total, cy + th / 2f),
                            new PointF(x0 + bw + gap, cy) });
                    }
                }
            }
        }
    }

    // Barra deslizante escura (progresso e volume)
    class DarkSlider : Control
    {
        int min = 0, max = 100, val = 0;
        bool dragging, hover;
        public Color Fill = Color.FromArgb(255, 80, 80);
        public Color TrackColor = Color.FromArgb(70, 70, 70);
        public event EventHandler Changing;   // enquanto arrasta
        public event EventHandler Committed;  // ao soltar

        public DarkSlider()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Height = 20;
            Cursor = Cursors.Hand;
        }

        public int Minimum { get { return min; } set { min = value; if (val < min) val = min; Invalidate(); } }
        public int Maximum { get { return max; } set { max = value; if (val > max) val = max; Invalidate(); } }
        public int Value
        {
            get { return val; }
            set
            {
                int v = Math.Max(min, Math.Min(max, value));
                if (v != val) { val = v; Invalidate(); }
            }
        }
        public bool Dragging { get { return dragging; } }

        int ValueFromX(int x)
        {
            int pad = 8;
            int w = Width - 2 * pad;
            if (w <= 0) return min;
            double f = (double)(x - pad) / w;
            f = Math.Max(0, Math.Min(1, f));
            return min + (int)Math.Round(f * (max - min));
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            dragging = true;
            val = ValueFromX(e.X);
            Invalidate();
            if (Changing != null) Changing(this, EventArgs.Empty);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!dragging) return;
            val = ValueFromX(e.X);
            Invalidate();
            if (Changing != null) Changing(this, EventArgs.Empty);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (!dragging) return;
            dragging = false;
            val = ValueFromX(e.X);
            Invalidate();
            if (Committed != null) Committed(this, EventArgs.Empty);
        }

        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hover = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hover = false; Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(BackColor);
            int pad = 8;
            int w = Width - 2 * pad;
            if (w <= 0) return;
            int cy = Height / 2;
            int fw = (max == min) ? 0 : (int)((double)(val - min) / (max - min) * w);
            using (SolidBrush b = new SolidBrush(TrackColor)) g.FillRectangle(b, pad, cy - 2, w, 4);
            using (SolidBrush b = new SolidBrush(hover || dragging ? Fill : Color.FromArgb(210, 210, 210)))
                g.FillRectangle(b, pad, cy - 2, fw, 4);
            if (hover || dragging)
            {
                using (SolidBrush b = new SolidBrush(Color.White))
                    g.FillEllipse(b, pad + fw - 6, cy - 6, 12, 12);
            }
        }
    }

    static class Prompt
    {
        // 0 = cancelar, 1 = primeiro botao, 2 = segundo botao
        public static int Choose(IWin32Window owner, string title, string msg, string b1, string b2)
        {
            Form f = new Form();
            f.Text = title;
            f.FormBorderStyle = FormBorderStyle.FixedDialog;
            f.StartPosition = FormStartPosition.CenterParent;
            f.ClientSize = new Size(420, 150);
            f.MaximizeBox = false;
            f.MinimizeBox = false;
            f.ShowInTaskbar = false;
            f.BackColor = Color.FromArgb(30, 30, 30);
            f.ForeColor = Color.White;
            f.Font = new Font("Segoe UI", 9.5f);
            f.HandleCreated += delegate { Native.DarkTitle(f.Handle); };

            Label l = new Label();
            l.Text = msg;
            l.Location = new Point(16, 14);
            l.Size = new Size(388, 70);

            int res = 0;
            string[] names = new string[] { "Cancelar", b1, b2 };
            for (int i = 0; i < 3; i++)
            {
                int code = i;
                Button b = new Button();
                b.Text = names[i];
                b.Size = new Size(i == 0 ? 90 : 128, 30);
                b.Location = new Point(i == 0 ? 16 : (i == 1 ? 150 : 284), 104);
                b.FlatStyle = FlatStyle.Flat;
                b.FlatAppearance.BorderSize = 0;
                b.BackColor = Color.FromArgb(55, 55, 55);
                b.ForeColor = Color.White;
                b.Click += delegate { res = code; f.Close(); };
                f.Controls.Add(b);
                if (i == 2) f.AcceptButton = b;
                if (i == 0) f.CancelButton = b;
            }
            f.Controls.Add(l);
            f.ShowDialog(owner);
            f.Dispose();
            return res;
        }

        public static string Ask(IWin32Window owner, string title, string label, string def)
        {
            Form f = new Form();
            f.Text = title;
            f.FormBorderStyle = FormBorderStyle.FixedDialog;
            f.StartPosition = FormStartPosition.CenterParent;
            f.ClientSize = new Size(360, 128);
            f.MaximizeBox = false;
            f.MinimizeBox = false;
            f.ShowInTaskbar = false;
            f.BackColor = Color.FromArgb(30, 30, 30);
            f.ForeColor = Color.White;
            f.Font = new Font("Segoe UI", 9.5f);
            f.HandleCreated += delegate { Native.DarkTitle(f.Handle); };

            Label l = new Label();
            l.Text = label;
            l.Location = new Point(16, 14);
            l.AutoSize = true;

            TextBox t = new TextBox();
            t.Text = def;
            t.Location = new Point(16, 40);
            t.Width = 328;
            t.BackColor = Color.FromArgb(45, 45, 45);
            t.ForeColor = Color.White;
            t.BorderStyle = BorderStyle.FixedSingle;

            Button ok = new Button();
            ok.Text = "OK";
            ok.DialogResult = DialogResult.OK;
            ok.Location = new Point(172, 84);
            ok.Size = new Size(84, 30);
            ok.FlatStyle = FlatStyle.Flat;
            ok.FlatAppearance.BorderSize = 0;
            ok.BackColor = Color.FromArgb(255, 80, 80);

            Button cancel = new Button();
            cancel.Text = "Cancelar";
            cancel.DialogResult = DialogResult.Cancel;
            cancel.Location = new Point(262, 84);
            cancel.Size = new Size(84, 30);
            cancel.FlatStyle = FlatStyle.Flat;
            cancel.FlatAppearance.BorderSize = 0;
            cancel.BackColor = Color.FromArgb(55, 55, 55);

            f.Controls.Add(l);
            f.Controls.Add(t);
            f.Controls.Add(ok);
            f.Controls.Add(cancel);
            f.AcceptButton = ok;
            f.CancelButton = cancel;
            f.Shown += delegate { t.Focus(); t.SelectAll(); };

            string r = null;
            if (f.ShowDialog(owner) == DialogResult.OK) r = t.Text.Trim();
            f.Dispose();
            return (r != null && r.Length > 0) ? r : null;
        }
    }

    static class Program
    {
        // beat://musica/ID  ->  ID (ou null se o argumento nao for um link valido)
        public static string ParseLink(string[] args)
        {
            if (args == null) return null;
            foreach (string a in args)
            {
                Match m = Regex.Match(a.Trim(), @"^beat:(?://)?musica/([A-Za-z0-9_\-]{11})/?$", RegexOptions.IgnoreCase);
                if (m.Success) return m.Groups[1].Value;
            }
            return null;
        }

        [STAThread]
        static void Main(string[] args)
        {
            string link = ParseLink(args);
            bool created;
            Mutex single = new Mutex(true, "Local\\BeatPlayerSingleInstance", out created);
            if (!created)
            {
                // ja existe um Beat aberto: deixa o pedido num arquivo e avisa a janela dele
                string msg = link != null ? link : "show";
                try
                {
                    string d = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Beat");
                    Directory.CreateDirectory(d);
                    File.WriteAllText(Path.Combine(d, "link.txt"), msg);
                    Native.PostMessage((IntPtr)0xFFFF, Native.WM_BEATLINK, IntPtr.Zero, IntPtr.Zero);
                }
                catch (Exception) { }
                return;
            }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm(link));
            GC.KeepAlive(single);
        }
    }

    // avisa quando o usuario clica em qualquer lugar da janela (para fechar o historico de busca)
    class OutsideClickFilter : IMessageFilter
    {
        public Action<IntPtr> Handler;

        public bool PreFilterMessage(ref Message m)
        {
            // clique esquerdo/direito (area util e barra de titulo)
            if (m.Msg == 0x0201 || m.Msg == 0x0204 || m.Msg == 0x00A1 || m.Msg == 0x00A4)
            {
                if (Handler != null) Handler(m.HWnd);
            }
            return false;
        }
    }

    class MainForm : Form
    {
        static readonly Color BgMain = Color.FromArgb(14, 14, 14);
        static readonly Color BgPanel = Color.FromArgb(24, 24, 24);
        static readonly Color BgSel = Color.FromArgb(44, 44, 44);
        static readonly Color Accent = Color.FromArgb(255, 80, 80);
        static readonly Color Dim = Color.FromArgb(145, 145, 145);
        static readonly Color Line = Color.FromArgb(34, 34, 34);

        // controles
        TextBox txtSearch;
        Button btnSearch, btnPlNew;
        IconButton tabSearch, tabFav, tabPl, tabHome;
        // tela cheia do player e tela inicial
        Panel topPanel, infoPanel, nowPanel, bottomPanel;
        ScrollPanel homePanel;
        IconButton bLink, bBack, bShuffle, bPrev, bPlay, bNext, bRepeat, bHeart2, bRelated2;
        ThumbBox picBig;
        Label lblBigTitle, lblBigSub, lblPos2, lblDur2, lblVolTxt;
        DarkSlider trkSeek2, trkVol2;
        System.Windows.Forms.Timer nowTimer;
        bool nowShown = false;
        bool homeBuilding = false;
        string bigShownId = null;
        Track lastPlayed = null;
        Dictionary<string, Bitmap> bigThumbs = new Dictionary<string, Bitmap>();
        HashSet<string> bigReq = new HashSet<string>();
        Font fHomeBig = new Font("Segoe UI", 20f, FontStyle.Bold);
        Font fHomeSec = new Font("Segoe UI", 12f, FontStyle.Bold);
        Font fHomeChip = new Font("Segoe UI", 10f);
        Font fBigTitle = new Font("Segoe UI", 15f, FontStyle.Bold);
        IconButton btnShuffle, btnPrev, btnPlay, btnNext, btnRepeat, btnHeart;
        DBListBox lst;
        Panel plPanel, pnlPlSearch;
        TextBox txtPl;
        IconButton btnPlBack, btnPlCover;
        ScrollPanel plListPanel;
        int plOpen = -1;   // -1 = lista de playlists; >=0 = dentro de uma playlist
        bool plBuilding = false;
        Dictionary<string, Bitmap> coverFiles = new Dictionary<string, Bitmap>();
        ThumbBox picNow;
        IconButton btnRelated, btnLink;
        ToolTip tt = new ToolTip();
        bool related = false;
        bool fetchingRelated = false;
        bool waitingRelated = false;
        int relToken = 0;
        List<string> history = new List<string>();
        Dictionary<string, string> histCover = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string resultsQuery;
        const string ClearTag = "\u0001limpar";
        static readonly Font fHistClear = new Font("Segoe UI", 9f);
        static readonly Font fHistX = new Font("Segoe UI Symbol", 9f);
        ListBox histBox;
        bool histSuppress = false;
        List<Track> queueSource = null;
        Dictionary<string, Bitmap> thumbs = new Dictionary<string, Bitmap>();
        HashSet<string> thumbReq = new HashSet<string>();
        bool updatingCombo = false;
        Label lblInfo, lblNow, lblSub, lblPos, lblDur;
        DarkSlider trkSeek, trkVol;
        System.Windows.Forms.Timer timer;
        ContextMenuStrip ctx, trayMenu;
        NotifyIcon tray;
        Font fontBold, fontSmall, fontSym;
        Icon appIcon;

        // motor de audio (Windows Media Player sem janela)
        dynamic wmp;
        bool started = false;
        bool loading = false;
        DateTime openedAt = DateTime.Now;
        ManualResetEvent ytReady = new ManualResetEvent(false);

        // dados
        string appDir, ytdlp, cacheDir, dataFile;
        List<Track> results = new List<Track>();
        List<Track> favorites = new List<Track>();
        List<Playlist> playlists = new List<Playlist>();
        List<Track> viewList;
        List<Track> queue = new List<Track>();
        int view = 0;
        int qIndex = -1;
        Track nowTrack = null;
        string nowId = null;
        int token = 0;
        int searchToken = 0;
        int ctxIndex = -1;
        bool isPlaying = false;
        bool triedFallback = false;
        bool shuffle = false;
        int repeat = 0; // 0 nao, 1 lista, 2 faixa
        bool reallyExit = false;
        const string AppVersion = "1.9";
        Panel queuePanel, linkCard;
        DBListBox qList;
        Label lblQCount;
        IconButton btnQueue, btnMiniBar, bMini2;
        bool queueOpen, linkShown, plSuppress;
        int qDownY;
        Form miniForm;
        SlideView slide;
        System.Windows.Forms.Timer slideTimer;
        Stopwatch slideSw;
        List<Track> linkTracks;
        string linkTitle;
        RoundPanel settingsPanel, searchHost, addPop;
        LyricsView lyricsView;
        IconButton btnLetra, bLetra2, btnAdd, bAdd2;
        bool lyricsAvail, lyricsOpen;
        const double LyricsLag = 0.45;   // a letra (LRC) costuma vir adiantada em relacao ao audio
        int lyricsToken;
        DateTime lyricsHoldUntil = DateTime.MinValue;
        IconButton btnGear;
        ToggleSwitch tgClear, tgTray, tgAnim;
        bool optClearHist = false, optTray = true, optAnim = true, coversCleaned = false;
        const string ShareBase = "https://mikhail-yanukovych.github.io/Beat/?m=";
        const string UpdateApi = "https://api.github.com/repos/Mikhail-Yanukovych/Beat/releases/latest";
        bool updating = false;
        bool tipShown = false;
        string lastQuery = "";
        int savedVolume = 70;
        Random rnd = new Random();

        string startLink;
        OutsideClickFilter clickFilter;

        public MainForm(string link)
        {
            startLink = link;
            Text = "Beat";
            ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072; // TLS 1.2
            ServicePointManager.DefaultConnectionLimit = 12;
            ClientSize = new Size(1320, 700);
            MinimumSize = new Size(1300, 520);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = BgMain;
            ForeColor = Color.White;
            Font = new Font("Segoe UI", 9.5f);
            KeyPreview = true;
            fontBold = new Font("Segoe UI", 10.5f, FontStyle.Bold);
            fontSmall = new Font("Segoe UI", 9f);
            fontSym = new Font("Segoe UI Symbol", 13f);
            try { appIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch (Exception) { appIcon = SystemIcons.Application; }
            Icon = appIcon;

            appDir = AppDomain.CurrentDomain.BaseDirectory;
            ytdlp = Path.Combine(appDir, "yt-dlp.exe");
            cacheDir = Path.Combine(Path.GetTempPath(), "Beat");
            dataFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), Path.Combine("Beat", "dados.txt"));
            viewList = results;

            BuildUi();
            BuildMenus();
            BuildPlayer();
            Load();
            ApplyLoaded();
            SetView(3);
            EnsureYtdlp();
            CheckUpdate();
            if (startLink != null) Shown += delegate { OpenSharedLink(startLink); };

            clickFilter = new OutsideClickFilter();
            clickFilter.Handler = delegate(IntPtr h)
            {
                Control c = Control.FromHandle(h);
                if (settingsPanel != null && settingsPanel.Visible)
                {
                    bool inside = false;
                    for (Control q = c; q != null; q = q.Parent) if (q == settingsPanel || q == btnGear) { inside = true; break; }
                    if (!inside) HideSettings();
                }
                if (addPop != null && addPop.Visible)
                {
                    bool in2 = false;
                    for (Control q = c; q != null; q = q.Parent) if (q == addPop || q == btnAdd || q == bAdd2) { in2 = true; break; }
                    if (!in2) HideAddPop();
                }
                if (histBox == null || !histBox.Visible) return;
                if (c != txtSearch && c != histBox) HideHist();
            };
            Application.AddMessageFilter(clickFilter);
            Disposed += delegate { Application.RemoveMessageFilter(clickFilter); };
            Deactivate += delegate { HideHist(); };
            BuildSettings();
        }

        // ================= interface =================

        IconButton MakeIcon(string text, Size size, Color fore)
        {
            IconButton b = new IconButton();
            b.Text = text;
            b.Font = fontSym;
            b.Size = size;
            b.ForeColor = fore;
            b.BackColor = BgPanel;
            return b;
        }

        void BuildUi()
        {
            // ---------- topo ----------
            Panel top = new Panel();
            top.Dock = DockStyle.Top;
            top.Height = 64;
            top.Padding = new Padding(16, 14, 16, 14);
            top.BackColor = BgPanel;

            txtSearch = new TextBox();
            txtSearch.Dock = DockStyle.Fill;
            txtSearch.Font = new Font("Segoe UI", 12f);
            txtSearch.BackColor = Color.FromArgb(40, 40, 40);
            txtSearch.ForeColor = Color.White;
            txtSearch.BorderStyle = BorderStyle.None;
            txtSearch.KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; DoSearch(); }
                else if (e.KeyCode == Keys.Escape) { HideHist(); }
                else if (e.KeyCode == Keys.Down)
                {
                    e.SuppressKeyPress = true;
                    if (!histBox.Visible) ShowHist();
                    if (histBox.Visible && histBox.Items.Count > 0) { histBox.SelectedIndex = 0; histBox.Focus(); }
                }
            };
            txtSearch.MouseWheel += delegate(object s, MouseEventArgs e) { ScrollHome(e.Delta); };
            txtSearch.Click += delegate { ShowHist(); };
            txtSearch.TextChanged += delegate { if (!histSuppress && txtSearch.Focused) ShowHist(); };
            txtSearch.LostFocus += delegate
            {
                BeginInvoke((MethodInvoker)delegate { if (!histBox.Focused) HideHist(); });
            };

            RoundPanel host = new RoundPanel();
            host.Dock = DockStyle.Fill;
            host.Radius = 17;
            host.BorderColor = Color.FromArgb(70, 70, 70);
            host.Padding = new Padding(18, 7, 14, 0);
            searchHost = host;
            txtSearch.GotFocus += delegate { searchHost.BorderColor = Accent; searchHost.Invalidate(); };
            txtSearch.LostFocus += delegate { searchHost.BorderColor = Color.FromArgb(70, 70, 70); searchHost.Invalidate(); };
            host.Controls.Add(txtSearch);
            host.Click += delegate { txtSearch.Focus(); };

            Panel spacer = new Panel();
            spacer.Dock = DockStyle.Right;
            spacer.Width = 8;

            IconButton sb = new IconButton();
            sb.Text = "Buscar";
            sb.Font = new Font("Segoe UI", 10.5f, FontStyle.Bold);
            sb.Dock = DockStyle.Right;
            sb.Width = 104;
            sb.Filled = true;
            sb.Radius = 17;
            sb.BackColor = Accent;
            sb.ForeColor = Color.White;
            sb.Click += delegate { DoSearch(); };
            btnSearch = sb;

            Panel tabs = new Panel();
            tabs.Dock = DockStyle.Left;
            tabs.Width = 444;
            tabs.BackColor = BgPanel;
            tabHome = MakeTab("Início", 3, 0);
            tabSearch = MakeTab("Busca", 0, 108);
            tabFav = MakeTab("Favoritos", 1, 216);
            tabPl = MakeTab("Playlists", 2, 324);
            tabs.Controls.Add(tabHome);
            tabs.Controls.Add(tabSearch);
            tabs.Controls.Add(tabFav);
            tabs.Controls.Add(tabPl);

            topPanel = top;
            top.Controls.Add(host);
            top.Controls.Add(spacer);
            top.Controls.Add(btnSearch);
            top.Controls.Add(tabs);
            Panel gsp = new Panel();
            gsp.Dock = DockStyle.Right;
            gsp.Width = 8;
            top.Controls.Add(gsp);
            btnGear = MakeIcon("\u2699", new Size(40, 36), Dim);
            btnGear.Font = new Font("Segoe UI Symbol", 15f);
            btnGear.Dock = DockStyle.Right;
            btnGear.Width = 40;
            btnGear.Click += delegate { ToggleSettings(); };
            top.Controls.Add(btnGear);
            host.BringToFront();

            // ---------- barra de informacao (e controles de playlist) ----------
            Panel infoBar = new Panel();
            infoBar.Dock = DockStyle.Top;
            infoBar.Height = 42;
            infoBar.BackColor = BgMain;
            infoBar.Padding = new Padding(0, 8, 16, 4);

            lblInfo = new Label();
            lblInfo.Dock = DockStyle.Fill;
            lblInfo.TextAlign = ContentAlignment.MiddleLeft;
            lblInfo.Padding = new Padding(16, 0, 0, 0);
            lblInfo.ForeColor = Dim;
            lblInfo.AutoEllipsis = true;

            plPanel = new Panel();
            plPanel.Dock = DockStyle.Right;
            plPanel.Width = 400;
            plPanel.Visible = false;

            txtPl = new TextBox();
            txtPl.Dock = DockStyle.Fill;
            txtPl.Font = new Font("Segoe UI", 10.5f);
            txtPl.BackColor = Color.FromArgb(40, 40, 40);
            txtPl.ForeColor = Color.White;
            txtPl.BorderStyle = BorderStyle.None;
            txtPl.HandleCreated += delegate { Native.SendMessage(txtPl.Handle, 0x1501, (IntPtr)1, "Buscar playlist"); };
            txtPl.TextChanged += delegate { if (plSuppress) return; if (view == 1) RefreshList(); else RefreshPlList(); };
            txtPl.MouseWheel += delegate(object s, MouseEventArgs e) { ScrollPl(e.Delta); };

            pnlPlSearch = new Panel();
            pnlPlSearch.Dock = DockStyle.Fill;
            pnlPlSearch.BackColor = Color.FromArgb(40, 40, 40);
            pnlPlSearch.Padding = new Padding(12, 6, 12, 0);
            pnlPlSearch.Controls.Add(txtPl);

            Panel sp1 = new Panel(); sp1.Dock = DockStyle.Right; sp1.Width = 8;
            btnPlNew = MakeSmall("Nova");
            btnPlNew.Click += delegate { NewPlaylist(null); };

            btnPlCover = MakeSmall("Capa");
            btnPlCover.Click += delegate
            {
                Playlist cp = CurrentPlaylist();
                if (cp != null) ShowCoverMenu(cp, btnPlCover, new Point(0, btnPlCover.Height));
            };

            btnPlBack = MakeSmall("\u2190 Playlists");
            btnPlBack.Dock = DockStyle.Left;
            btnPlBack.Width = 110;
            btnPlBack.Click += delegate { plOpen = -1; SetView(2); };

            plPanel.Controls.Add(pnlPlSearch);
            plPanel.Controls.Add(sp1);
            plPanel.Controls.Add(btnPlNew);
            plPanel.Controls.Add(btnPlCover);

            infoPanel = infoBar;
            infoBar.Controls.Add(lblInfo);
            infoBar.Controls.Add(btnPlBack);
            infoBar.Controls.Add(plPanel);
            Panel spQ = new Panel(); spQ.Dock = DockStyle.Right; spQ.Width = 8;
            btnQueue = MakeSmall("Fila");
            btnQueue.Click += delegate { ToggleQueue(); };
            infoBar.Controls.Add(spQ);
            infoBar.Controls.Add(btnQueue);

            // ---------- lista ----------
            lst = new DBListBox();
            lst.Dock = DockStyle.Fill;
            lst.BackColor = BgMain;
            lst.ForeColor = Color.White;
            lst.BorderStyle = BorderStyle.None;
            lst.DrawMode = DrawMode.OwnerDrawFixed;
            lst.ItemHeight = 68;
            lst.IntegralHeight = false;
            lst.DrawItem += lst_DrawItem;
            lst.HandleCreated += delegate { Native.DarkScroll(lst.Handle); };
            lst.DoubleClick += delegate { if (lst.SelectedIndex >= 0) PlayFromView(lst.SelectedIndex); };
            lst.KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter && lst.SelectedIndex >= 0)
                {
                    e.SuppressKeyPress = true;
                    PlayFromView(lst.SelectedIndex);
                }
            };
            lst.MouseDown += lst_MouseDown;
            lst.Reordered += MovePlaylistItem;

            // ---------- rodape: player ----------
            Panel bottom = new Panel();
            bottom.Dock = DockStyle.Bottom;
            bottom.Height = 100;
            bottom.BackColor = BgPanel;

            Panel sep = new Panel();
            sep.Dock = DockStyle.Top;
            sep.Height = 1;
            sep.BackColor = Line;

            TableLayoutPanel tl = new TableLayoutPanel();
            tl.Dock = DockStyle.Fill;
            tl.ColumnCount = 3;
            tl.RowCount = 1;
            tl.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 500));
            tl.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 500));
            tl.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 500));
            tl.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            // lados iguais: o bloco do meio (play/pause + progresso) fica centralizado na janela
            tl.SizeChanged += delegate
            {
                int W = tl.ClientSize.Width;
                if (W < 100) return;
                int cw = Math.Max(300, Math.Min(760, W - 2 * 500));
                int side = Math.Max(500, (W - cw) / 2);
                if ((int)tl.ColumnStyles[0].Width != side || (int)tl.ColumnStyles[1].Width != cw)
                {
                    tl.ColumnStyles[0].Width = side;
                    tl.ColumnStyles[1].Width = cw;
                    tl.ColumnStyles[2].Width = side;
                }
            };

            // esquerda: titulo + canal/status
            Panel left = new Panel();
            left.Dock = DockStyle.Fill;
            left.Padding = new Padding(18, 12, 8, 0);

            lblNow = new Label();
            lblNow.Dock = DockStyle.Top;
            lblNow.Height = 26;
            lblNow.Font = fontBold;
            lblNow.AutoEllipsis = true;
            lblNow.Text = "Nada tocando";

            lblSub = new Label();
            lblSub.Dock = DockStyle.Top;
            lblSub.Height = 22;
            lblSub.ForeColor = Dim;
            lblSub.AutoEllipsis = true;
            lblSub.Text = "";

            picNow = new ThumbBox();
            picNow.Dock = DockStyle.Left;
            picNow.Width = 102;
            picNow.BackColor = BgPanel;
            picNow.Visible = false;
            picNow.Cursor = Cursors.Hand;
            picNow.MouseClick += delegate(object s, MouseEventArgs e) { if (e.X < 88 && e.Y < 50) ShowNow(); };

            btnLetra = MakeLetra();
            btnLetra.Location = new Point(0, 2);
            btnLetra.Click += delegate { ToggleLyrics(); };
            Panel letraRow = new Panel();
            letraRow.Dock = DockStyle.Top;
            letraRow.Height = 32;
            letraRow.BackColor = BgPanel;
            letraRow.Controls.Add(btnLetra);

            left.Controls.Add(letraRow);
            left.Controls.Add(lblSub);
            left.Controls.Add(lblNow);
            left.Controls.Add(picNow);

            // centro: controles + progresso
            TableLayoutPanel center = new TableLayoutPanel();
            center.Dock = DockStyle.Fill;
            center.ColumnCount = 1;
            center.RowCount = 2;
            center.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
            center.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            center.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            FlowLayoutPanel flow = new FlowLayoutPanel();
            flow.AutoSize = true;
            flow.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            flow.WrapContents = false;
            flow.Anchor = AnchorStyles.None;
            flow.BackColor = BgPanel;

            btnShuffle = MakeIcon("⇄", new Size(44, 44), Dim);
            btnShuffle.Margin = new Padding(4, 8, 4, 0);
            btnShuffle.Click += delegate { shuffle = !shuffle; UpdateModeButtons(); Save(); };

            btnPrev = MakeIcon("", new Size(44, 44), Color.White);
            btnPrev.Glyph = "prev";
            btnPrev.Margin = new Padding(4, 8, 4, 0);
            btnPrev.Click += delegate { Prev(); };

            btnPlay = MakeIcon("", new Size(50, 50), Color.White);
            btnPlay.Filled = true;
            btnPlay.BackColor = Accent;
            btnPlay.Glyph = "play";
            btnPlay.Margin = new Padding(6, 4, 6, 0);
            btnPlay.Click += delegate { TogglePlay(); };

            btnNext = MakeIcon("", new Size(44, 44), Color.White);
            btnNext.Glyph = "next";
            btnNext.Margin = new Padding(4, 8, 4, 0);
            btnNext.Click += delegate { Next(false); };

            btnRepeat = MakeIcon("↻", new Size(44, 44), Dim);
            btnRepeat.Margin = new Padding(4, 8, 4, 0);
            btnRepeat.Click += delegate { repeat = (repeat + 1) % 3; UpdateModeButtons(); Save(); };

            flow.Controls.Add(btnShuffle);
            flow.Controls.Add(btnPrev);
            flow.Controls.Add(btnPlay);
            flow.Controls.Add(btnNext);
            flow.Controls.Add(btnRepeat);

            TableLayoutPanel seekRow = new TableLayoutPanel();
            seekRow.Dock = DockStyle.Fill;
            seekRow.ColumnCount = 3;
            seekRow.RowCount = 1;
            seekRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 52));
            seekRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            seekRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 52));
            seekRow.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            lblPos = new Label();
            lblPos.Dock = DockStyle.Fill;
            lblPos.TextAlign = ContentAlignment.TopRight;
            lblPos.Padding = new Padding(0, 4, 0, 0);
            lblPos.ForeColor = Dim;
            lblPos.Text = "0:00";

            lblDur = new Label();
            lblDur.Dock = DockStyle.Fill;
            lblDur.TextAlign = ContentAlignment.TopLeft;
            lblDur.Padding = new Padding(0, 4, 0, 0);
            lblDur.ForeColor = Dim;
            lblDur.Text = "0:00";

            trkSeek = new DarkSlider();
            trkSeek.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top;
            trkSeek.Margin = new Padding(0, 0, 0, 0);
            trkSeek.BackColor = BgPanel;
            trkSeek.Minimum = 0;
            trkSeek.Maximum = 1;
            trkSeek.Changing += delegate { lblPos.Text = Fmt(trkSeek.Value); };
            trkSeek.Committed += delegate
            {
                try { wmp.controls.currentPosition = (double)trkSeek.Value; } catch (Exception) { }
            };

            seekRow.Controls.Add(lblPos, 0, 0);
            seekRow.Controls.Add(trkSeek, 1, 0);
            seekRow.Controls.Add(lblDur, 2, 0);

            center.Controls.Add(flow, 0, 0);
            center.Controls.Add(seekRow, 0, 1);

            // direita: relacionadas + coracao + volume
            TableLayoutPanel right = new TableLayoutPanel();
            right.Dock = DockStyle.Fill;
            right.ColumnCount = 7;
            right.Padding = new Padding(0, 0, 0, 40);
            right.RowCount = 1;
            right.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            right.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 134));
            right.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 112));
            right.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 46));
            right.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 42));
            right.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 42));
            right.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 116));
            right.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            btnRelated = new IconButton();
            btnRelated.Text = "Relacionadas";
            btnRelated.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
            btnRelated.Size = new Size(124, 30);
            btnRelated.Anchor = AnchorStyles.None;
            btnRelated.BackColor = Color.FromArgb(61, 33, 33);
            btnRelated.ForeColor = Dim;
            btnRelated.Radius = 15;
            btnRelated.Outline = true;
            btnRelated.Star = true;
            btnRelated.Click += delegate { SetRelated(!related, true); };
            tt.SetToolTip(btnRelated, "Tocar musicas relacionadas");

            btnLink = new IconButton();
            btnLink.Text = "Copiar link";
            btnLink.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
            btnLink.Size = new Size(104, 30);
            btnLink.Anchor = AnchorStyles.None;
            btnLink.BackColor = BgPanel;
            btnLink.ForeColor = Dim;
            btnLink.Radius = 15;
            btnLink.Outline = true;
            btnLink.Click += delegate { CopyLink(nowTrack); };

            btnHeart = MakeIcon("", new Size(40, 40), Dim);
            btnHeart.Glyph = "heart";
            btnHeart.Anchor = AnchorStyles.None;
            btnHeart.Click += delegate { if (nowTrack != null) ToggleFav(nowTrack); };

            btnAdd = MakeAddBtn();
            btnAdd.Click += delegate { ShowAddPop(btnAdd, false); };
            tt.SetToolTip(btnAdd, "Adicionar a uma playlist");

            trkVol = new DarkSlider();
            trkVol.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            trkVol.Margin = new Padding(0, 0, 18, 0);
            trkVol.BackColor = BgPanel;
            trkVol.Minimum = 0;
            trkVol.Maximum = 100;
            trkVol.Value = 70;
            trkVol.Changing += delegate { try { wmp.settings.volume = trkVol.Value; } catch (Exception) { } };
            trkVol.Committed += delegate { Save(); };

            right.Controls.Add(btnRelated, 1, 0);
            right.Controls.Add(btnLink, 2, 0);
            right.Controls.Add(btnHeart, 3, 0);
            btnMiniBar = MakeMiniBtn();
            btnMiniBar.Click += delegate { OpenMini(); };
            tt.SetToolTip(btnMiniBar, "Mini player");
            right.Controls.Add(btnAdd, 4, 0);
            right.Controls.Add(btnMiniBar, 5, 0);
            right.Controls.Add(trkVol, 6, 0);

            tl.Controls.Add(left, 0, 0);
            tl.Controls.Add(center, 1, 0);
            tl.Controls.Add(right, 2, 0);

            bottom.Controls.Add(tl);
            bottom.Controls.Add(sep);

            BuildNowPanel();
            BuildHomePanel();
            BuildPlListPanel();
            Controls.Add(lst);
            BuildQueuePanel();
            Controls.Add(queuePanel);
            BuildLinkCard();
            Controls.Add(linkCard);
            bottomPanel = bottom;
            Controls.Add(bottom);
            Controls.Add(infoBar);
            Controls.Add(top);

            // historico de pesquisa (lista que aparece embaixo da barra)
            histBox = new ListBox();
            histBox.DrawMode = DrawMode.OwnerDrawVariable;
            histBox.MeasureItem += histBox_MeasureItem;
            histBox.IntegralHeight = false;
            histBox.BorderStyle = BorderStyle.None;
            histBox.BackColor = Color.FromArgb(34, 34, 34);
            histBox.ForeColor = Color.White;
            histBox.Font = new Font("Segoe UI", 10.5f);
            histBox.Visible = false;
            histBox.DrawItem += histBox_DrawItem;
            histBox.MouseMove += delegate(object s, MouseEventArgs e)
            {
                int hi = histBox.IndexFromPoint(e.Location);
                if (hi >= 0 && hi != histBox.SelectedIndex) histBox.SelectedIndex = hi;
            };
            histBox.MouseUp += delegate(object s, MouseEventArgs e)
            {
                int hi = histBox.IndexFromPoint(e.Location);
                if (hi < 0 || hi >= histBox.Items.Count) return;
                string hit = (string)histBox.Items[hi];
                if (hit != ClearTag && e.Button == MouseButtons.Left && e.X >= histBox.Width - 48) { DeleteHistItem(hit); return; }
                PickHist(hi);
            };
            histBox.KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter && histBox.SelectedIndex >= 0) { e.SuppressKeyPress = true; PickHist(histBox.SelectedIndex); }
                else if (e.KeyCode == Keys.Escape) { e.SuppressKeyPress = true; HideHist(); txtSearch.Focus(); }
                else if (e.KeyCode == Keys.Up && histBox.SelectedIndex <= 0) { e.SuppressKeyPress = true; txtSearch.Focus(); }
                else if (e.KeyCode == Keys.Delete && histBox.SelectedIndex >= 0)
                {
                    e.SuppressKeyPress = true;
                    string hs = (string)histBox.Items[histBox.SelectedIndex];
                    if (hs != ClearTag) DeleteHistItem(hs);
                }
            };
            histBox.LostFocus += delegate
            {
                BeginInvoke((MethodInvoker)delegate { if (!txtSearch.Focused) HideHist(); });
            };
            Controls.Add(histBox);
            Resize += delegate { HideHist(); };

            // atalhos
            KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Escape && nowShown)
                {
                    e.Handled = true;
                    e.SuppressKeyPress = true;
                    HideNow();
                }
                else if (e.KeyCode == Keys.Space && ActiveControl != txtSearch)
                {
                    e.Handled = true;
                    e.SuppressKeyPress = true;
                    TogglePlay();
                }
            };

            Shown += delegate { txtSearch.Focus(); };
        }

        IconButton MakeTab(string text, int v, int x)
        {
            IconButton b = new IconButton();
            b.Text = text;
            b.Font = new Font("Segoe UI", 10.5f, FontStyle.Bold);
            b.Location = new Point(x, 0);
            b.Size = new Size(104, 36);
            b.BackColor = BgPanel;
            b.ForeColor = Dim;
            int vv = v;
            b.Click += delegate { if (vv == 2) plOpen = -1; SetView(vv); };
            return b;
        }

        IconButton MakeSmall(string text)
        {
            IconButton b = new IconButton();
            b.Text = text;
            b.Font = fontSmall;
            b.Dock = DockStyle.Right;
            b.Width = 70;
            b.Filled = true;
            b.Radius = 8;
            b.BackColor = Color.FromArgb(40, 40, 40);
            b.ForeColor = Color.White;
            return b;
        }

        void BuildMenus()
        {
            ctx = new ContextMenuStrip();
            ctx.Renderer = new ToolStripProfessionalRenderer(new DarkColors());
            ctx.ForeColor = Color.White;
            ctx.Opening += ctx_Opening;
            lst.ContextMenuStrip = ctx;

            trayMenu = new ContextMenuStrip();
            trayMenu.Renderer = new ToolStripProfessionalRenderer(new DarkColors());
            trayMenu.ForeColor = Color.White;
            trayMenu.Items.Add(MI("Abrir Beat", delegate { ShowMain(); }));
            trayMenu.Items.Add(MI("Mini player", delegate { OpenMini(); }));
            trayMenu.Items.Add(MI("Tocar / Pausar", delegate { TogglePlay(); }));
            trayMenu.Items.Add(MI("Proxima", delegate { Next(false); }));
            trayMenu.Items.Add(MI("Anterior", delegate { Prev(); }));
            trayMenu.Items.Add(new ToolStripSeparator());
            trayMenu.Items.Add(MI("Sair", delegate { reallyExit = true; Close(); }));

            tray = new NotifyIcon();
            tray.Icon = appIcon;
            tray.Text = "Beat";
            tray.ContextMenuStrip = trayMenu;
            tray.Visible = true;
            tray.MouseClick += delegate(object s, MouseEventArgs e)
            {
                if (e.Button == MouseButtons.Left) ShowMain();
            };
        }

        ToolStripMenuItem MI(string text, EventHandler click)
        {
            ToolStripMenuItem m = new ToolStripMenuItem(text);
            m.ForeColor = Color.White;
            if (click != null) m.Click += click;
            return m;
        }

        void ShowMain()
        {
            Show();
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            Activate();
        }

        // ================= player =================

        void BuildPlayer()
        {
            try
            {
                Type tp = Type.GetTypeFromProgID("WMPlayer.OCX.7");
                wmp = Activator.CreateInstance(tp);
                wmp.settings.autoStart = true;
                wmp.settings.volume = 70;
            }
            catch (Exception ex)
            {
                wmp = null;
                lblSub.Text = "Windows Media Player nao encontrado: " + ex.Message;
            }

            timer = new System.Windows.Forms.Timer();
            timer.Interval = 400;
            timer.Tick += delegate { Tick(); };
            timer.Start();

            FormClosing += delegate(object s, FormClosingEventArgs e)
            {
                if (!reallyExit && optTray && e.CloseReason == CloseReason.UserClosing)
                {
                    e.Cancel = true;
                    Save();
                    Hide();
                    if (!tipShown)
                    {
                        tipShown = true;
                        tray.ShowBalloonTip(2500, "Beat", "O Beat continua na bandeja. Clique com o botao direito no icone para sair.", ToolTipIcon.Info);
                    }
                    return;
                }
                reallyExit = true;
                if (miniForm != null && !miniForm.IsDisposed) miniForm.Close();
                if (optClearHist) { history.Clear(); histCover.Clear(); }
                Save();
                timer.Stop();
                try { for (int i = 1; i <= 4; i++) Native.UnregisterHotKey(Handle, i); } catch (Exception) { }
                try { tray.Visible = false; tray.Dispose(); } catch (Exception) { }
                try { wmp.close(); } catch (Exception) { }
                try { if (Directory.Exists(cacheDir)) Directory.Delete(cacheDir, true); } catch (Exception) { }
            };
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Native.DarkTitle(Handle);
            // teclas de midia do teclado (0x4000 = sem repeticao)
            Native.RegisterHotKey(Handle, 1, 0x4000, 0xB3); // play/pause
            Native.RegisterHotKey(Handle, 2, 0x4000, 0xB0); // proxima
            Native.RegisterHotKey(Handle, 3, 0x4000, 0xB1); // anterior
            Native.RegisterHotKey(Handle, 4, 0x4000, 0xB2); // parar
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x0312)
            {
                int id = m.WParam.ToInt32();
                if (id == 1) TogglePlay();
                else if (id == 2) Next(false);
                else if (id == 3) Prev();
                else if (id == 4) StopPlayback();
                return;
            }
            if (m.Msg == Native.WM_BEATLINK)
            {
                try
                {
                    string f = Path.Combine(Path.GetDirectoryName(dataFile), "link.txt");
                    if (File.Exists(f))
                    {
                        string txt = File.ReadAllText(f).Trim();
                        try { File.Delete(f); } catch (Exception) { }
                        if (txt == "show") ShowMain();
                        else if (Regex.IsMatch(txt, "^[A-Za-z0-9_\\-]{11}$")) OpenSharedLink(txt);
                    }
                }
                catch (Exception) { }
                return;
            }
            base.WndProc(ref m);
        }

        // ================= links compartilhados =================

        void OpenSharedLink(string id)
        {
            if (!Regex.IsMatch(id, "^[A-Za-z0-9_\\-]{11}$")) return;
            ShowMain();
            lblInfo.Text = "Abrindo link...";
            ThreadPool.QueueUserWorkItem(delegate
            {
                ytReady.WaitOne();
                string err = "";
                Track t = null;
                try
                {
                    string args = "--encoding utf-8 --no-warnings --no-playlist --skip-download --print \"%(id)s|||%(title)s|||%(duration_string)s|||%(channel)s\" \"https://www.youtube.com/watch?v=" + id + "\"";
                    List<Track> res = ParseTracks(Run(ytdlp, args, 60000, out err));
                    if (res.Count > 0) t = res[0];
                }
                catch (Exception) { }
                UI(delegate
                {
                    if (t == null) { lblInfo.Text = "Nao foi possivel abrir o link."; return; }
                    StartSingle(t);
                });
            });
        }

        void CopyLink(Track t)
        {
            if (t == null) return;
            try
            {
                Clipboard.SetText(ShareBase + t.Id);
                lblInfo.Text = "Link copiado.";
                if (bLink != null) bLink.Text = "Link copiado!";
                if (btnLink != null) btnLink.Text = "Link copiado!";
                System.Windows.Forms.Timer tm = new System.Windows.Forms.Timer();
                tm.Interval = 1600;
                tm.Tick += delegate
                {
                    tm.Stop(); tm.Dispose();
                    if (bLink != null) bLink.Text = "Copiar link";
                    if (btnLink != null) btnLink.Text = "Copiar link";
                };
                tm.Start();
            }
            catch (Exception) { lblInfo.Text = "Nao foi possivel copiar o link."; }
        }

        void Tick()
        {
            if (queuePanel != null && queuePanel.Visible && !qList.Capture && qList.Items.Count != Math.Max(0, queue.Count - qIndex - 1)) RefreshQueue();
            if (wmp == null || nowTrack == null) return;
            try
            {
                int st = (int)wmp.playState;
                if (loading)
                {
                    if (st == 3)
                    {
                        loading = false;
                        started = true;
                        ReadDuration();
                        SetPlaying(true);
                        lblSub.Text = nowTrack.Channel;
                    }
                    else if ((DateTime.Now - openedAt).TotalSeconds > 15)
                    {
                        loading = false;
                        if (!triedFallback) Fallback();
                        else { lblSub.Text = "Nao foi possivel tocar esta faixa."; SetPlaying(false); }
                    }
                    return;
                }
                if (!started) return;
                if (st == 8 || st == 1) { started = false; Next(true); return; }
                if (st == 3)
                {
                    if (trkSeek.Maximum <= 1) ReadDuration();
                    if (!trkSeek.Dragging)
                    {
                        int pos = (int)(double)wmp.controls.currentPosition;
                        trkSeek.Value = pos;
                        lblPos.Text = Fmt(pos);
                    }
                }
            }
            catch (Exception) { }
        }

        void ReadDuration()
        {
            double d = 0;
            try { d = (double)wmp.currentMedia.duration; } catch (Exception) { }
            if (d > 0)
            {
                int secs = Math.Max(1, (int)d);
                trkSeek.Maximum = secs;
                lblDur.Text = Fmt(secs);
            }
        }

        void SetPlaying(bool playing)
        {
            isPlaying = playing;
            btnPlay.Glyph = playing ? "pause" : "play";
            btnPlay.Invalidate();
        }

        void StopPlayback()
        {
            started = false;
            loading = false;
            try { if (wmp != null) wmp.controls.stop(); } catch (Exception) { }
            SetPlaying(false);
        }

        void PlayFromView(int idx)
        {
            RememberCover(idx);
            PlayList(viewList, idx);
        }

        // a capa do historico e a da musica que a pessoa escolheu naquela busca
        void RememberCover(int idx)
        {
            if (resultsQuery == null || !object.ReferenceEquals(viewList, results)) return;
            if (idx < 0 || idx >= viewList.Count || !history.Contains(resultsQuery)) return;
            if (IsArtistQuery(resultsQuery, viewList[idx]))
            {
                if (histCover.Remove(resultsQuery)) Save();
                return;
            }
            string cur;
            if (histCover.TryGetValue(resultsQuery, out cur) && cur == viewList[idx].Id) return;
            histCover[resultsQuery] = viewList[idx].Id;
            Save();
        }

        // busca so pelo nome do artista (ou parte dele): nao ganha capa
        static bool IsArtistQuery(string q, Track t)
        {
            string ql = q.Trim().ToLower();
            if (ql.Length == 0) return false;
            string ch = (t.Channel ?? "").ToLower();
            if (ch.EndsWith(" - topic")) ch = ch.Substring(0, ch.Length - 8);
            string ti = (t.Title ?? "").ToLower();
            int d = ti.IndexOf(" - ");
            string ap = d > 0 ? ti.Substring(0, d) : "";
            if (ch.Length > 0 && (ch == ql || ch.IndexOf(ql) >= 0)) return true;
            if (ap.Length > 0 && (ap == ql || ap.IndexOf(ql) >= 0)) return true;
            return false;
        }

        void PlayList(List<Track> list, int idx)
        {
            if (idx < 0 || idx >= list.Count) return;
            queue = new List<Track>(list);
            queueSource = list;
            qIndex = idx;
            if (related) TrimQueueForRelated(queue[qIndex]);
            PlayCurrent();
        }

        void PlayCurrent()
        {
            if (qIndex < 0 || qIndex >= queue.Count) return;
            Track t = queue[qIndex];
            nowTrack = t;
            nowId = t.Id;
            lastPlayed = t.Copy();
            picNow.Visible = true;
            picNow.Thumb = GetThumb(t.Id);
            picNow.Invalidate();
            int my = ++token;
            triedFallback = false;
            started = false;
            loading = false;
            lblNow.Text = t.Title;
            lblSub.Text = "Carregando...";
            try { tray.Text = Trunc(t.Title, 60); } catch (Exception) { }
            try { if (wmp != null) wmp.controls.stop(); } catch (Exception) { }
            SetPlaying(false);
            trkSeek.Value = 0;
            trkSeek.Maximum = 1;
            lblPos.Text = "0:00";
            lblDur.Text = (t.Duration != null && t.Duration.Length > 0) ? t.Duration : "0:00";
            UpdateHeart();
            FetchLyrics(t);
            RefreshQueue();
            lst.Invalidate();
            if (related) EnsureRelated();

            ThreadPool.QueueUserWorkItem(delegate
            {
                ytReady.WaitOne();
                string err = "";
                string url = null;
                try
                {
                    string o = Run(ytdlp, "--encoding utf-8 --no-warnings --no-playlist -g -f \"bestaudio[ext=m4a]/best[ext=mp4]/bestaudio\" https://www.youtube.com/watch?v=" + t.Id, 60000, out err);
                    foreach (string line in o.Split('\n'))
                    {
                        string l = line.Trim();
                        if (l.StartsWith("http")) { url = l; break; }
                    }
                }
                catch (Exception ex) { err = ex.Message; }

                UI(delegate
                {
                    if (my != token) return;
                    if (url == null)
                    {
                        lblSub.Text = "Nao consegui obter o audio. " + err.Trim();
                        return;
                    }
                    OpenAndPlay(url);
                });
            });
        }

        void OpenAndPlay(string target)
        {
            if (wmp == null) { lblSub.Text = "Windows Media Player indisponivel."; return; }
            try { wmp.controls.stop(); } catch (Exception) { }
            started = false;
            wmp.URL = target;
            wmp.settings.volume = trkVol.Value;
            openedAt = DateTime.Now;
            loading = true;
            wmp.controls.play();
        }

        // Se o stream direto falhar, baixa o audio para a pasta temporaria e toca de la.
        void Fallback()
        {
            triedFallback = true;
            if (nowTrack == null) return;
            int my = token;
            Track t = nowTrack;
            lblSub.Text = "Baixando o audio...";

            ThreadPool.QueueUserWorkItem(delegate
            {
                string file = null;
                try
                {
                    Directory.CreateDirectory(cacheDir);
                    string err;
                    Run(ytdlp, "--encoding utf-8 --no-warnings --no-playlist -f \"bestaudio[ext=m4a]/best[ext=mp4]\" -o \"" + Path.Combine(cacheDir, t.Id) + ".%(ext)s\" https://www.youtube.com/watch?v=" + t.Id, 180000, out err);
                    foreach (string f in Directory.GetFiles(cacheDir, t.Id + ".*"))
                    {
                        if (f.EndsWith(".part") || f.EndsWith(".ytdl")) continue;
                        file = f;
                        break;
                    }
                }
                catch (Exception) { }

                UI(delegate
                {
                    if (my != token) return;
                    if (file == null) { lblSub.Text = "Nao foi possivel tocar esta faixa."; SetPlaying(false); return; }
                    OpenAndPlay(file);
                });
            });
        }

        void TogglePlay()
        {
            if (wmp == null) return;
            if (nowTrack == null)
            {
                if (lst.SelectedIndex >= 0) PlayList(viewList, lst.SelectedIndex);
                else if (viewList.Count > 0) PlayList(viewList, 0);
                return;
            }
            if (loading) return;
            if (isPlaying) { wmp.controls.pause(); SetPlaying(false); }
            else
            {
                if (!started) { PlayCurrent(); return; }
                wmp.controls.play();
                SetPlaying(true);
            }
        }

        void Next(bool auto)
        {
            if (queue.Count == 0) return;
            if (auto && repeat == 2) { PlayCurrent(); return; }
            int n;
            if (shuffle && queue.Count > 1)
            {
                do { n = rnd.Next(queue.Count); } while (n == qIndex);
            }
            else
            {
                n = qIndex + 1;
                if (n >= queue.Count)
                {
                    if (related) { WaitForRelated(); return; }
                    if (repeat >= 1) n = 0;
                    else { StopPlayback(); lblSub.Text = "Fim da lista."; return; }
                }
            }
            qIndex = n;
            PlayCurrent();
        }

        void Prev()
        {
            if (queue.Count == 0) return;
            try
            {
                if (isPlaying && wmp != null && (double)wmp.controls.currentPosition > 3)
                {
                    wmp.controls.currentPosition = 0.0;
                    return;
                }
            }
            catch (Exception) { }
            int n = qIndex - 1;
            if (n < 0) n = (repeat >= 1) ? queue.Count - 1 : 0;
            qIndex = n;
            PlayCurrent();
        }

        void UpdateModeButtons()
        {
            btnShuffle.ForeColor = shuffle ? Accent : Dim;
            btnRepeat.ForeColor = repeat > 0 ? Accent : Dim;
            btnRepeat.Text = repeat == 2 ? "↻¹" : "↻";
            btnShuffle.Invalidate();
            btnRepeat.Invalidate();
        }

        void UpdateHeart()
        {
            bool fav = nowTrack != null && IsFav(nowTrack.Id);
            btnHeart.Glyph = fav ? "hearton" : "heart";
            btnHeart.ForeColor = fav ? Accent : Dim;
            btnHeart.Invalidate();
        }

        // ================= miniaturas =================

        // devolve a miniatura se ja estiver baixada; senao pede o download e atualiza a tela quando chegar
        Bitmap GetThumb(string id)
        {
            Bitmap b;
            if (thumbs.TryGetValue(id, out b)) return b;
            if (!thumbReq.Contains(id))
            {
                thumbReq.Add(id);
                string idc = id;
                ThreadPool.QueueUserWorkItem(delegate
                {
                    Bitmap bmp = null;
                    try
                    {
                        using (WebClient wc = new WebClient())
                        {
                            byte[] data = wc.DownloadData("https://i.ytimg.com/vi/" + idc + "/mqdefault.jpg");
                            using (MemoryStream ms = new MemoryStream(data))
                            using (Image src = Image.FromStream(ms))
                            {
                                bmp = new Bitmap(128, 72);
                                using (Graphics g = Graphics.FromImage(bmp))
                                {
                                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                                    g.DrawImage(src, 0, 0, 128, 72);
                                }
                            }
                        }
                    }
                    catch (Exception) { bmp = null; }

                    UI(delegate
                    {
                        if (bmp == null) return;
                        if (thumbs.Count > 400) { thumbs.Clear(); thumbReq.Clear(); thumbReq.Add(idc); }
                        thumbs[idc] = bmp;
                        lst.Invalidate();
                        if (qList != null && queuePanel.Visible) qList.Invalidate();
                        if (histBox != null && histBox.Visible) histBox.Invalidate();
                        if (nowTrack != null && nowTrack.Id == idc) { picNow.Thumb = bmp; picNow.Invalidate(); }
                        if (view == 3 && !nowShown) RefreshHome();
                        else if (view == 2 && plOpen < 0 && !nowShown) RefreshPlList();
                    });
                });
            }
            return null;
        }

        // ================= musicas relacionadas (Mix do YouTube) =================

        void SetRelated(bool on, bool fetchNow)
        {
            related = on;
            if (!on) waitingRelated = false;
            UpdateRelatedButton();
            Save();
            if (on && nowTrack != null) TrimQueueForRelated(nowTrack);
            if (on && fetchNow && nowTrack != null) EnsureRelated();
            if (on) lblInfo.Text = "Relacionadas ligado: depois desta, toca musicas parecidas.";
        }

        // com Relacionadas ligado, a fila deixa de seguir a lista de busca:
        // fica so a faixa atual + as musicas parecidas com ela
        void TrimQueueForRelated(Track cur)
        {
            relToken++;
            queue = new List<Track>();
            queue.Add(cur);
            qIndex = 0;
            if (ReferenceEquals(queueSource, results))
            {
                results = new List<Track>(queue);
                queueSource = results;
                if (view == 0)
                {
                    viewList = results;
                    RefreshList();
                    try { lst.TopIndex = 0; } catch (Exception) { }
                }
            }
        }

        void UpdateRelatedButton()
        {
            btnRelated.Filled = related;
            btnRelated.ForeColor = related ? Accent : Dim;
            btnRelated.OutlineColor = related ? Accent : Color.FromArgb(62, 62, 62);
            btnRelated.Invalidate();
        }

        bool InQueue(string id)
        {
            foreach (Track q in queue) if (q.Id == id) return true;
            return false;
        }

        // busca mais faixas quando a fila esta acabando
        void EnsureRelated()
        {
            if (!related || nowTrack == null || fetchingRelated) return;
            bool nearEnd = qIndex >= queue.Count - 4;
            if (nearEnd || (shuffle && queue.Count < 15)) FetchRelated(nowTrack);
        }

        void WaitForRelated()
        {
            waitingRelated = true;
            lblSub.Text = "Buscando musicas relacionadas...";
            if (!fetchingRelated) FetchRelated(nowTrack);
        }

        static List<Track> ParseTracks(string outp)
        {
            List<Track> res = new List<Track>();
            foreach (string line in outp.Split('\n'))
            {
                string l = line.Trim();
                if (l.Length == 0) continue;
                string[] p = l.Split(new string[] { "|||" }, StringSplitOptions.None);
                if (p.Length < 4 || p[0].Length == 0) continue;
                Track t = new Track();
                t.Id = p[0];
                t.Title = p[1];
                t.Duration = p[2] == "NA" ? "" : p[2];
                t.Channel = p[3] == "NA" ? "" : p[3];
                res.Add(t);
            }
            return res;
        }

        void FetchRelated(Track seed)
        {
            if (seed == null || fetchingRelated) return;
            fetchingRelated = true;
            int myTok = relToken;

            ThreadPool.QueueUserWorkItem(delegate
            {
                ytReady.WaitOne();
                string err = "";
                List<Track> res = new List<Track>();
                try
                {
                    string args = "--encoding utf-8 --flat-playlist --no-warnings --playlist-end 40 --print \"%(id)s|||%(title)s|||%(duration_string)s|||%(channel)s\" \"https://www.youtube.com/watch?v=" + seed.Id + "&list=RD" + seed.Id + "\"";
                    res = ParseTracks(Run(ytdlp, args, 60000, out err));
                }
                catch (Exception ex) { err = ex.Message; }

                UI(delegate
                {
                    fetchingRelated = false;
                    if (myTok != relToken)
                    {
                        // o usuario trocou de musica enquanto buscava: descarta e busca de novo
                        if (related && nowTrack != null) FetchRelated(nowTrack);
                        return;
                    }
                    int added = 0;
                    if (related)
                    {
                        List<Track> fresh = new List<Track>();
                        foreach (Track t in res)
                        {
                            if (!InQueue(t.Id)) { queue.Add(t); fresh.Add(t); added++; }
                        }
                        // se a fila veio da lista de busca, mostra as novas faixas nela
                        if (added > 0 && ReferenceEquals(queueSource, results))
                        {
                            results.AddRange(fresh);
                            if (view == 0) RefreshList();
                        }
                        if (added > 0 && view == 0 && nowTrack != null) lblInfo.Text = "Relacionadas a: " + Trunc(nowTrack.Title, 70);
                    }
                    if (waitingRelated)
                    {
                        waitingRelated = false;
                        if (added > 0) Next(false);
                        else { StopPlayback(); lblSub.Text = "Nao encontrei mais musicas relacionadas."; }
                    }
                });
            });
        }

        // ================= utilidades =================

        static string Fmt(int secs)
        {
            if (secs < 0) secs = 0;
            int h = secs / 3600, m = (secs % 3600) / 60, s = secs % 60;
            if (h > 0) return h + ":" + m.ToString("00") + ":" + s.ToString("00");
            return m + ":" + s.ToString("00");
        }

        static string Trunc(string s, int n)
        {
            if (s == null) return "";
            return s.Length <= n ? s : s.Substring(0, n - 1) + "...";
        }

        void UI(MethodInvoker a)
        {
            if (IsDisposed || !IsHandleCreated) return;
            try { BeginInvoke(a); } catch (Exception) { }
        }

        void SetSub(string s) { UI(delegate { lblSub.Text = s; }); }
        void SetInfo(string s) { UI(delegate { lblInfo.Text = s; }); }

        static string Run(string exe, string args, int timeoutMs, out string err)
        {
            ProcessStartInfo psi = new ProcessStartInfo(exe, args);
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            psi.StandardOutputEncoding = Encoding.UTF8;
            psi.StandardErrorEncoding = Encoding.UTF8;
            StringBuilder o = new StringBuilder();
            StringBuilder e = new StringBuilder();
            using (Process p = Process.Start(psi))
            {
                p.OutputDataReceived += delegate(object s, DataReceivedEventArgs ev)
                {
                    if (ev.Data != null) lock (o) { o.AppendLine(ev.Data); }
                };
                p.ErrorDataReceived += delegate(object s, DataReceivedEventArgs ev)
                {
                    if (ev.Data != null) lock (e) { e.AppendLine(ev.Data); }
                };
                p.BeginOutputReadLine();
                p.BeginErrorReadLine();
                if (!p.WaitForExit(timeoutMs))
                {
                    try { p.Kill(); } catch (Exception) { }
                }
                p.WaitForExit();
            }
            err = e.ToString();
            return o.ToString();
        }

        // ================= yt-dlp =================

        // ================= atualizacao automatica (GitHub Releases) =================

        static Version NormVersion(Version v)
        {
            return new Version(v.Major, v.Minor, Math.Max(0, v.Build), Math.Max(0, v.Revision));
        }

        void CheckUpdate()
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    Thread.Sleep(4000);
                    ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072; // TLS 1.2
                    string json;
                    using (WebClient wc = new WebClient())
                    {
                        wc.Headers[HttpRequestHeader.UserAgent] = "Beat-Updater";
                        wc.Encoding = Encoding.UTF8;
                        json = wc.DownloadString(UpdateApi);
                    }
                    Match mt = Regex.Match(json, "\"tag_name\"\\s*:\\s*\"([^\"]+)\"");
                    Match mu = Regex.Match(json, "\"browser_download_url\"\\s*:\\s*\"([^\"]+BeatSetup\\.exe)\"");
                    if (!mt.Success || !mu.Success) return;
                    string tag = mt.Groups[1].Value;
                    string url = mu.Groups[1].Value;
                    Version remote, local;
                    if (!Version.TryParse(tag.TrimStart('v', 'V'), out remote)) return;
                    if (!Version.TryParse(AppVersion, out local)) return;
                    if (NormVersion(remote) <= NormVersion(local)) return;
                    UI(delegate { AskUpdate(tag, url); });
                }
                catch (Exception) { }
            });
        }

        void AskUpdate(string tag, string url)
        {
            if (updating || IsDisposed) return;
            MessageBox.Show(this, "Uma nova vers\u00E3o do Beat est\u00E1 dispon\u00EDvel (" + tag + ").\n\nO aplicativo vai atualizar agora.",
                "Beat", MessageBoxButtons.OK, MessageBoxIcon.Information);
            StartUpdate(tag, url);
        }

        void UpdateFailed(string tag, string url, string msg)
        {
            updating = false;
            lblInfo.Text = "Falha ao atualizar: " + Trunc(msg, 80);
            if (IsDisposed) return;
            if (MessageBox.Show(this, "Nao foi possivel baixar a atualizacao.\n\n" + Trunc(msg, 120) + "\n\nTentar de novo? Se cancelar, o Beat fecha, pois a atualizacao e obrigatoria.",
                "Beat", MessageBoxButtons.RetryCancel, MessageBoxIcon.Warning) == DialogResult.Retry)
                StartUpdate(tag, url);
            else
            {
                reallyExit = true;
                Close();
            }
        }

        void StartUpdate(string tag, string url)
        {
            if (updating || IsDisposed) return;
            updating = true;
            lblInfo.Text = "Baixando atualizacao...";
            ThreadPool.QueueUserWorkItem(delegate
            {
                string setup = Path.Combine(Path.GetTempPath(), "BeatSetup-update.exe");
                try
                {
                    ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072;
                    using (WebClient wc = new WebClient())
                    {
                        wc.Headers[HttpRequestHeader.UserAgent] = "Beat-Updater";
                        wc.DownloadFile(url, setup);
                    }
                    if (new FileInfo(setup).Length < 50000) throw new Exception("arquivo baixado invalido");
                    UI(delegate
                    {
                        try
                        {
                            ProcessStartInfo psi = new ProcessStartInfo(setup, "/update \"" + appDir.TrimEnd('\\') + "\"");
                            psi.UseShellExecute = false;
                            Process.Start(psi);
                            reallyExit = true;
                            Close();
                        }
                        catch (Exception ex2)
                        {
                            UpdateFailed(tag, url, ex2.Message);
                        }
                    });
                }
                catch (Exception ex)
                {
                    string msg = ex.Message;
                    UI(delegate { UpdateFailed(tag, url, msg); });
                }
            });
        }

        void EnsureYtdlp()
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    if (!File.Exists(ytdlp))
                    {
                        SetSub("Baixando yt-dlp (so na primeira vez)...");
                        ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072; // TLS 1.2
                        string tmp = ytdlp + ".tmp";
                        using (WebClient wc = new WebClient())
                        {
                            wc.DownloadFile("https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe", tmp);
                        }
                        File.Move(tmp, ytdlp);
                        SetSub("Pronto.");
                    }
                    else if ((DateTime.Now - File.GetLastWriteTime(ytdlp)).TotalDays > 7)
                    {
                        string err;
                        Run(ytdlp, "-U", 60000, out err);
                    }
                    ytReady.Set();
                }
                catch (Exception ex)
                {
                    SetSub("Falha ao baixar o yt-dlp: " + ex.Message);
                }
            });
        }

        // ================= lista de playlists (com busca e capas) =================

        string CoverDir { get { return Path.Combine(Path.GetDirectoryName(dataFile), "capas"); } }

        void BuildPlListPanel()
        {
            plListPanel = new ScrollPanel();
            plListPanel.Dock = DockStyle.Fill;
            plListPanel.BackColor = BgMain;
            plListPanel.Visible = false;
            plListPanel.HandleCreated += delegate { Native.DarkScroll(plListPanel.Handle); };
            plListPanel.SizeChanged += delegate { if (plListPanel.Visible) RefreshPlList(); };
            Controls.Add(plListPanel);
        }

        void ScrollPl(int delta)
        {
            if (plListPanel == null || !plListPanel.Visible) return;
            int cur = -plListPanel.AutoScrollPosition.Y;
            plListPanel.AutoScrollPosition = new Point(0, Math.Max(0, cur - delta));
        }

        void AttachPlFocus(Control c)
        {
            c.MouseEnter += delegate { if (plListPanel.Visible && !txtPl.Focused) plListPanel.Focus(); };
            foreach (Control ch in c.Controls) AttachPlFocus(ch);
        }

        Bitmap LoadCoverFile(string name)
        {
            Bitmap b;
            if (coverFiles.TryGetValue(name, out b)) return b;
            b = null;
            try
            {
                string path = Path.Combine(CoverDir, name);
                if (File.Exists(path))
                {
                    using (Image im = Image.FromFile(path)) b = new Bitmap(im);
                }
            }
            catch (Exception) { b = null; }
            if (b != null) coverFiles[name] = b;
            return b;
        }

        Bitmap PlaylistThumb(Playlist pl)
        {
            if (pl.Cover != null && pl.Cover.StartsWith("f:"))
            {
                Bitmap b = LoadCoverFile(pl.Cover.Substring(2));
                if (b != null) return b;
            }
            else if (pl.Cover != null && pl.Cover.StartsWith("t:"))
            {
                return GetThumb(pl.Cover.Substring(2));
            }
            return pl.Items.Count > 0 ? GetThumb(pl.Items[0].Id) : null;
        }

        void DropCoverFile(Playlist p)
        {
            if (p.Cover == null || !p.Cover.StartsWith("f:")) return;
            string name = p.Cover.Substring(2);
            Bitmap b;
            if (coverFiles.TryGetValue(name, out b)) { coverFiles.Remove(name); b.Dispose(); }
            try { File.Delete(Path.Combine(CoverDir, name)); } catch (Exception) { }
        }

        void CoverChanged()
        {
            Save();
            if (view == 2 && plOpen < 0) RefreshPlList();
            else if (view == 3) RefreshHome();
        }

        void PickCoverFile(Playlist p)
        {
            using (OpenFileDialog d = new OpenFileDialog())
            {
                d.Title = "Escolher capa da playlist";
                d.Filter = "Imagens|*.png;*.jpg;*.jpeg;*.bmp|Todos os arquivos|*.*";
                if (d.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    Directory.CreateDirectory(CoverDir);
                    string nm = Guid.NewGuid().ToString("N") + ".png";
                    using (Image src = Image.FromFile(d.FileName))
                    using (Bitmap bm = new Bitmap(320, 180))
                    {
                        using (Graphics g = Graphics.FromImage(bm))
                        {
                            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                            float sc = Math.Max(320f / src.Width, 180f / src.Height);
                            float w = src.Width * sc, h = src.Height * sc;
                            g.DrawImage(src, (320f - w) / 2f, (180f - h) / 2f, w, h);
                        }
                        bm.Save(Path.Combine(CoverDir, nm), System.Drawing.Imaging.ImageFormat.Png);
                    }
                    DropCoverFile(p);
                    p.Cover = "f:" + nm;
                }
                catch (Exception)
                {
                    MessageBox.Show(this, "Nao foi possivel abrir essa imagem.");
                    return;
                }
            }
            CoverChanged();
        }

        void ShowCoverMenu(Playlist p, Control anchor, Point pt)
        {
            ContextMenuStrip m = new ContextMenuStrip();
            m.Renderer = new ToolStripProfessionalRenderer(new DarkColors());
            m.ForeColor = Color.White;
            m.Items.Add(MI("Usar a primeira musica", delegate { DropCoverFile(p); p.Cover = null; CoverChanged(); }));
            m.Items.Add(MI("Escolher imagem do computador...", delegate { PickCoverFile(p); }));
            if (p.Items.Count > 0)
            {
                ToolStripMenuItem sub = MI("Usar a capa de outra musica", null);
                for (int i = 0; i < p.Items.Count && i < 40; i++)
                {
                    Track tk = p.Items[i];
                    sub.DropDownItems.Add(MI(Trunc(tk.Title, 50), delegate { DropCoverFile(p); p.Cover = "t:" + tk.Id; CoverChanged(); }));
                }
                m.Items.Add(sub);
            }
            m.Show(anchor, pt);
        }

        void RefreshPlList()
        {
            if (plListPanel == null || plBuilding) return;
            plBuilding = true;
            try
            {
                int keepY = -plListPanel.AutoScrollPosition.Y;
                plListPanel.AutoScrollPosition = new Point(0, 0);
                plListPanel.SuspendLayout();
                List<Control> old = new List<Control>();
                foreach (Control c in plListPanel.Controls) old.Add(c);
                plListPanel.Controls.Clear();
                foreach (Control c in old) c.Dispose();

                string f = txtPl.Text.Trim().ToLower();
                int W = Math.Max(300, plListPanel.ClientSize.Width);
                int x0 = 16, y = 12, cw = W - 2 * x0;
                int shown = 0;

                for (int i = 0; i < playlists.Count; i++)
                {
                    Playlist pl = playlists[i];
                    if (f.Length > 0 && pl.Name.ToLower().IndexOf(f) < 0) continue;
                    shown++;
                    int pidx = i;
                    Playlist plc = pl;

                    RoundPanel row = new RoundPanel();
                    row.FillColor = BgPanel;
                    row.Radius = 10;
                    row.Cursor = Cursors.Hand;
                    row.SetBounds(x0, y, cw, 56);

                    ThumbBox tb = new ThumbBox();
                    tb.Fit = true;
                    tb.Edit = true;
                    tb.BackColor = BgPanel;
                    tb.Cursor = Cursors.Hand;
                    tb.SetBounds(10, 10, 64, 36);
                    tb.Thumb = PlaylistThumb(pl);
                    ThumbBox tbc = tb;
                    tb.Click += delegate { ShowCoverMenu(plc, tbc, new Point(0, tbc.Height)); };

                    string how = pl.Cover == null ? "primeira musica" : (pl.Cover.StartsWith("f:") ? "imagem propria" : "outra musica");
                    Label nm = HomeLabel(pl.Name, fontBold, Color.White, BgPanel, 88, 8, cw - 88 - 56, 22);
                    Label sb2 = HomeLabel(pl.Items.Count + (pl.Items.Count == 1 ? " musica" : " musicas") + "  ·  capa: " + how, fontSmall, Dim, BgPanel, 88, 31, cw - 88 - 56, 18);
                    nm.Cursor = Cursors.Hand;
                    sb2.Cursor = Cursors.Hand;

                    IconButton del = MakeIcon("✕", new Size(32, 32), Dim);
                    del.ClearColor = BgPanel;
                    del.Location = new Point(cw - 32 - 12, 12);
                    tt.SetToolTip(del, "Excluir playlist");
                    del.Click += delegate { BeginInvoke((MethodInvoker)delegate { DeletePlaylistAt(pidx); }); };

                    EventHandler open = delegate { OpenPlaylist(pidx); };
                    row.Click += open; nm.Click += open; sb2.Click += open;

                    row.Controls.Add(tb);
                    row.Controls.Add(nm);
                    row.Controls.Add(sb2);
                    row.Controls.Add(del);
                    plListPanel.Controls.Add(row);
                    y += 56 + 6;
                }

                if (shown == 0)
                {
                    string msg = playlists.Count == 0 ? "Nenhuma playlist ainda. Clique em Nova, ou use o botao direito numa musica." : "Nenhuma playlist com esse nome.";
                    plListPanel.Controls.Add(HomeLabel(msg, Font, Dim, BgMain, x0, y + 8, cw, 24));
                    y += 40;
                }

                foreach (Control c in plListPanel.Controls) AttachPlFocus(c);
                plListPanel.AutoScrollMinSize = new Size(0, y + 12);
                plListPanel.ResumeLayout();
                plListPanel.AutoScrollPosition = new Point(0, keepY);
            }
            finally { plBuilding = false; }
        }

        // ================= tela cheia do player =================

        void BuildNowPanel()
        {
            nowPanel = new Panel();
            nowPanel.Dock = DockStyle.Fill;
            nowPanel.BackColor = BgPanel;
            nowPanel.Visible = false;

            bBack = MakeIcon("", new Size(40, 40), Color.White);
            bBack.Glyph = "back";
            bBack.Filled = true;
            bBack.BackColor = Color.FromArgb(38, 38, 38);
            bBack.Location = new Point(18, 16);
            bBack.Click += delegate { HideNow(); };
            tt.SetToolTip(bBack, "Voltar");

            picBig = new ThumbBox();
            picBig.Fit = true;
            picBig.BackColor = BgPanel;

            lblBigTitle = new Label();
            lblBigTitle.Font = fBigTitle;
            lblBigTitle.ForeColor = Color.White;
            lblBigTitle.BackColor = BgPanel;
            lblBigTitle.TextAlign = ContentAlignment.MiddleCenter;
            lblBigTitle.AutoEllipsis = true;

            lblBigSub = new Label();
            lblBigSub.ForeColor = Dim;
            lblBigSub.BackColor = BgPanel;
            lblBigSub.TextAlign = ContentAlignment.MiddleCenter;
            lblBigSub.AutoEllipsis = true;

            lblPos2 = new Label();
            lblPos2.ForeColor = Dim;
            lblPos2.BackColor = BgPanel;
            lblPos2.TextAlign = ContentAlignment.MiddleRight;
            lblPos2.Text = "0:00";

            lblDur2 = new Label();
            lblDur2.ForeColor = Dim;
            lblDur2.BackColor = BgPanel;
            lblDur2.TextAlign = ContentAlignment.MiddleLeft;
            lblDur2.Text = "0:00";

            trkSeek2 = new DarkSlider();
            trkSeek2.BackColor = BgPanel;
            trkSeek2.Minimum = 0;
            trkSeek2.Maximum = 1;
            trkSeek2.Changing += delegate { lblPos2.Text = Fmt(trkSeek2.Value); };
            trkSeek2.Committed += delegate
            {
                try { wmp.controls.currentPosition = (double)trkSeek2.Value; } catch (Exception) { }
            };

            bShuffle = MakeIcon("⇄", new Size(44, 44), Dim);
            bShuffle.Click += delegate { shuffle = !shuffle; UpdateModeButtons(); Save(); };
            bPrev = MakeIcon("", new Size(44, 44), Color.White);
            bPrev.Glyph = "prev";
            bPrev.Click += delegate { Prev(); };
            bPlay = MakeIcon("", new Size(64, 64), Color.White);
            bPlay.Filled = true;
            bPlay.BackColor = Accent;
            bPlay.Glyph = "play";
            bPlay.Click += delegate { TogglePlay(); };
            bNext = MakeIcon("", new Size(44, 44), Color.White);
            bNext.Glyph = "next";
            bNext.Click += delegate { Next(false); };
            bRepeat = MakeIcon("↻", new Size(44, 44), Dim);
            bRepeat.Click += delegate { repeat = (repeat + 1) % 3; UpdateModeButtons(); Save(); };

            bRelated2 = new IconButton();
            bRelated2.Text = "Relacionadas";
            bRelated2.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
            bRelated2.Size = new Size(124, 30);
            bRelated2.BackColor = Color.FromArgb(61, 33, 33);
            bRelated2.ForeColor = Dim;
            bRelated2.Radius = 15;
            bRelated2.Outline = true;
            bRelated2.Star = true;
            bRelated2.Click += delegate { SetRelated(!related, true); };

            bLink = new IconButton();
            bLink.Text = "Copiar link";
            bLink.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
            bLink.Size = new Size(104, 30);
            bLink.BackColor = BgPanel;
            bLink.ForeColor = Dim;
            bLink.Radius = 15;
            bLink.Outline = true;
            bLink.Click += delegate { CopyLink(nowTrack); };

            bHeart2 = MakeIcon("", new Size(40, 40), Dim);
            bHeart2.Glyph = "heart";
            bHeart2.Click += delegate { if (nowTrack != null) ToggleFav(nowTrack); };

            lblVolTxt = new Label();
            lblVolTxt.Text = "Volume";
            lblVolTxt.ForeColor = Dim;
            lblVolTxt.BackColor = BgPanel;
            lblVolTxt.TextAlign = ContentAlignment.MiddleRight;

            trkVol2 = new DarkSlider();
            trkVol2.BackColor = BgPanel;
            trkVol2.Minimum = 0;
            trkVol2.Maximum = 100;
            trkVol2.Value = 70;
            trkVol2.Changing += delegate
            {
                try { wmp.settings.volume = trkVol2.Value; } catch (Exception) { }
                trkVol.Value = trkVol2.Value;
            };
            trkVol2.Committed += delegate { Save(); };

            nowPanel.Controls.Add(bBack);
            nowPanel.Controls.Add(picBig);
            nowPanel.Controls.Add(lblBigTitle);
            nowPanel.Controls.Add(lblBigSub);
            nowPanel.Controls.Add(lblPos2);
            nowPanel.Controls.Add(trkSeek2);
            nowPanel.Controls.Add(lblDur2);
            nowPanel.Controls.Add(bShuffle);
            nowPanel.Controls.Add(bPrev);
            nowPanel.Controls.Add(bPlay);
            nowPanel.Controls.Add(bNext);
            nowPanel.Controls.Add(bRepeat);
            bRelated2.Visible = false;
            bLetra2 = MakeLetra();
            bLetra2.BackColor = BgPanel;
            bLetra2.Click += delegate { ToggleLyrics(); };
            bAdd2 = MakeAddBtn();
            bAdd2.Click += delegate { ShowAddPop(bAdd2, false); };
            lyricsView = new LyricsView();
            lyricsView.Visible = false;
            lyricsView.LineClicked += delegate(int li)
            {
                if (li < 0 || li >= lyricsView.Times.Length) return;
                try { wmp.controls.currentPosition = lyricsView.Times[li] + LyricsLag + 0.05; } catch (Exception) { }
                lyricsHoldUntil = DateTime.Now.AddMilliseconds(700);
                lyricsView.SetCurrent(li);
            };
            nowPanel.Controls.Add(lyricsView);
            nowPanel.Controls.Add(bLetra2);
            bMini2 = MakeMiniBtn();
            bMini2.Click += delegate { OpenMini(); };
            nowPanel.Controls.Add(bMini2);
            nowPanel.Controls.Add(bAdd2);
            nowPanel.Controls.Add(bRelated2);
            nowPanel.Controls.Add(bHeart2);
            nowPanel.Controls.Add(bLink);
            nowPanel.Controls.Add(lblVolTxt);
            nowPanel.Controls.Add(trkVol2);
            nowPanel.Resize += delegate { LayoutNow(); };
            Controls.Add(nowPanel);

            nowTimer = new System.Windows.Forms.Timer();
            nowTimer.Interval = 120;
            nowTimer.Tick += delegate { SyncNow(); };
        }

        void LayoutNow()
        {
            int W = nowPanel.ClientSize.Width, H = nowPanel.ClientSize.Height;
            if (W < 100 || H < 100) return;
            int imgH = Math.Max(90, Math.Min(300, H - 260));
            int imgW = imgH * 16 / 9;
            if (imgW > W - 80) { imgW = Math.Max(120, W - 80); imgH = imgW * 9 / 16; }
            int blockH = imgH + 240;
            int y = Math.Max(16, (H - blockH) / 2);
            picBig.SetBounds((W - imgW) / 2, y, imgW, imgH);
            int lw = Math.Min(W - 80, 780);
            lyricsView.SetBounds((W - lw) / 2, y, lw, imgH);
            y += imgH + 18;
            lblBigTitle.SetBounds(40, y, W - 80, 26); y += 26;
            lblBigSub.SetBounds(40, y, W - 80, 22); y += 22 + 18;
            int sw = Math.Min(520, W - 160);
            int sx = (W - sw) / 2;
            lblPos2.SetBounds(sx - 54, y, 50, 20);
            trkSeek2.SetBounds(sx, y, sw, 20);
            lblDur2.SetBounds(sx + sw + 4, y, 50, 20);
            y += 20 + 18;
            int gap = 14;
            int total = 44 * 4 + 64 + gap * 4;
            int cx = (W - total) / 2;
            int cy = y + 32;
            bShuffle.SetBounds(cx, cy - 22, 44, 44); cx += 44 + gap;
            bPrev.SetBounds(cx, cy - 22, 44, 44); cx += 44 + gap;
            bPlay.SetBounds(cx, cy - 32, 64, 64); cx += 64 + gap;
            bNext.SetBounds(cx, cy - 22, 44, 44); cx += 44 + gap;
            bRepeat.SetBounds(cx, cy - 22, 44, 44);
            y += 64 + 18;
            int g40 = 40;
            int rowW = 70 + g40 + 104 + g40 + 40 + g40 + 30 + g40 + 40 + g40 + 60 + 8 + 110;
            int rx = (W - rowW) / 2;
            bLetra2.SetBounds(rx, y + 2, 70, 30); rx += 70 + g40;
            bLink.SetBounds(rx, y + 2, 104, 30); rx += 104 + g40;
            bHeart2.SetBounds(rx, y - 1, 40, 40); rx += 40 + g40;
            bAdd2.SetBounds(rx, y + 4, 30, 30); rx += 30 + g40;
            bMini2.SetBounds(rx, y - 1, 40, 40); rx += 40 + g40;
            lblVolTxt.SetBounds(rx, y + 6, 60, 20); rx += 60 + 8;
            trkVol2.SetBounds(rx, y + 6, 110, 20);
        }

        void ApplyView()
        {
            if (nowPanel == null || homePanel == null || plListPanel == null) return;
            bool home = view == 3;
            bool plList = view == 2 && plOpen < 0;
            bool plIn = view == 2 && plOpen >= 0;
            SuspendLayout();
            plPanel.Visible = view == 2 || view == 1;
            pnlPlSearch.Visible = plList || view == 1;
            btnPlNew.Visible = plList;
            btnPlBack.Visible = plIn;
            btnPlCover.Visible = plIn;
            topPanel.Visible = !nowShown;
            bottomPanel.Visible = !nowShown;
            infoPanel.Visible = !nowShown && !home;
            lst.Visible = !nowShown && !home && !plList;
            plListPanel.Visible = !nowShown && plList;
            homePanel.Visible = !nowShown && home;
            nowPanel.Visible = nowShown;
            lst.AllowDrag = view == 2 && plOpen >= 0;
            if (queuePanel != null) queuePanel.Visible = queueOpen && !nowShown && !home;
            if (linkCard != null) linkCard.Visible = linkShown && view == 0 && !nowShown;
            if (btnQueue != null) btnQueue.ForeColor = queueOpen ? Accent : Color.White;
            ResumeLayout(true);
            if (nowShown) ApplyLyricsMode();
        }

        void ShowNow()
        {
            if (nowTrack == null) return;
            nowShown = true;
            HideHist();
            ApplyView();
            LayoutNow();
            UpdateNowPanel();
            ApplyLyricsMode();
            SyncNow();
            nowTimer.Start();
        }

        void HideNow()
        {
            nowShown = false;
            nowTimer.Stop();
            if (lyricsOpen) { lyricsOpen = false; UpdateLetraButtons(); }   // fechar a tela cheia fecha a letra (botao apaga)
            ApplyView();
            if (view == 3) RefreshHome();
        }

        void UpdateNowPanel()
        {
            if (nowTrack == null)
            {
                bigShownId = null;
                lblBigTitle.Text = "Nada tocando";
                picBig.Thumb = null;
                picBig.Invalidate();
                return;
            }
            bigShownId = nowTrack.Id;
            lblBigTitle.Text = nowTrack.Title;
            Bitmap bb = GetBigThumb(nowTrack.Id);
            picBig.Thumb = bb != null ? bb : GetThumb(nowTrack.Id);
            picBig.Invalidate();
        }

        // mantem a tela cheia igual ao player de baixo (progresso, botoes, volume)
        void SyncNow()
        {
            if (!nowShown) return;
            if (nowTrack != null && bigShownId != nowTrack.Id) UpdateNowPanel();
            if (trkSeek2.Maximum != trkSeek.Maximum) trkSeek2.Maximum = trkSeek.Maximum;
            if (!trkSeek2.Dragging)
            {
                trkSeek2.Value = trkSeek.Value;
                if (lblPos2.Text != lblPos.Text) lblPos2.Text = lblPos.Text;
            }
            if (lblDur2.Text != lblDur.Text) lblDur2.Text = lblDur.Text;
            if (!trkVol2.Dragging && trkVol2.Value != trkVol.Value) trkVol2.Value = trkVol.Value;
            if (lblBigSub.Text != lblSub.Text) lblBigSub.Text = lblSub.Text;
            if (lyricsView.Visible && lyricsView.Synced && DateTime.Now >= lyricsHoldUntil)
            {
                double pos = 0;
                try { pos = (double)wmp.controls.currentPosition; } catch (Exception) { }
                int li = -1;
                for (int k = 0; k < lyricsView.Times.Length; k++) { if (lyricsView.Times[k] <= pos - LyricsLag) li = k; else break; }
                lyricsView.SetCurrent(li);
            }
            if (bPlay.Glyph != btnPlay.Glyph) { bPlay.Glyph = btnPlay.Glyph; bPlay.Invalidate(); }
            if (bShuffle.ForeColor != btnShuffle.ForeColor) { bShuffle.ForeColor = btnShuffle.ForeColor; bShuffle.Invalidate(); }
            if (bRepeat.ForeColor != btnRepeat.ForeColor || bRepeat.Text != btnRepeat.Text)
            {
                bRepeat.ForeColor = btnRepeat.ForeColor;
                bRepeat.Text = btnRepeat.Text;
                bRepeat.Invalidate();
            }
            if (bHeart2.ForeColor != btnHeart.ForeColor || bHeart2.Glyph != btnHeart.Glyph)
            {
                bHeart2.ForeColor = btnHeart.ForeColor;
                bHeart2.Glyph = btnHeart.Glyph;
                bHeart2.Invalidate();
            }
            if (bRelated2.Filled != btnRelated.Filled || bRelated2.ForeColor != btnRelated.ForeColor || bRelated2.OutlineColor != btnRelated.OutlineColor)
            {
                bRelated2.Filled = btnRelated.Filled;
                bRelated2.ForeColor = btnRelated.ForeColor;
                bRelated2.OutlineColor = btnRelated.OutlineColor;
                bRelated2.Invalidate();
            }
        }

        // capa grande (maxres, com fallback para a media)
        Bitmap GetBigThumb(string id)
        {
            Bitmap b;
            if (bigThumbs.TryGetValue(id, out b)) return b;
            if (!bigReq.Contains(id))
            {
                bigReq.Add(id);
                string idc = id;
                ThreadPool.QueueUserWorkItem(delegate
                {
                    Bitmap bmp = null;
                    string[] names = new string[] { "maxresdefault", "mqdefault" };
                    foreach (string nm in names)
                    {
                        try
                        {
                            using (WebClient wc = new WebClient())
                            {
                                byte[] data = wc.DownloadData("https://i.ytimg.com/vi/" + idc + "/" + nm + ".jpg");
                                using (MemoryStream ms = new MemoryStream(data))
                                using (Image src = Image.FromStream(ms))
                                {
                                    bmp = new Bitmap(640, 360);
                                    using (Graphics g = Graphics.FromImage(bmp))
                                    {
                                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                                        g.DrawImage(src, 0, 0, 640, 360);
                                    }
                                }
                            }
                        }
                        catch (Exception) { bmp = null; }
                        if (bmp != null) break;
                    }
                    UI(delegate
                    {
                        if (bmp == null) return;
                        if (bigThumbs.Count > 6) { bigThumbs.Clear(); bigReq.Clear(); bigReq.Add(idc); }
                        bigThumbs[idc] = bmp;
                        if (nowShown && bigShownId == idc) { picBig.Thumb = bmp; picBig.Invalidate(); }
                    });
                });
            }
            return null;
        }

        // ================= link do YouTube na busca =================

        static bool ParseYtLink(string q, out string vid, out string list)
        {
            vid = null; list = null;
            if (!Regex.IsMatch(q, @"^(https?://)?(www\.|m\.|music\.)?(youtube\.com|youtu\.be)/", RegexOptions.IgnoreCase)) return false;
            Match mv = Regex.Match(q, @"(?:youtu\.be/|[?&]v=|/shorts/|/embed/)([A-Za-z0-9_\-]{11})");
            Match ml = Regex.Match(q, @"[?&]list=([A-Za-z0-9_\-]+)");
            if (mv.Success) vid = mv.Groups[1].Value;
            if (ml.Success) list = ml.Groups[1].Value;
            if (list != null && list.StartsWith("RD") && vid != null) list = null;
            return vid != null || list != null;
        }

        void DoLink(string vid, string list)
        {
            int my = ++searchToken;
            if (view != 0) SetView(0);
            lblInfo.Text = "Lendo o link...";
            HideHist();
            bool isList = list != null;
            ThreadPool.QueueUserWorkItem(delegate
            {
                ytReady.WaitOne();
                string err = "";
                List<Track> res = new List<Track>();
                string plName = null;
                try
                {
                    string url = isList ? "https://www.youtube.com/playlist?list=" + list : "https://www.youtube.com/watch?v=" + vid;
                    string args = "--encoding utf-8 --flat-playlist --no-warnings " + (isList ? "" : "--no-playlist ") + "--print \"%(id)s|||%(title)s|||%(duration_string)s|||%(channel)s|||%(playlist_title)s\" \"" + url + "\"";
                    string outp = Run(ytdlp, args, 90000, out err);
                    foreach (string line in outp.Split('\n'))
                    {
                        string l = line.Trim();
                        if (l.Length == 0) continue;
                        string[] pp = l.Split(new string[] { "|||" }, StringSplitOptions.None);
                        if (pp.Length < 4 || pp[0].Length == 0) continue;
                        Track t = new Track();
                        t.Id = pp[0]; t.Title = pp[1];
                        t.Duration = pp[2] == "NA" ? "" : pp[2];
                        t.Channel = pp[3] == "NA" ? "" : pp[3];
                        res.Add(t);
                        if (plName == null && pp.Length > 4 && pp[4] != "NA" && pp[4].Length > 0) plName = pp[4];
                    }
                }
                catch (Exception ex) { err = ex.Message; }
                UI(delegate
                {
                    if (my != searchToken) return;
                    if (res.Count == 0) { lblInfo.Text = "Não consegui ler esse link. " + Trunc(err.Trim(), 100); return; }
                    results = res;
                    resultsQuery = null;
                    linkTracks = res;
                    linkTitle = isList ? (plName != null ? plName : "Playlist do YouTube") : res[0].Title;
                    linkShown = true;
                    BuildLinkCardContent(isList);
                    SetView(0);
                    ApplyView();
                });
            });
        }

        void BuildLinkCard()
        {
            linkCard = new Panel();
            linkCard.Dock = DockStyle.Top;
            linkCard.Height = 108;
            linkCard.BackColor = BgMain;
            linkCard.Padding = new Padding(16, 6, 16, 8);
            linkCard.Visible = false;
        }

        void BuildLinkCardContent(bool isList)
        {
            List<Control> old = new List<Control>();
            foreach (Control c in linkCard.Controls) old.Add(c);
            linkCard.Controls.Clear();
            foreach (Control c in old) c.Dispose();
            RoundPanel card = new RoundPanel();
            card.Dock = DockStyle.Fill;
            card.FillColor = BgPanel;
            card.Radius = 12;
            ThumbBox tb = new ThumbBox();
            tb.Fit = true;
            tb.BackColor = BgPanel;
            tb.SetBounds(12, 10, 128, 72);
            tb.Thumb = GetThumb(linkTracks[0].Id);
            card.Controls.Add(tb);
            Label t1 = new Label();
            t1.Text = linkTitle;
            t1.Font = fontBold;
            t1.ForeColor = Color.White;
            t1.BackColor = BgPanel;
            t1.AutoEllipsis = true;
            t1.SetBounds(154, 14, 420, 24);
            card.Controls.Add(t1);
            Label t2 = new Label();
            t2.Text = isList ? "Playlist do YouTube · " + linkTracks.Count + (linkTracks.Count == 1 ? " música" : " músicas") : linkTracks[0].Channel;
            t2.Font = fontSmall;
            t2.ForeColor = Dim;
            t2.BackColor = BgPanel;
            t2.AutoEllipsis = true;
            t2.SetBounds(154, 40, 420, 20);
            card.Controls.Add(t2);
            IconButton bp = new IconButton();
            bp.Text = "Tocar";
            bp.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
            bp.Filled = true; bp.Radius = 8; bp.BackColor = Accent; bp.ForeColor = Color.White;
            bp.ClearColor = BgPanel;
            bp.SetBounds(154, 62, 90, 28);
            bp.Click += delegate { PlayList(results, 0); };
            card.Controls.Add(bp);
            if (isList)
            {
                IconButton bi = new IconButton();
                bi.Text = "Importar como playlist";
                bi.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
                bi.Filled = true; bi.Radius = 8; bi.BackColor = Color.FromArgb(40, 40, 40); bi.ForeColor = Color.White;
                bi.ClearColor = BgPanel;
                bi.SetBounds(254, 62, 170, 28);
                bi.Click += delegate { ImportLinkPlaylist(); };
                card.Controls.Add(bi);
            }
            linkCard.Controls.Add(card);
        }

        void ImportLinkPlaylist()
        {
            if (linkTracks == null || linkTracks.Count == 0) return;
            string name = linkTitle;
            int n = 2;
            while (true)
            {
                bool dup = false;
                foreach (Playlist q in playlists) if (string.Equals(q.Name, name, StringComparison.OrdinalIgnoreCase)) { dup = true; break; }
                if (!dup) break;
                name = linkTitle + " (" + n + ")";
                n++;
            }
            Playlist p = new Playlist();
            p.Name = name;
            foreach (Track t in linkTracks) p.Items.Add(t.Copy());
            playlists.Add(p);
            Save();
            lblInfo.Text = "Playlist \"" + name + "\" importada (" + p.Items.Count + " músicas).";
        }

        // ================= fila de reproducao =================

        void BuildQueuePanel()
        {
            queuePanel = new Panel();
            queuePanel.Dock = DockStyle.Right;
            queuePanel.Width = 340;
            queuePanel.BackColor = BgPanel;
            queuePanel.Visible = false;

            qList = new DBListBox();
            qList.Dock = DockStyle.Fill;
            qList.BackColor = BgPanel;
            qList.ForeColor = Color.White;
            qList.BorderStyle = BorderStyle.None;
            qList.DrawMode = DrawMode.OwnerDrawFixed;
            qList.ItemHeight = 56;
            qList.IntegralHeight = false;
            qList.AllowDrag = true;
            qList.HandleWidth = 40;
            qList.DrawItem += qList_DrawItem;
            qList.HandleCreated += delegate { Native.DarkScroll(qList.Handle); };
            qList.Reordered += delegate(int from, int to)
            {
                int a = qIndex + 1 + from, b = qIndex + 1 + to;
                if (a < 0 || a >= queue.Count || b < 0 || b >= queue.Count) return;
                Track t = queue[a];
                queue.RemoveAt(a);
                queue.Insert(b, t);
                RefreshQueue();
            };
            qList.MouseDown += delegate(object s, MouseEventArgs e) { qDownY = e.Y; };
            qList.MouseUp += delegate(object s, MouseEventArgs e)
            {
                if (e.Button != MouseButtons.Left || Math.Abs(e.Y - qDownY) > 5) return;
                int i = qList.IndexFromPoint(e.Location);
                int ti = qIndex + 1 + i;
                if (i < 0 || ti >= queue.Count || e.X < 40) return;
                if (e.X >= qList.Width - 40) { queue.RemoveAt(ti); RefreshQueue(); }
                else { qIndex = ti; PlayCurrent(); }
            };

            Panel head = new Panel();
            head.Dock = DockStyle.Top;
            head.Height = 48;
            head.BackColor = BgPanel;
            IconButton clr = MakeSmall("Limpar");
            clr.Dock = DockStyle.Right;
            clr.ClearColor = BgPanel;
            clr.Click += delegate
            {
                if (qIndex + 1 < queue.Count) queue.RemoveRange(qIndex + 1, queue.Count - qIndex - 1);
                RefreshQueue();
            };
            Panel hs = new Panel(); hs.Dock = DockStyle.Right; hs.Width = 12;
            lblQCount = new Label();
            lblQCount.Dock = DockStyle.Fill;
            lblQCount.Font = fontBold;
            lblQCount.ForeColor = Color.White;
            lblQCount.Padding = new Padding(16, 0, 0, 0);
            lblQCount.TextAlign = ContentAlignment.MiddleLeft;
            head.Controls.Add(lblQCount);
            head.Controls.Add(hs);
            head.Controls.Add(clr);
            Panel bar = new Panel();
            bar.Dock = DockStyle.Left;
            bar.Width = 1;
            bar.BackColor = Line;

            queuePanel.Controls.Add(qList);
            queuePanel.Controls.Add(head);
            queuePanel.Controls.Add(bar);
        }

        void qList_DrawItem(object sender, DrawItemEventArgs e)
        {
            int ti = qIndex + 1 + e.Index;
            if (e.Index < 0 || ti >= queue.Count) return;
            Track t = queue[ti];
            bool sel = (e.State & DrawItemState.Selected) != 0;
            using (SolidBrush b = new SolidBrush(sel ? BgSel : BgPanel)) e.Graphics.FillRectangle(b, e.Bounds);
            Rectangle bd = e.Bounds;
            TextRenderer.DrawText(e.Graphics, "≡", fontSym, new Rectangle(bd.X, bd.Y, 40, bd.Height), Color.FromArgb(120, 120, 120),
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            Gfx.DrawThumb(e.Graphics, new Rectangle(bd.X + 40, bd.Y + 11, 56, 34), GetThumb(t.Id), 5);
            int tx = bd.X + 106, tw = Math.Max(10, bd.Width - 106 - 40);
            TextFormatFlags f = TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.Left;
            TextRenderer.DrawText(e.Graphics, t.Title, fontSmall, new Rectangle(tx, bd.Y + 9, tw, 20), Color.White, f);
            TextRenderer.DrawText(e.Graphics, t.Channel, fontSmall, new Rectangle(tx, bd.Y + 29, tw, 18), Dim, f);
            TextRenderer.DrawText(e.Graphics, "✕", fHistX, new Rectangle(bd.Right - 40, bd.Y, 40, bd.Height), Color.FromArgb(119, 119, 119),
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            using (Pen pn = new Pen(Line)) e.Graphics.DrawLine(pn, bd.X + 16, bd.Bottom - 1, bd.Right - 16, bd.Bottom - 1);
        }

        void RefreshQueue()
        {
            if (qList == null) return;
            int n = Math.Max(0, queue.Count - qIndex - 1);
            qList.BeginUpdate();
            qList.Items.Clear();
            for (int i = 0; i < n; i++) qList.Items.Add(queue[qIndex + 1 + i].Title);
            qList.EndUpdate();
            lblQCount.Text = "A seguir (" + n + ")";
        }

        void ToggleQueue()
        {
            queueOpen = !queueOpen;
            RefreshQueue();
            ApplyView();
        }

        void QueuePlayNext(Track t)
        {
            if (nowTrack == null || queue.Count == 0) { StartSingle(t); return; }
            queue.Insert(Math.Min(queue.Count, qIndex + 1), t.Copy());
            RefreshQueue();
            lblInfo.Text = "Vai tocar depois: " + Trunc(t.Title, 60);
        }

        void QueueAdd(Track t)
        {
            if (nowTrack == null || queue.Count == 0) { StartSingle(t); return; }
            queue.Add(t.Copy());
            RefreshQueue();
            lblInfo.Text = "Adicionada à fila: " + Trunc(t.Title, 60);
        }

        // ================= mini player =================

        void OpenMini()
        {
            if (miniForm != null && !miniForm.IsDisposed) { miniForm.Activate(); return; }
            Form f = new Form();
            miniForm = f;
            f.FormBorderStyle = FormBorderStyle.None;
            f.StartPosition = FormStartPosition.Manual;
            f.ShowInTaskbar = false;
            f.TopMost = true;
            f.BackColor = BgPanel;
            f.ClientSize = new Size(380, 150);
            Rectangle wa = Screen.FromControl(this).WorkingArea;
            f.Location = new Point(wa.Right - 380 - 16, wa.Bottom - 150 - 16);
            f.Paint += delegate(object s, PaintEventArgs e)
            {
                using (Pen pn = new Pen(Color.FromArgb(62, 62, 62))) e.Graphics.DrawRectangle(pn, 0, 0, f.Width - 1, f.Height - 1);
            };
            MouseEventHandler drag = delegate(object s, MouseEventArgs e)
            {
                if (e.Button == MouseButtons.Left) { Native.ReleaseCapture(); Native.SendMessageI(f.Handle, 0xA1, 2, 0); }
            };
            f.MouseDown += drag;

            ThumbBox cover = new ThumbBox();
            cover.Fit = true;
            cover.BackColor = BgPanel;
            cover.SetBounds(14, 14, 96, 54);
            cover.MouseDown += drag;
            Label lt = new Label();
            lt.Font = fontBold; lt.ForeColor = Color.White; lt.BackColor = BgPanel; lt.AutoEllipsis = true;
            lt.SetBounds(122, 14, 190, 24);
            lt.MouseDown += drag;
            Label ls = new Label();
            ls.Font = fontSmall; ls.ForeColor = Dim; ls.BackColor = BgPanel; ls.AutoEllipsis = true;
            ls.SetBounds(122, 40, 190, 20);
            ls.MouseDown += drag;

            IconButton bPin = MakeIcon("📌", new Size(28, 28), Accent);
            bPin.Font = new Font("Segoe UI Emoji", 10f);
            bPin.Location = new Point(316, 8);
            bPin.Click += delegate { f.TopMost = !f.TopMost; bPin.ForeColor = f.TopMost ? Accent : Dim; bPin.Invalidate(); };
            IconButton bExp = MakeIcon("⤢", new Size(28, 28), Color.White);
            bExp.Location = new Point(346, 8);
            bExp.Click += delegate { f.Close(); };

            Label lp = new Label();
            lp.ForeColor = Dim; lp.BackColor = BgPanel; lp.TextAlign = ContentAlignment.TopRight; lp.Font = fontSmall;
            lp.SetBounds(6, 82, 44, 18);
            Label ld = new Label();
            ld.ForeColor = Dim; ld.BackColor = BgPanel; ld.TextAlign = ContentAlignment.TopLeft; ld.Font = fontSmall;
            ld.SetBounds(330, 82, 44, 18);
            DarkSlider sl = new DarkSlider();
            sl.BackColor = BgPanel;
            sl.SetBounds(54, 80, 272, 20);
            sl.Minimum = 0; sl.Maximum = 1;
            sl.Committed += delegate { try { wmp.controls.currentPosition = (double)sl.Value; } catch (Exception) { } };

            IconButton mPrev = MakeIcon("", new Size(36, 36), Color.White);
            mPrev.Glyph = "prev"; mPrev.SetBounds(124, 106, 36, 36);
            mPrev.Click += delegate { Prev(); };
            IconButton mPlay = MakeIcon("", new Size(40, 40), Color.White);
            mPlay.Filled = true; mPlay.BackColor = Accent; mPlay.Glyph = "play"; mPlay.SetBounds(170, 104, 40, 40);
            mPlay.Click += delegate { TogglePlay(); };
            IconButton mNext = MakeIcon("", new Size(36, 36), Color.White);
            mNext.Glyph = "next"; mNext.SetBounds(220, 106, 36, 36);
            mNext.Click += delegate { Next(false); };

            f.Controls.Add(cover); f.Controls.Add(lt); f.Controls.Add(ls);
            f.Controls.Add(bPin); f.Controls.Add(bExp);
            f.Controls.Add(lp); f.Controls.Add(ld); f.Controls.Add(sl);
            f.Controls.Add(mPrev); f.Controls.Add(mPlay); f.Controls.Add(mNext);

            System.Windows.Forms.Timer mt = new System.Windows.Forms.Timer();
            mt.Interval = 300;
            mt.Tick += delegate
            {
                if (nowTrack != null)
                {
                    if (lt.Text != nowTrack.Title) { lt.Text = nowTrack.Title; cover.Thumb = GetThumb(nowTrack.Id); cover.Invalidate(); }
                    if (cover.Thumb == null) { cover.Thumb = GetThumb(nowTrack.Id); cover.Invalidate(); }
                }
                if (ls.Text != lblSub.Text) ls.Text = lblSub.Text;
                if (sl.Maximum != trkSeek.Maximum) sl.Maximum = trkSeek.Maximum;
                if (!sl.Dragging) { sl.Value = trkSeek.Value; lp.Text = lblPos.Text; }
                ld.Text = lblDur.Text;
                if (mPlay.Glyph != btnPlay.Glyph) { mPlay.Glyph = btnPlay.Glyph; mPlay.Invalidate(); }
            };
            mt.Start();
            f.FormClosed += delegate
            {
                mt.Stop(); mt.Dispose();
                miniForm = null;
                if (!reallyExit && !IsDisposed) ShowMain();
            };
            f.Show();
            Hide();
        }

        // ================= letra (LRCLIB) =================

        IconButton MakeLetra()
        {
            IconButton b = new IconButton();
            b.Text = "Letra";
            b.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
            b.Size = new Size(70, 26);
            b.BackColor = BgPanel;
            b.Radius = 13;
            b.Outline = true;
            b.Anchor = AnchorStyles.None;
            return b;
        }

        IconButton MakeMiniBtn()
        {
            IconButton b = new IconButton();
            b.Size = new Size(40, 40);
            b.BackColor = BgPanel;
            b.ForeColor = Dim;
            b.Glyph = "mini";
            b.Anchor = AnchorStyles.None;
            return b;
        }

        IconButton MakeAddBtn()
        {
            IconButton b = new IconButton();
            b.Glyph = "plus";
            b.Size = new Size(30, 30);
            b.BackColor = BgPanel;
            b.ForeColor = Dim;
            b.Radius = 14;
            b.Outline = true;
            b.OutlineColor = Color.FromArgb(95, 95, 95);
            b.Anchor = AnchorStyles.None;
            return b;
        }

        void UpdateLetraButtons()
        {
            IconButton[] bs = new IconButton[] { btnLetra, bLetra2 };
            foreach (IconButton b in bs)
            {
                if (b == null) continue;
                b.Enabled = lyricsAvail;
                if (!lyricsAvail) { b.Filled = false; b.ForeColor = Color.FromArgb(75, 75, 75); b.OutlineColor = Color.FromArgb(42, 42, 42); }
                else if (lyricsOpen) { b.Filled = true; b.BackColor = Color.FromArgb(61, 33, 33); b.ForeColor = Accent; b.OutlineColor = Accent; }
                else { b.Filled = false; b.BackColor = BgPanel; b.ForeColor = Dim; b.OutlineColor = Color.FromArgb(62, 62, 62); }
                b.Invalidate();
            }
        }

        void ToggleLyrics()
        {
            if (!lyricsAvail) return;
            lyricsOpen = !lyricsOpen;
            UpdateLetraButtons();
            if (lyricsOpen && !nowShown) ShowNow();
            else if (nowShown) ApplyLyricsMode();
        }

        void ApplyLyricsMode()
        {
            if (lyricsView == null) return;
            bool show = lyricsOpen && lyricsAvail && nowShown;
            lyricsView.Visible = show;
            picBig.Visible = !show;
            if (show) lyricsView.BringToFront();
        }

        static int ParseSecs(string d)
        {
            if (d == null || d.Length == 0) return 0;
            string[] p = d.Split(':');
            int tot = 0;
            foreach (string x in p) { int v; if (!int.TryParse(x, out v)) return 0; tot = tot * 60 + v; }
            return tot;
        }

        static bool LyricsTitleOk(string title)
        {
            return !Regex.IsMatch(title ?? "", @"\b(live|ao vivo|remix|sped\s*up|speed\s*up|slowed|reverb|nightcore|cover|acoustic|ac[uú]stic[oa]?|8d|instrumental|karaoke|mashup|bass boosted|reaction)\b", RegexOptions.IgnoreCase);
        }

        static bool SplitTitle(Track t, out string artist, out string name)
        {
            artist = ""; name = "";
            string ti = t.Title ?? "";
            ti = Regex.Replace(ti, @"\s*[\(\[][^\)\]]*[\)\]]", "");
            ti = Regex.Replace(ti, @"\b(official\s*(music\s*)?video|official\s*audio|lyrics?\s*video|lyric\s*video|lyrics|v[ií]deo\s*oficial|clipe\s*oficial|HD|4K)\b", "", RegexOptions.IgnoreCase);
            ti = ti.Trim(' ', '-', '|', '–');
            int d = ti.IndexOf(" - ");
            if (d > 0) { artist = ti.Substring(0, d).Trim(); name = ti.Substring(d + 3).Trim(); }
            else
            {
                artist = Regex.Replace(t.Channel ?? "", @"(\s*-\s*Topic|VEVO)$", "", RegexOptions.IgnoreCase).Trim();
                name = ti;
            }
            name = Regex.Replace(name, @"\s+(feat\.?|ft\.?)\s.*$", "", RegexOptions.IgnoreCase).Trim();
            return artist.Length > 0 && name.Length > 0;
        }

        static string JsonStr(string seg, string key)
        {
            Match m = Regex.Match(seg, "\"" + key + "\"\\s*:\\s*(null|\"((?:[^\"\\\\]|\\\\.)*)\")", RegexOptions.Singleline);
            if (!m.Success || !m.Groups[2].Success) return null;
            try { return Regex.Unescape(m.Groups[2].Value); } catch (Exception) { return m.Groups[2].Value; }
        }

        static double JsonNum(string seg, string key)
        {
            Match m = Regex.Match(seg, "\"" + key + "\"\\s*:\\s*([0-9.]+)");
            double v;
            if (m.Success && double.TryParse(m.Groups[1].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v)) return v;
            return -1;
        }

        static string HttpGet(string url)
        {
            using (WebClient wc = new WebClient())
            {
                wc.Encoding = Encoding.UTF8;
                wc.Headers[HttpRequestHeader.UserAgent] = "Beat/" + AppVersion;
                return wc.DownloadString(url);
            }
        }

        // so aceita a letra se a duracao bater (+-2 s) com a do video: assim a letra fica no tempo certo
        static void FindLyrics(string artist, string name, int sec, out string lrc, out string plain)
        {
            lrc = null; plain = null;
            string a = Uri.EscapeDataString(artist), n = Uri.EscapeDataString(name);
            try
            {
                string j = HttpGet("https://lrclib.net/api/get?artist_name=" + a + "&track_name=" + n + "&duration=" + sec);
                double d = JsonNum(j, "duration");
                if (d > 0 && Math.Abs(d - sec) <= 2)
                {
                    lrc = JsonStr(j, "syncedLyrics");
                    plain = JsonStr(j, "plainLyrics");
                    if ((lrc != null && lrc.Length > 0) || (plain != null && plain.Length > 0)) return;
                }
            }
            catch (Exception) { }
            lrc = null; plain = null;
            try
            {
                string j = HttpGet("https://lrclib.net/api/search?track_name=" + n + "&artist_name=" + a);
                string[] segs = j.Split(new string[] { "\"id\":" }, StringSplitOptions.RemoveEmptyEntries);
                string bestPlain = null;
                foreach (string seg in segs)
                {
                    double d = JsonNum(seg, "duration");
                    if (d < 0 || Math.Abs(d - sec) > 2) continue;
                    string sl = JsonStr(seg, "syncedLyrics");
                    if (sl != null && sl.Length > 0) { lrc = sl; return; }
                    string pl = JsonStr(seg, "plainLyrics");
                    if (bestPlain == null && pl != null && pl.Length > 0) bestPlain = pl;
                }
                plain = bestPlain;
            }
            catch (Exception) { }
        }

        void FetchLyrics(Track t)
        {
            lyricsAvail = false;
            lyricsView.SetData(new string[0], new double[0], true);
            UpdateLetraButtons();
            if (nowShown) ApplyLyricsMode();
            int my = ++lyricsToken;
            int sec = ParseSecs(t.Duration);
            string artist, name;
            if (sec <= 0 || !LyricsTitleOk(t.Title) || !SplitTitle(t, out artist, out name)) return;
            ThreadPool.QueueUserWorkItem(delegate
            {
                string lrc = null, plain = null;
                try { FindLyrics(artist, name, sec, out lrc, out plain); } catch (Exception) { }
                UI(delegate { if (my == lyricsToken) ApplyLyrics(lrc, plain); });
            });
        }

        void ApplyLyrics(string lrc, string plain)
        {
            List<KeyValuePair<double, string>> items = new List<KeyValuePair<double, string>>();
            if (lrc != null && lrc.Length > 0)
            {
                foreach (string line in lrc.Split('\n'))
                {
                    MatchCollection ms = Regex.Matches(line, @"\[(\d+):(\d+(?:\.\d+)?)\]");
                    if (ms.Count == 0) continue;
                    string text = Regex.Replace(line, @"\[[^\]]*\]", "").Trim();
                    if (text.Length == 0) continue;
                    foreach (Match m in ms)
                    {
                        double tm = int.Parse(m.Groups[1].Value) * 60.0 + double.Parse(m.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture);
                        items.Add(new KeyValuePair<double, string>(tm, text));
                    }
                }
                items.Sort(delegate(KeyValuePair<double, string> x, KeyValuePair<double, string> y) { return x.Key.CompareTo(y.Key); });
            }
            if (items.Count >= 3)
            {
                string[] ls = new string[items.Count];
                double[] ts = new double[items.Count];
                for (int i = 0; i < items.Count; i++) { ls[i] = items[i].Value; ts[i] = items[i].Key; }
                lyricsView.SetData(ls, ts, true);
                lyricsAvail = true;
            }
            else if (plain != null && plain.Trim().Length > 0)
            {
                List<string> pl = new List<string>();
                foreach (string l in plain.Replace("\r", "").Split('\n')) pl.Add(l.Trim());
                lyricsView.SetData(pl.ToArray(), new double[0], false);
                lyricsAvail = true;
            }
            UpdateLetraButtons();
            if (nowShown) ApplyLyricsMode();
        }

        // ================= botao "+" (adicionar a playlist) =================

        void HideAddPop()
        {
            if (addPop != null && addPop.Visible) addPop.Visible = false;
        }

        Panel PopRow(int y, int w, Bitmap thumb, bool plus, string name, string sub, bool check, EventHandler click)
        {
            Panel r = new Panel();
            r.SetBounds(0, y, w, 48);
            r.BackColor = Color.FromArgb(30, 30, 30);
            r.Cursor = Cursors.Hand;
            r.Paint += delegate(object s, PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                Rectangle tr = new Rectangle(12, 6, 36, 36);
                if (plus)
                {
                    using (GraphicsPath gp = Gfx.RoundRect(new Rectangle(tr.X, tr.Y, tr.Width - 1, tr.Height - 1), 6))
                    using (Pen pn = new Pen(Color.FromArgb(95, 95, 95))) g.DrawPath(pn, gp);
                    TextRenderer.DrawText(g, "+", new Font("Segoe UI", 14f), tr, Dim, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                }
                else Gfx.DrawThumb(g, tr, thumb, 6);
                TextRenderer.DrawText(g, name, fontBold, new Rectangle(60, 5, r.Width - 60 - 40, 22), Color.White, TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.Left);
                if (sub != null) TextRenderer.DrawText(g, sub, fontSmall, new Rectangle(60, 26, r.Width - 60 - 40, 18), Dim, TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.Left);
                if (check) TextRenderer.DrawText(g, "✓", new Font("Segoe UI Symbol", 12f, FontStyle.Bold), new Rectangle(r.Width - 38, 0, 30, 48), Accent, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            };
            r.MouseEnter += delegate { r.BackColor = Color.FromArgb(44, 44, 44); };
            r.MouseLeave += delegate { r.BackColor = Color.FromArgb(30, 30, 30); };
            r.Click += click;
            return r;
        }

        void ShowAddPop(Control anchor, bool force)
        {
            if (nowTrack == null) return;
            if (!force && addPop != null && addPop.Visible) { HideAddPop(); return; }
            Track t = nowTrack;
            if (addPop == null)
            {
                addPop = new RoundPanel();
                addPop.FillColor = Color.FromArgb(30, 30, 30);
                addPop.BorderColor = Color.FromArgb(60, 60, 60);
                addPop.Radius = 12;
                addPop.Visible = false;
                Controls.Add(addPop);
            }
            List<Control> old = new List<Control>();
            foreach (Control c in addPop.Controls) old.Add(c);
            addPop.Controls.Clear();
            foreach (Control c in old) c.Dispose();

            int w = 320;
            int nrows = Math.Min(playlists.Count, 6);
            int h = 44 + 48 + nrows * 48 + 10;
            addPop.Controls.Add(SetLabel("Adicionar à playlist", 16, 12, w - 32, fontBold, Color.White));
            addPop.Controls.Add(PopRow(44, w, null, true, "Nova playlist", null, false, delegate { HideAddPop(); NewPlaylist(t); }));
            if (playlists.Count > 0)
            {
                Panel box = new Panel();
                box.SetBounds(0, 92, w - 2, nrows * 48);
                box.BackColor = Color.FromArgb(30, 30, 30);
                box.AutoScroll = playlists.Count > 6;
                int rw = playlists.Count > 6 ? w - 22 : w - 2;
                for (int i = 0; i < playlists.Count; i++)
                {
                    Playlist pl = playlists[i];
                    bool has = false;
                    foreach (Track xt in pl.Items) if (xt.Id == t.Id) { has = true; break; }
                    Playlist pp = pl;
                    bool hh = has;
                    box.Controls.Add(PopRow(i * 48, rw, PlaylistThumb(pl), false, pl.Name,
                        pl.Items.Count + (pl.Items.Count == 1 ? " música" : " músicas"), has,
                        delegate
                        {
                            if (hh) { for (int k = pp.Items.Count - 1; k >= 0; k--) if (pp.Items[k].Id == t.Id) pp.Items.RemoveAt(k); Save(); if (view == 2) RefreshList(); }
                            else AddToPlaylist(pp, t);
                            BeginInvoke((MethodInvoker)delegate { ShowAddPop(anchor, true); });
                        }));
                }
                addPop.Controls.Add(box);
            }
            addPop.Size = new Size(w, h);
            Point p = PointToClient(anchor.PointToScreen(new Point(anchor.Width / 2, 0)));
            int x = Math.Max(8, Math.Min(ClientSize.Width - w - 8, p.X - w + 40));
            int y = p.Y - h - 8;
            if (y < 8) y = p.Y + anchor.Height + 8;
            addPop.Location = new Point(x, y);
            addPop.Visible = true;
            addPop.BringToFront();
        }

        void MovePlaylistItem(int from, int to)
        {
            Playlist pl = CurrentPlaylist();
            if (pl == null || from < 0 || from >= pl.Items.Count || to < 0 || to >= pl.Items.Count) return;
            Track t = pl.Items[from];
            pl.Items.RemoveAt(from);
            pl.Items.Insert(to, t);
            Save();
            RefreshList();
            lst.SelectedIndex = to;
        }

        // ================= configuracoes e backup =================

        Label SetLabel(string text, int x, int y, int w, Font f, Color c)
        {
            Label l = new Label();
            l.Text = text;
            l.Font = f;
            l.ForeColor = c;
            l.BackColor = Color.FromArgb(30, 30, 30);
            l.SetBounds(x, y, w, 24);
            l.TextAlign = ContentAlignment.MiddleLeft;
            return l;
        }

        void BuildSettings()
        {
            settingsPanel = new RoundPanel();
            settingsPanel.FillColor = Color.FromArgb(30, 30, 30);
            settingsPanel.BorderColor = Color.FromArgb(60, 60, 60);
            settingsPanel.Radius = 12;
            settingsPanel.Size = new Size(340, 270);
            settingsPanel.Visible = false;

            Font fl = new Font("Segoe UI", 10f);
            settingsPanel.Controls.Add(SetLabel("Configura\u00E7\u00F5es", 18, 14, 300, fontBold, Color.White));

            string[] names = new string[] { "Limpar hist\u00F3rico ao fechar o app", "Minimizar para a bandeja ao fechar", "Anima\u00E7\u00F5es" };
            ToggleSwitch[] tg = new ToggleSwitch[3];
            for (int i = 0; i < 3; i++)
            {
                int y = 54 + i * 40;
                settingsPanel.Controls.Add(SetLabel(names[i], 18, y, 250, fl, Color.FromArgb(225, 225, 225)));
                ToggleSwitch t = new ToggleSwitch();
                t.Location = new Point(340 - 18 - 42, y);
                t.Changed += delegate
                {
                    optClearHist = tgClear.Checked;
                    optTray = tgTray.Checked;
                    optAnim = tgAnim.Checked;
                    Save();
                };
                settingsPanel.Controls.Add(t);
                tg[i] = t;
            }
            tgClear = tg[0]; tgTray = tg[1]; tgAnim = tg[2];

            Panel div = new Panel();
            div.BackColor = Color.FromArgb(55, 55, 55);
            div.SetBounds(18, 182, 304, 1);
            settingsPanel.Controls.Add(div);

            settingsPanel.Controls.Add(SetLabel("Backup", 18, 192, 300, fontBold, Color.White));
            IconButton bx = MakeSmall("Exportar");
            bx.Dock = DockStyle.None;
            bx.ClearColor = Color.FromArgb(30, 30, 30);
            bx.SetBounds(18, 224, 144, 32);
            bx.Click += delegate { HideSettings(); ExportBackup(); };
            IconButton bi = MakeSmall("Importar");
            bi.Dock = DockStyle.None;
            bi.ClearColor = Color.FromArgb(30, 30, 30);
            bi.SetBounds(178, 224, 144, 32);
            bi.Click += delegate { HideSettings(); ImportBackup(); };
            settingsPanel.Controls.Add(bx);
            settingsPanel.Controls.Add(bi);

            Controls.Add(settingsPanel);
        }

        void ToggleSettings()
        {
            if (settingsPanel.Visible) { HideSettings(); return; }
            tgClear.Checked = optClearHist;
            tgTray.Checked = optTray;
            tgAnim.Checked = optAnim;
            settingsPanel.Location = new Point(ClientSize.Width - settingsPanel.Width - 16, topPanel.Bottom - 4);
            settingsPanel.Visible = true;
            settingsPanel.BringToFront();
        }

        void HideSettings()
        {
            if (settingsPanel != null && settingsPanel.Visible) settingsPanel.Visible = false;
        }

        void ExportBackup()
        {
            SaveFileDialog d = new SaveFileDialog();
            d.Filter = "Backup do Beat (*.beat)|*.beat";
            d.FileName = "beat-backup-" + DateTime.Now.ToString("yyyy-MM-dd") + ".beat";
            if (d.ShowDialog(this) != DialogResult.OK) return;
            try
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("BEAT1");
                foreach (Track t in favorites) sb.AppendLine("F\t" + TrackLine(t));
                foreach (Playlist p in playlists)
                {
                    string cv = (p.Cover != null && p.Cover.StartsWith("t:")) ? p.Cover : "";
                    sb.AppendLine("P\t" + Clean(p.Name) + "\t" + cv);
                    foreach (Track t in p.Items) sb.AppendLine("T\t" + TrackLine(t));
                }
                File.WriteAllText(d.FileName, sb.ToString(), Encoding.UTF8);
                SetInfo("Backup salvo.");
            }
            catch (Exception ex) { MessageBox.Show(this, "N\u00E3o foi poss\u00EDvel salvar: " + ex.Message); }
        }

        void ImportBackup()
        {
            OpenFileDialog d = new OpenFileDialog();
            d.Filter = "Backup do Beat (*.beat)|*.beat";
            if (d.ShowDialog(this) != DialogResult.OK) return;
            List<Track> nf = new List<Track>();
            List<Playlist> np = new List<Playlist>();
            try
            {
                string[] lines = File.ReadAllLines(d.FileName, Encoding.UTF8);
                if (lines.Length == 0 || lines[0].Trim() != "BEAT1") { MessageBox.Show(this, "Este arquivo n\u00E3o parece ser um backup do Beat."); return; }
                Playlist cur = null;
                foreach (string line in lines)
                {
                    string[] p = line.Split('\t');
                    if (p.Length < 2) continue;
                    if (p[0] == "F") { Track t = ParseTrack(p, 1); if (t != null) nf.Add(t); }
                    else if (p[0] == "P") { cur = new Playlist(); cur.Name = p[1]; if (p.Length > 2 && p[2].StartsWith("t:")) cur.Cover = p[2]; np.Add(cur); }
                    else if (p[0] == "T") { Track t = ParseTrack(p, 1); if (t != null && cur != null) cur.Items.Add(t); }
                }
            }
            catch (Exception ex) { MessageBox.Show(this, "N\u00E3o foi poss\u00EDvel ler o arquivo: " + ex.Message); return; }

            string msg = "O backup tem " + nf.Count + " favoritos e " + np.Count + " playlists.\r\n\r\nSubstituir tudo apaga o que voc\u00EA tem agora. Juntar mant\u00E9m o atual e acrescenta o que faltar.";
            int r = Prompt.Choose(this, "Importar backup", msg, "Substituir tudo", "Juntar");
            if (r == 0) return;
            if (r == 1)
            {
                favorites.Clear(); favorites.AddRange(nf);
                playlists.Clear(); playlists.AddRange(np);
                plOpen = -1;
            }
            else
            {
                foreach (Track t in nf) if (!IsFav(t.Id)) favorites.Add(t);
                foreach (Playlist q in np)
                {
                    Playlist ex = null;
                    foreach (Playlist e2 in playlists) if (string.Equals(e2.Name, q.Name, StringComparison.OrdinalIgnoreCase)) { ex = e2; break; }
                    if (ex == null) { playlists.Add(q); continue; }
                    foreach (Track t in q.Items)
                    {
                        bool has = false;
                        foreach (Track y in ex.Items) if (y.Id == t.Id) { has = true; break; }
                        if (!has) ex.Items.Add(t);
                    }
                }
            }
            Save();
            SetView(view);
            SetInfo("Backup importado.");
        }

        // ================= tela inicial =================

        void BuildHomePanel()
        {
            homePanel = new ScrollPanel();
            homePanel.Dock = DockStyle.Fill;
            homePanel.BackColor = BgMain;
            homePanel.Visible = false;
            homePanel.HandleCreated += delegate { Native.DarkScroll(homePanel.Handle); };
            homePanel.SizeChanged += delegate { if (homePanel.Visible) RefreshHome(); };
            Controls.Add(homePanel);
        }

        static string Greeting()
        {
            int h = DateTime.Now.Hour;
            if (h >= 5 && h < 12) return "Bom dia";
            if (h >= 12 && h < 18) return "Boa tarde";
            return "Boa noite";
        }

        Label HomeLabel(string text, Font f, Color fc, Color bc, int x, int y, int w, int h)
        {
            Label l = new Label();
            l.Text = text;
            l.Font = f;
            l.ForeColor = fc;
            l.BackColor = bc;
            l.AutoEllipsis = true;
            l.SetBounds(x, y, w, h);
            return l;
        }

        int HomeSection(string text, int x, int y, int w)
        {
            homePanel.Controls.Add(HomeLabel(text, fHomeSec, Color.White, BgMain, x, y, w, 26));
            return y + 32;
        }

        void HomeFocus(object sender, EventArgs e)
        {
            if (homePanel.Visible && !(txtSearch.Focused && txtSearch.Text.Length > 0)) homePanel.Focus();
        }

        void ScrollHome(int delta)
        {
            if (homePanel == null || !homePanel.Visible) return;
            int cur = -homePanel.AutoScrollPosition.Y;
            homePanel.AutoScrollPosition = new Point(0, Math.Max(0, cur - delta));
        }

        void AttachHomeFocus(Control c)
        {
            c.MouseEnter += HomeFocus;
            foreach (Control ch in c.Controls) AttachHomeFocus(ch);
        }

        void RefreshHome()
        {
            if (homeBuilding || homePanel == null) return;
            homeBuilding = true;
            try
            {
                int keepY = -homePanel.AutoScrollPosition.Y;
                homePanel.AutoScrollPosition = new Point(0, 0);
                homePanel.SuspendLayout();
                List<Control> old = new List<Control>();
                foreach (Control c in homePanel.Controls) old.Add(c);
                homePanel.Controls.Clear();
                foreach (Control c in old) c.Dispose();

                int W = Math.Max(320, homePanel.ClientSize.Width);
                int x0 = 24, y = 20;
                int cw = W - 2 * x0;
                bool any = false;

                homePanel.Controls.Add(HomeLabel(Greeting(), fHomeBig, Color.White, BgMain, x0, y, cw, 40));
                y += 56;

                // continuar ouvindo
                if (lastPlayed != null)
                {
                    any = true;
                    y = HomeSection("Continuar ouvindo", x0, y, cw);
                    Track lp = lastPlayed;
                    RoundPanel card = new RoundPanel();
                    card.FillColor = BgPanel;
                    card.Radius = 12;
                    card.SetBounds(x0, y, cw, 86);

                    ThumbBox tb = new ThumbBox();
                    tb.Fit = true;
                    tb.BackColor = BgPanel;
                    tb.Cursor = Cursors.Hand;
                    tb.SetBounds(12, 10, 116, 65);
                    tb.Thumb = GetThumb(lp.Id);
                    tb.Click += delegate { OpenFromHome(lp); };

                    card.Controls.Add(tb);
                    card.Controls.Add(HomeLabel(lp.Title, fontBold, Color.White, BgPanel, 142, 18, cw - 142 - 76, 24));
                    card.Controls.Add(HomeLabel(lp.Channel, fontSmall, Dim, BgPanel, 142, 44, cw - 142 - 76, 20));

                    IconButton pb = MakeIcon("", new Size(44, 44), Color.White);
                    pb.Filled = true;
                    pb.BackColor = Accent;
                    pb.Glyph = (isPlaying && nowTrack != null && nowTrack.Id == lp.Id) ? "pause" : "play";
                    pb.ClearColor = BgPanel;
                    pb.Location = new Point(cw - 44 - 16, 21);
                    pb.Click += delegate { PlayPauseHome(lp); };
                    card.Controls.Add(pb);

                    homePanel.Controls.Add(card);
                    y += 86 + 28;
                }

                // playlists
                if (playlists.Count > 0)
                {
                    any = true;
                    y = HomeSection("Suas playlists", x0, y, cw);
                    int pw = 196, ph = 158, pg = 12;
                    int cols = Math.Max(1, (cw + pg) / (pw + pg));
                    for (int i = 0; i < playlists.Count; i++)
                    {
                        Playlist pl = playlists[i];
                        int pidx = i;
                        int col = i % cols, row = i / cols;
                        RoundPanel pc = new RoundPanel();
                        pc.FillColor = BgPanel;
                        pc.Radius = 12;
                        pc.Cursor = Cursors.Hand;
                        pc.SetBounds(x0 + col * (pw + pg), y + row * (ph + pg), pw, ph);

                        ThumbBox ptb = new ThumbBox();
                        ptb.Fit = true;
                        ptb.BackColor = BgPanel;
                        ptb.Cursor = Cursors.Hand;
                        ptb.SetBounds(12, 12, pw - 24, 90);
                        ptb.Thumb = PlaylistThumb(pl);

                        Label pn = HomeLabel(pl.Name, fontBold, Color.White, BgPanel, 12, 108, pw - 24, 22);
                        Label pcnt = HomeLabel(pl.Items.Count + (pl.Items.Count == 1 ? " música" : " músicas"), fontSmall, Dim, BgPanel, 12, 130, pw - 24, 20);
                        pn.Cursor = Cursors.Hand;
                        pcnt.Cursor = Cursors.Hand;

                        EventHandler open = delegate { OpenPlaylist(pidx); };
                        pc.Click += open; ptb.Click += open; pn.Click += open; pcnt.Click += open;
                        pc.Controls.Add(ptb);
                        pc.Controls.Add(pn);
                        pc.Controls.Add(pcnt);
                        homePanel.Controls.Add(pc);
                    }
                    int prows = (playlists.Count + cols - 1) / cols;
                    y += prows * (ph + pg) + 16;
                }

                // favoritas (as mais recentes primeiro, ate 2 linhas)
                if (favorites.Count > 0)
                {
                    any = true;
                    y = HomeSection("Favoritas", x0, y, cw);
                    int fw = 150, fh = 84, fg = 12, flh = 24;
                    int fcols = Math.Max(1, (cw + fg) / (fw + fg));
                    int fn = Math.Min(favorites.Count, fcols * 2);
                    for (int i = 0; i < fn; i++)
                    {
                        int fidx = favorites.Count - 1 - i;
                        Track ft = favorites[fidx];
                        int col = i % fcols, row = i / fcols;
                        int fx = x0 + col * (fw + fg), fy = y + row * (fh + flh + fg);

                        ThumbBox ftb = new ThumbBox();
                        ftb.Fit = true;
                        ftb.BackColor = BgMain;
                        ftb.Cursor = Cursors.Hand;
                        ftb.SetBounds(fx, fy, fw, fh);
                        ftb.Thumb = GetThumb(ft.Id);
                        Label fl = HomeLabel(ft.Title, fontSmall, Dim, BgMain, fx, fy + fh + 3, fw, 20);
                        fl.Cursor = Cursors.Hand;
                        EventHandler playFav = delegate { PlayFavorite(fidx); };
                        ftb.Click += playFav; fl.Click += playFav;
                        homePanel.Controls.Add(ftb);
                        homePanel.Controls.Add(fl);
                    }
                    int frows = (fn + fcols - 1) / fcols;
                    y += frows * (fh + flh + fg) + 12;
                }

                // buscas recentes
                if (history.Count > 0)
                {
                    any = true;
                    y = HomeSection("Buscas recentes", x0, y, cw);
                    int ccx = x0;
                    foreach (string hs in history)
                    {
                        int chw = TextRenderer.MeasureText(hs, fHomeChip).Width + 30;
                        if (ccx + chw > x0 + cw && ccx > x0) { ccx = x0; y += 38; }
                        IconButton chip = new IconButton();
                        chip.Text = hs;
                        chip.Font = fHomeChip;
                        chip.Filled = true;
                        chip.Radius = 15;
                        chip.BackColor = Color.FromArgb(38, 38, 38);
                        chip.ForeColor = Color.FromArgb(221, 221, 221);
                        chip.SetBounds(ccx, y, chw, 30);
                        string hq = hs;
                        chip.Click += delegate
                        {
                            histSuppress = true;
                            txtSearch.Text = hq;
                            histSuppress = false;
                            DoSearch();
                        };
                        homePanel.Controls.Add(chip);
                        ccx += chw + 8;
                    }
                    y += 38 + 12;
                }

                if (!any)
                {
                    homePanel.Controls.Add(HomeLabel("Pesquise uma música na barra acima para começar.", Font, Dim, BgMain, x0, y, cw, 24));
                    y += 40;
                }

                foreach (Control c in homePanel.Controls) AttachHomeFocus(c);
                homePanel.AutoScrollMinSize = new Size(0, y + 24);
                homePanel.ResumeLayout();
                homePanel.AutoScrollPosition = new Point(0, keepY);
            }
            finally { homeBuilding = false; }
        }

        void OpenFromHome(Track t)
        {
            if (t == null) return;
            if (nowTrack == null || nowTrack.Id != t.Id) StartSingle(t);
            ShowNow();
        }

        void StartSingle(Track t)
        {
            queue = new List<Track>();
            queue.Add(t.Copy());
            queueSource = null;
            qIndex = 0;
            PlayCurrent();
        }

        void PlayPauseHome(Track t)
        {
            if (nowTrack != null && nowTrack.Id == t.Id) TogglePlay(); else StartSingle(t);
            RefreshHome();
        }

        void PlayFavorite(int idx)
        {
            PlayList(favorites, idx);
            ShowNow();
        }

        void OpenPlaylist(int idx)
        {
            plOpen = (idx >= 0 && idx < playlists.Count) ? idx : -1;
            SetView(2);
        }

        // ================= historico de pesquisa =================

        void AddHistory(string q)
        {
            if (q == null || q.Length == 0) return;
            for (int i = history.Count - 1; i >= 0; i--)
                if (string.Equals(history[i], q, StringComparison.OrdinalIgnoreCase)) history.RemoveAt(i);
            history.Insert(0, q);
            while (history.Count > 10) { histCover.Remove(history[history.Count - 1]); history.RemoveAt(history.Count - 1); }
        }

        void ShowHist()
        {
            string f = txtSearch.Text.Trim();
            histBox.Items.Clear();
            foreach (string h in history)
            {
                if (f.Length > 0 && h.IndexOf(f, StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (string.Equals(h, f, StringComparison.OrdinalIgnoreCase)) continue;
                histBox.Items.Add(h);
            }
            if (histBox.Items.Count == 0) { HideHist(); return; }
            if (f.Length == 0) histBox.Items.Add(ClearTag);
            int hh = 2;
            for (int i = 0; i < histBox.Items.Count; i++) hh += HistItemHeight(i);
            Control host = txtSearch.Parent;
            Point p = PointToClient(host.PointToScreen(new Point(0, host.Height + 4)));
            histBox.SetBounds(p.X, p.Y, host.Width, hh);
            histBox.SelectedIndex = -1;
            histBox.Visible = true;
            histBox.BringToFront();
        }

        int HistItemHeight(int i)
        {
            if (i >= 0 && i < histBox.Items.Count && (string)histBox.Items[i] == ClearTag) return 36;
            return 48;
        }

        void histBox_MeasureItem(object sender, MeasureItemEventArgs e)
        {
            e.ItemHeight = HistItemHeight(e.Index);
        }

        void DeleteHistItem(string hs)
        {
            for (int hk = history.Count - 1; hk >= 0; hk--)
                if (string.Equals(history[hk], hs, StringComparison.OrdinalIgnoreCase)) history.RemoveAt(hk);
            histCover.Remove(hs);
            Save();
            ShowHist();
            if (histBox.Visible) histBox.Focus(); else txtSearch.Focus();
            if (view == 3 && !nowShown) RefreshHome();
        }

        void ClearHistory()
        {
            history.Clear();
            histCover.Clear();
            Save();
            HideHist();
            txtSearch.Focus();
            if (view == 3 && !nowShown) RefreshHome();
        }

        void HideHist()
        {
            if (histBox != null && histBox.Visible) histBox.Visible = false;
        }

        void PickHist(int i)
        {
            if (i < 0 || i >= histBox.Items.Count) return;
            string q = (string)histBox.Items[i];
            if (q == ClearTag) { ClearHistory(); return; }
            histSuppress = true;
            txtSearch.Text = q;
            histSuppress = false;
            txtSearch.SelectionStart = q.Length;
            HideHist();
            txtSearch.Focus();
            DoSearch();
        }

        void histBox_DrawItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= histBox.Items.Count) return;
            bool sel = (e.State & DrawItemState.Selected) != 0;
            using (SolidBrush b = new SolidBrush(sel ? BgSel : Color.FromArgb(34, 34, 34)))
                e.Graphics.FillRectangle(b, e.Bounds);
            string text = (string)histBox.Items[e.Index];
            if (text == ClearTag)
            {
                TextRenderer.DrawText(e.Graphics, "Limpar hist\u00F3rico", fHistClear, e.Bounds, Color.FromArgb(150, 150, 150),
                    TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPrefix);
                return;
            }
            Rectangle tr = new Rectangle(e.Bounds.X + 12, e.Bounds.Y + (e.Bounds.Height - 36) / 2, 64, 36);
            string cid;
            Bitmap cb = null;
            if (histCover.TryGetValue(text, out cid)) cb = GetThumb(cid);
            if (cb != null)
            {
                GraphicsState gs = e.Graphics.Save();
                e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                e.Graphics.DrawImage(cb, tr);
                e.Graphics.Restore(gs);
            }
            Rectangle r = new Rectangle(tr.Right + 14, e.Bounds.Y, e.Bounds.Width - tr.Right - 14 - 52, e.Bounds.Height);
            TextRenderer.DrawText(e.Graphics, text, histBox.Font, r, Color.White,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            Rectangle xr = new Rectangle(e.Bounds.Right - 48, e.Bounds.Y, 48, e.Bounds.Height);
            TextRenderer.DrawText(e.Graphics, "\u2715", fHistX, xr, sel ? Color.FromArgb(220, 220, 220) : Color.FromArgb(119, 119, 119),
                TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPrefix);
        }

        // ================= busca =================

        void DoSearch()
        {
            string q = txtSearch.Text.Trim().Replace("\"", " ").Replace("\\", " ");
            if (q.Length == 0) return;
            string lv, ll;
            if (ParseYtLink(q, out lv, out ll)) { DoLink(lv, ll); return; }
            linkShown = false;
            lastQuery = txtSearch.Text.Trim();
            string histKey = lastQuery;
            AddHistory(lastQuery);
            HideHist();
            Save();
            int my = ++searchToken;
            if (view != 0) SetView(0);
            lblInfo.Text = "Buscando...";
            btnSearch.Enabled = false;

            ThreadPool.QueueUserWorkItem(delegate
            {
                ytReady.WaitOne();
                string err = "";
                List<Track> res = new List<Track>();
                try
                {
                    string args = "--encoding utf-8 --flat-playlist --no-warnings --print \"%(id)s|||%(title)s|||%(duration_string)s|||%(channel)s\" \"ytsearch25:" + q + "\"";
                    string outp = Run(ytdlp, args, 60000, out err);
                    foreach (string line in outp.Split('\n'))
                    {
                        string l = line.Trim();
                        if (l.Length == 0) continue;
                        string[] p = l.Split(new string[] { "|||" }, StringSplitOptions.None);
                        if (p.Length < 4 || p[0].Length == 0) continue;
                        Track t = new Track();
                        t.Id = p[0];
                        t.Title = p[1];
                        t.Duration = p[2] == "NA" ? "" : p[2];
                        t.Channel = p[3] == "NA" ? "" : p[3];
                        res.Add(t);
                    }
                }
                catch (Exception ex) { err = ex.Message; }

                UI(delegate
                {
                    btnSearch.Enabled = true;
                    if (my != searchToken) return;
                    results = res;
                    resultsQuery = histKey;
                    SetView(0);
                    if (res.Count == 0) lblInfo.Text = "Nada encontrado. " + (err.Length > 0 ? Trunc(err.Trim(), 120) : "");
                    else
                    {
                        lst.Focus();
                        lst.SelectedIndex = 0;
                    }
                });
            });
        }

        // ================= vistas (busca / favoritos / playlists) =================

        static int TabOrder(int v) { return v == 3 ? 0 : v + 1; }

        Bitmap Snap(Rectangle area)
        {
            Bitmap b = new Bitmap(area.Width, area.Height);
            using (Graphics g = Graphics.FromImage(b))
                g.CopyFromScreen(PointToScreen(area.Location), Point.Empty, area.Size);
            return b;
        }

        // troca de aba com deslize (usa "fotos" da area de conteudo)
        void SetView(int v)
        {
            bool anim = optAnim && v != view && !nowShown && Visible && WindowState != FormWindowState.Minimized
                && slide == null && bottomPanel != null && topPanel != null && topPanel.Visible;
            Bitmap oldB = null;
            Rectangle area = Rectangle.Empty;
            if (anim)
            {
                HideHist();
                area = new Rectangle(0, topPanel.Bottom, ClientSize.Width, bottomPanel.Top - topPanel.Bottom);
                if (area.Height > 60 && area.Width > 100) { try { oldB = Snap(area); } catch (Exception) { oldB = null; } }
            }
            int from = view;
            SetViewCore(v);
            if (oldB == null) return;
            Bitmap newB = null;
            try { Refresh(); newB = Snap(area); } catch (Exception) { newB = null; }
            if (newB == null) { oldB.Dispose(); return; }
            slide = new SlideView();
            slide.Old = oldB; slide.New = newB;
            slide.FromRight = TabOrder(v) > TabOrder(from);
            slide.T = 0f;
            slide.Bounds = area;
            Controls.Add(slide);
            slide.BringToFront();
            slideSw = Stopwatch.StartNew();
            if (slideTimer == null)
            {
                slideTimer = new System.Windows.Forms.Timer();
                slideTimer.Interval = 15;
                slideTimer.Tick += delegate
                {
                    if (slide == null) { slideTimer.Stop(); return; }
                    float t = slideSw.ElapsedMilliseconds / 350f;
                    if (t >= 1f)
                    {
                        slideTimer.Stop();
                        Controls.Remove(slide);
                        slide.Old.Dispose(); slide.New.Dispose();
                        slide.Dispose();
                        slide = null;
                    }
                    else { slide.T = t; slide.Invalidate(); }
                };
            }
            slideTimer.Start();
        }

        void SetViewCore(int v)
        {
            int prevView = view;
            if (v != prevView && (v == 1 || prevView == 1))
            {
                plSuppress = true;
                txtPl.Text = "";
                plSuppress = false;
                if (txtPl.IsHandleCreated) Native.SendMessage(txtPl.Handle, 0x1501, (IntPtr)1, v == 1 ? "Buscar nos favoritos" : "Buscar playlist");
            }
            if (v != 0) linkShown = false;
            view = v;
            HideHist();
            tabSearch.Underline = v == 0; tabSearch.ForeColor = v == 0 ? Color.White : Dim;
            tabFav.Underline = v == 1; tabFav.ForeColor = v == 1 ? Color.White : Dim;
            tabPl.Underline = v == 2; tabPl.ForeColor = v == 2 ? Color.White : Dim;
            tabSearch.Invalidate(); tabFav.Invalidate(); tabPl.Invalidate();
            if (v == 2 && plOpen >= playlists.Count) plOpen = -1;
            tabHome.Underline = v == 3; tabHome.ForeColor = v == 3 ? Color.White : Dim; tabHome.Invalidate();
            if (v == 3)
            {
                viewList = results;
                ApplyView();
                RefreshHome();
                return;
            }
            ApplyView();

            if (v == 0) viewList = results;
            else if (v == 1) viewList = FavFiltered();
            else
            {
                Playlist p = CurrentPlaylist();
                viewList = p != null ? p.Items : new List<Track>();
                if (p == null) RefreshPlList();
            }
            lst.Items.Clear();
            RefreshList();
        }

        Playlist CurrentPlaylist()
        {
            if (plOpen < 0 || plOpen >= playlists.Count) return null;
            return playlists[plOpen];
        }

        List<Track> FavFiltered()
        {
            string f = txtPl != null ? txtPl.Text.Trim() : "";
            if (f.Length == 0) return favorites;
            List<Track> r = new List<Track>();
            foreach (Track t in favorites)
                if ((t.Title ?? "").IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0 || (t.Channel ?? "").IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0) r.Add(t);
            return r;
        }

        void RefreshList()
        {
            if (view == 1) viewList = FavFiltered();
            int top = lst.TopIndex;
            int sel = lst.SelectedIndex;
            lst.BeginUpdate();
            lst.Items.Clear();
            foreach (Track t in viewList) lst.Items.Add(t.Title);
            lst.EndUpdate();
            if (sel >= 0 && sel < lst.Items.Count) lst.SelectedIndex = sel;
            if (top >= 0 && top < lst.Items.Count) lst.TopIndex = top;

            if (view == 0)
                lblInfo.Text = results.Count > 0 ? results.Count + " resultados. De dois cliques para tocar; botao direito para mais opcoes." : "Digite uma musica ou artista e aperte Enter.";
            else if (view == 1)
                lblInfo.Text = favorites.Count > 0 ? (viewList.Count != favorites.Count ? "Favoritos (" + viewList.Count + " de " + favorites.Count + ")" : "Favoritos (" + favorites.Count + ")") : "Nenhum favorito ainda. Clique no coracao de uma musica.";
            else
            {
                Playlist p = CurrentPlaylist();
                if (p == null)
                    lblInfo.Text = playlists.Count == 0 ? "Nenhuma playlist ainda. Clique em Nova, ou use o botao direito numa musica."
                        : playlists.Count + (playlists.Count == 1 ? " playlist" : " playlists");
                else lblInfo.Text = p.Name + "  \u00B7  " + p.Items.Count + (p.Items.Count == 1 ? " musica" : " musicas");
            }
            lst.Invalidate();
        }

        // ================= favoritos e playlists =================

        bool IsFav(string id)
        {
            foreach (Track t in favorites) if (t.Id == id) return true;
            return false;
        }

        void ToggleFav(Track t)
        {
            int idx = -1;
            for (int i = 0; i < favorites.Count; i++) if (favorites[i].Id == t.Id) { idx = i; break; }
            if (idx >= 0) favorites.RemoveAt(idx);
            else favorites.Add(t.Copy());
            Save();
            UpdateHeart();
            if (view == 1) RefreshList(); else lst.Invalidate();
        }

        void NewPlaylist(Track addTrack)
        {
            string name = Prompt.Ask(this, "Nova playlist", "Nome da playlist:", "");
            if (name == null) return;
            foreach (Playlist q in playlists)
                if (q.Name.ToLower() == name.ToLower()) { MessageBox.Show(this, "Ja existe uma playlist com esse nome."); return; }
            Playlist p = new Playlist();
            p.Name = name;
            if (addTrack != null) p.Items.Add(addTrack.Copy());
            playlists.Add(p);
            Save();
            if (view == 2 && plOpen < 0) { RefreshPlList(); RefreshList(); }
            else if (view == 3) RefreshHome();
            else lblInfo.Text = "Playlist \"" + name + "\" criada.";
        }

        void DeletePlaylistAt(int i)
        {
            if (i < 0 || i >= playlists.Count) return;
            Playlist p = playlists[i];
            if (MessageBox.Show(this, "Excluir a playlist \"" + p.Name + "\"?", "Beat", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
            DropCoverFile(p);
            playlists.Remove(p);
            Save();
            RefreshPlList();
            RefreshList();
        }

        void AddToPlaylist(Playlist p, Track t)
        {
            foreach (Track x in p.Items) if (x.Id == t.Id) { lblInfo.Text = "Ja esta na playlist \"" + p.Name + "\"."; return; }
            p.Items.Add(t.Copy());
            Save();
            lblInfo.Text = "Adicionada a \"" + p.Name + "\".";
        }

        void ctx_Opening(object sender, CancelEventArgs e)
        {
            if (ctxIndex < 0 || ctxIndex >= viewList.Count) { e.Cancel = true; return; }
            int idx = ctxIndex;
            Track t = viewList[idx];
            ctx.Items.Clear();
            ctx.Items.Add(MI("Tocar", delegate { PlayFromView(idx); }));
            ctx.Items.Add(MI(IsFav(t.Id) ? "Remover dos favoritos" : "Adicionar aos favoritos", delegate { ToggleFav(t); }));

            ToolStripMenuItem sub = MI("Adicionar a playlist", null);
            foreach (Playlist p in playlists)
            {
                Playlist pp = p;
                sub.DropDownItems.Add(MI(p.Name, delegate { AddToPlaylist(pp, t); }));
            }
            if (playlists.Count > 0) sub.DropDownItems.Add(new ToolStripSeparator());
            sub.DropDownItems.Add(MI("Nova playlist...", delegate { NewPlaylist(t); }));
            ctx.Items.Add(sub);
            ctx.Items.Add(MI("Tocar musicas relacionadas", delegate { SetRelated(true, false); PlayFromView(idx); }));
            ctx.Items.Add(MI("Copiar link", delegate { CopyLink(t); }));

            if (view == 2)
            {
                Playlist cur = CurrentPlaylist();
                if (cur != null)
                {
                    ctx.Items.Add(new ToolStripSeparator());
                    ctx.Items.Add(MI("Remover desta playlist", delegate
                    {
                        if (idx < cur.Items.Count) { cur.Items.RemoveAt(idx); Save(); RefreshList(); }
                    }));
                }
            }
        }

        // ================= lista: desenho e mouse =================

        void ShowRowMenu(int idx, Point at)
        {
            if (idx < 0 || idx >= viewList.Count) return;
            Track t = viewList[idx];
            ContextMenuStrip m = new ContextMenuStrip();
            m.Renderer = new ToolStripProfessionalRenderer(new DarkColors());
            m.ForeColor = Color.White;
            m.Items.Add(MI("Tocar a seguir", delegate { QueuePlayNext(t); }));
            m.Items.Add(MI("Adicionar \u00E0 lista de reprodu\u00E7\u00E3o", delegate { QueueAdd(t); }));
            ToolStripMenuItem sub = MI("Adicionar \u00E0 playlist", null);
            foreach (Playlist pl in playlists)
            {
                Playlist pp = pl;
                sub.DropDownItems.Add(MI(pl.Name, delegate { AddToPlaylist(pp, t); }));
            }
            if (playlists.Count > 0) sub.DropDownItems.Add(new ToolStripSeparator());
            sub.DropDownItems.Add(MI("Nova playlist...", delegate { NewPlaylist(t); }));
            m.Items.Add(sub);
            if (view == 2)
            {
                Playlist cur = CurrentPlaylist();
                if (cur != null)
                {
                    m.Items.Add(new ToolStripSeparator());
                    m.Items.Add(MI("Remover desta playlist", delegate
                    {
                        if (idx < cur.Items.Count) { cur.Items.RemoveAt(idx); Save(); RefreshList(); }
                    }));
                }
            }
            m.Closed += delegate { BeginInvoke((MethodInvoker)delegate { m.Dispose(); }); };
            m.Show(lst, new Point(Math.Max(0, at.X - 250), at.Y));
        }

        void lst_MouseDown(object sender, MouseEventArgs e)
        {
            int i = lst.IndexFromPoint(e.Location);
            if (e.Button == MouseButtons.Right)
            {
                ctxIndex = i;
                if (i >= 0) lst.SelectedIndex = i;
                return;
            }
            if (e.Button == MouseButtons.Left && i >= 0 && i < viewList.Count)
            {
                int w = lst.ClientSize.Width;
                if (e.X >= w - 154 && e.X <= w - 118) ToggleFav(viewList[i]);
                else if (e.X >= w - 50 && e.X <= w - 14) { lst.SelectedIndex = i; ShowRowMenu(i, new Point(w - 14, lst.GetItemRectangle(i).Bottom - 10)); }
            }
        }

        void lst_DrawItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= viewList.Count) return;
            Track t = viewList[e.Index];
            bool sel = (e.State & DrawItemState.Selected) != 0;
            bool cur = nowId != null && t.Id == nowId;
            using (SolidBrush b = new SolidBrush(sel ? BgSel : BgMain)) e.Graphics.FillRectangle(b, e.Bounds);

            int right = e.Bounds.Right;
            Color tc = cur ? Accent : Color.White;
            TextFormatFlags f = TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.Left;

            Rectangle rt = new Rectangle(e.Bounds.X + 16, e.Bounds.Y + 9, 88, 50);
            Gfx.DrawThumb(e.Graphics, rt, GetThumb(t.Id), 6);

            int tx = e.Bounds.X + 118;
            int tw = Math.Max(10, right - 190 - tx);
            Rectangle r1 = new Rectangle(tx, e.Bounds.Y + 13, tw, 22);
            Rectangle r2 = new Rectangle(tx, e.Bounds.Y + 37, tw, 18);
            Rectangle rDur = new Rectangle(right - 112, e.Bounds.Y + 24, 60, 20);
            Rectangle rHeart = new Rectangle(right - 154, e.Bounds.Y + 20, 36, 28);
            {
                int dcx = right - 32, dcy = e.Bounds.Y + 34;
                GraphicsState gsd = e.Graphics.Save();
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (SolidBrush db = new SolidBrush(sel ? Color.White : Color.FromArgb(150, 150, 150)))
                    for (int dk = -1; dk <= 1; dk++) e.Graphics.FillEllipse(db, dcx - 2f, dcy + dk * 7 - 2f, 4f, 4f);
                e.Graphics.Restore(gsd);
            }

            TextRenderer.DrawText(e.Graphics, (cur ? "\u266A  " : "") + t.Title, fontBold, r1, tc, f);
            TextRenderer.DrawText(e.Graphics, t.Channel, fontSmall, r2, Dim, f);
            bool fav = IsFav(t.Id);
            Gfx.Heart(e.Graphics, rHeart.X + rHeart.Width / 2f, rHeart.Y + rHeart.Height / 2f + 0.5f, 18f,
                fav ? Accent : Color.FromArgb(95, 95, 95), fav);
            TextRenderer.DrawText(e.Graphics, t.Duration, fontSmall, rDur, Dim,
                TextFormatFlags.Right | TextFormatFlags.NoPrefix);
            using (Pen p = new Pen(Line)) e.Graphics.DrawLine(p, e.Bounds.X + 16, e.Bounds.Bottom - 1, right - 16, e.Bounds.Bottom - 1);
        }

        // ================= salvar / carregar =================

        static string Clean(string s)
        {
            if (s == null) return "";
            return s.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
        }

        static string TrackLine(Track t)
        {
            return Clean(t.Id) + "\t" + Clean(t.Title) + "\t" + Clean(t.Channel) + "\t" + Clean(t.Duration);
        }

        static Track ParseTrack(string[] p, int off)
        {
            if (p.Length < off + 4 || p[off].Length == 0) return null;
            Track t = new Track();
            t.Id = p[off]; t.Title = p[off + 1]; t.Channel = p[off + 2]; t.Duration = p[off + 3];
            return t;
        }

        void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(dataFile));
                StringBuilder sb = new StringBuilder();
                foreach (string h in history) sb.AppendLine("H\t" + Clean(h));
                foreach (string h in history) { string hc; if (histCover.TryGetValue(h, out hc)) sb.AppendLine("C\t" + Clean(h) + "\t" + hc); }
                sb.AppendLine("V\t" + trkVol.Value);
                sb.AppendLine("S\t" + (shuffle ? 1 : 0));
                sb.AppendLine("R\t" + repeat);
                sb.AppendLine("L\t" + (related ? 1 : 0));
                sb.AppendLine("O\tclear\t" + (optClearHist ? 1 : 0));
                sb.AppendLine("O\ttray\t" + (optTray ? 1 : 0));
                sb.AppendLine("O\tanim\t" + (optAnim ? 1 : 0));
                sb.AppendLine("O\tcv18\t1");
                if (lastPlayed != null) sb.AppendLine("N\t" + TrackLine(lastPlayed));
                foreach (Track t in favorites) sb.AppendLine("F\t" + TrackLine(t));
                foreach (Playlist p in playlists)
                {
                    sb.AppendLine("P\t" + Clean(p.Name) + "\t" + Clean(p.Cover));
                    foreach (Track t in p.Items) sb.AppendLine("T\t" + TrackLine(t));
                }
                File.WriteAllText(dataFile, sb.ToString(), Encoding.UTF8);
            }
            catch (Exception) { }
        }

        void Load()
        {
            try
            {
                if (!File.Exists(dataFile))
                {
                    string oldFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), Path.Combine("Batida", "dados.txt"));
                    if (File.Exists(oldFile))
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(dataFile));
                        File.Copy(oldFile, dataFile);
                    }
                }
                if (!File.Exists(dataFile)) return;
                Playlist curPl = null;
                foreach (string line in File.ReadAllLines(dataFile, Encoding.UTF8))
                {
                    string[] p = line.Split('\t');
                    if (p.Length < 2) continue;
                    string k = p[0];
                    if (k == "H") { if (p[1].Length > 0 && history.Count < 10) history.Add(p[1]); }
                    else if (k == "C") { if (p.Length > 2 && Regex.IsMatch(p[2], "^[A-Za-z0-9_\\-]{11}$")) histCover[p[1]] = p[2]; }
                    else if (k == "V") { int v; if (int.TryParse(p[1], out v)) savedVolume = Math.Max(0, Math.Min(100, v)); }
                    else if (k == "S") shuffle = p[1] == "1";
                    else if (k == "R") { int r; if (int.TryParse(p[1], out r)) repeat = Math.Max(0, Math.Min(2, r)); }
                    else if (k == "L") related = p[1] == "1";
                    else if (k == "O" && p.Length > 2)
                    {
                        if (p[1] == "clear") optClearHist = p[2] == "1";
                        else if (p[1] == "tray") optTray = p[2] == "1";
                        else if (p[1] == "anim") optAnim = p[2] == "1";
                        else if (p[1] == "cv18") coversCleaned = true;
                    }
                    else if (k == "N") { Track t = ParseTrack(p, 1); if (t != null) lastPlayed = t; }
                    else if (k == "F") { Track t = ParseTrack(p, 1); if (t != null) favorites.Add(t); }
                    else if (k == "P") { curPl = new Playlist(); curPl.Name = p[1]; if (p.Length > 2 && p[2].Length > 0) curPl.Cover = p[2]; playlists.Add(curPl); }
                    else if (k == "T") { Track t = ParseTrack(p, 1); if (t != null && curPl != null) curPl.Items.Add(t); }
                }
                if (!coversCleaned) histCover.Clear();
            }
            catch (Exception) { }
        }

        void ApplyLoaded()
        {
            trkVol.Value = savedVolume;
            try { if (wmp != null) wmp.settings.volume = savedVolume; } catch (Exception) { }
            UpdateModeButtons();
            UpdateRelatedButton();
        }
    }
}
