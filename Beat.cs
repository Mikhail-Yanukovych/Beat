// Beat - player de musica leve para Windows (busca no YouTube, toca so o audio)
// Compila com o csc.exe que ja vem no Windows (C# 5). Veja build.bat.
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
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
        public List<Track> Items = new List<Track>();
    }

    static class Native
    {
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
        public DBListBox() { this.DoubleBuffered = true; }
    }

    static class Gfx
    {
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
        }
    }

    class ThumbBox : Panel
    {
        public Bitmap Thumb;

        public ThumbBox() { DoubleBuffered = true; }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(BackColor);
            Gfx.DrawThumb(e.Graphics, new Rectangle(0, 0, 88, 50), Thumb, 6);
        }
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
            g.Clear(Parent != null ? Parent.BackColor : Color.Black);
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
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
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
        Button btnSearch, btnPlDel, btnPlNew;
        IconButton tabSearch, tabFav, tabPl;
        IconButton btnShuffle, btnPrev, btnPlay, btnNext, btnRepeat, btnHeart;
        DBListBox lst;
        ComboBox cmbPl;
        Panel plPanel;
        ThumbBox picNow;
        IconButton btnRelated;
        ToolTip tt = new ToolTip();
        bool related = false;
        bool fetchingRelated = false;
        bool waitingRelated = false;
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
        bool tipShown = false;
        string lastQuery = "";
        int savedVolume = 70;
        Random rnd = new Random();

        public MainForm()
        {
            Text = "Beat";
            ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072; // TLS 1.2
            ServicePointManager.DefaultConnectionLimit = 12;
            ClientSize = new Size(980, 660);
            MinimumSize = new Size(860, 520);
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
            SetView(0);
            EnsureYtdlp();
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
            };

            RoundPanel host = new RoundPanel();
            host.Dock = DockStyle.Fill;
            host.Radius = 10;
            host.Padding = new Padding(16, 7, 12, 0);
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
            sb.Radius = 10;
            sb.BackColor = Accent;
            sb.ForeColor = Color.White;
            sb.Click += delegate { DoSearch(); };
            btnSearch = sb;

            Panel tabs = new Panel();
            tabs.Dock = DockStyle.Left;
            tabs.Width = 336;
            tabs.BackColor = BgPanel;
            tabSearch = MakeTab("Busca", 0, 0);
            tabFav = MakeTab("Favoritos", 1, 108);
            tabPl = MakeTab("Playlists", 2, 216);
            tabs.Controls.Add(tabSearch);
            tabs.Controls.Add(tabFav);
            tabs.Controls.Add(tabPl);

            top.Controls.Add(host);
            top.Controls.Add(spacer);
            top.Controls.Add(btnSearch);
            top.Controls.Add(tabs);
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

            cmbPl = new ComboBox();
            cmbPl.Dock = DockStyle.Fill;
            cmbPl.DropDownStyle = ComboBoxStyle.DropDownList;
            cmbPl.FlatStyle = FlatStyle.Flat;
            cmbPl.BackColor = Color.FromArgb(40, 40, 40);
            cmbPl.ForeColor = Color.White;
            cmbPl.SelectedIndexChanged += delegate { if (!updatingCombo && view == 2) SetView(2); };

            Panel sp1 = new Panel(); sp1.Dock = DockStyle.Right; sp1.Width = 6;
            Panel sp2 = new Panel(); sp2.Dock = DockStyle.Right; sp2.Width = 6;
            btnPlNew = MakeSmall("Nova");
            btnPlNew.Click += delegate { NewPlaylist(null); };
            btnPlDel = MakeSmall("Excluir");
            btnPlDel.Click += delegate { DeletePlaylist(); };

            plPanel.Controls.Add(cmbPl);
            plPanel.Controls.Add(sp2);
            plPanel.Controls.Add(btnPlNew);
            plPanel.Controls.Add(sp1);
            plPanel.Controls.Add(btnPlDel);

            infoBar.Controls.Add(lblInfo);
            infoBar.Controls.Add(plPanel);

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
            tl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 28));
            tl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 44));
            tl.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 296));
            tl.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            // esquerda: titulo + canal/status
            Panel left = new Panel();
            left.Dock = DockStyle.Fill;
            left.Padding = new Padding(18, 24, 8, 0);

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
            right.ColumnCount = 4;
            right.Padding = new Padding(0, 0, 0, 40);
            right.RowCount = 1;
            right.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            right.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 134));
            right.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 46));
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

            btnHeart = MakeIcon("\u2661", new Size(40, 40), Dim);
            btnHeart.Anchor = AnchorStyles.None;
            btnHeart.Click += delegate { if (nowTrack != null) ToggleFav(nowTrack); };

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
            right.Controls.Add(btnHeart, 2, 0);
            right.Controls.Add(trkVol, 3, 0);

            tl.Controls.Add(left, 0, 0);
            tl.Controls.Add(center, 1, 0);
            tl.Controls.Add(right, 2, 0);

            bottom.Controls.Add(tl);
            bottom.Controls.Add(sep);

            Controls.Add(lst);
            Controls.Add(bottom);
            Controls.Add(infoBar);
            Controls.Add(top);

            // atalhos
            KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Space && ActiveControl != txtSearch)
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
            b.Click += delegate { SetView(vv); };
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
                if (!reallyExit && isPlaying && e.CloseReason == CloseReason.UserClosing)
                {
                    e.Cancel = true;
                    Save();
                    Hide();
                    if (!tipShown)
                    {
                        tipShown = true;
                        tray.ShowBalloonTip(2500, "Beat", "Continua tocando aqui na bandeja. Clique com o botao direito no icone para sair.", ToolTipIcon.Info);
                    }
                    return;
                }
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
            base.WndProc(ref m);
        }

        void Tick()
        {
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
            if (idx < 0 || idx >= viewList.Count) return;
            queue = new List<Track>(viewList);
            queueSource = viewList;
            qIndex = idx;
            PlayCurrent();
        }

        void PlayCurrent()
        {
            if (qIndex < 0 || qIndex >= queue.Count) return;
            Track t = queue[qIndex];
            nowTrack = t;
            nowId = t.Id;
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
                if (lst.SelectedIndex >= 0) PlayFromView(lst.SelectedIndex);
                else if (viewList.Count > 0) PlayFromView(0);
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
            btnHeart.Text = fav ? "♥" : "♡";
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
                        if (nowTrack != null && nowTrack.Id == idc) { picNow.Thumb = bmp; picNow.Invalidate(); }
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
            if (on && fetchNow && nowTrack != null) EnsureRelated();
            if (on) lblInfo.Text = "Relacionadas ligado: a fila se completa com musicas parecidas.";
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
                        if (added > 0 && view == 0) lblInfo.Text = "+" + added + " musicas relacionadas na fila.";
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

        // ================= busca =================

        void DoSearch()
        {
            string q = txtSearch.Text.Trim().Replace("\"", " ").Replace("\\", " ");
            if (q.Length == 0) return;
            lastQuery = txtSearch.Text.Trim();
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

        void SetView(int v)
        {
            view = v;
            tabSearch.Underline = v == 0; tabSearch.ForeColor = v == 0 ? Color.White : Dim;
            tabFav.Underline = v == 1; tabFav.ForeColor = v == 1 ? Color.White : Dim;
            tabPl.Underline = v == 2; tabPl.ForeColor = v == 2 ? Color.White : Dim;
            tabSearch.Invalidate(); tabFav.Invalidate(); tabPl.Invalidate();
            plPanel.Visible = v == 2;

            if (v == 0) viewList = results;
            else if (v == 1) viewList = favorites;
            else
            {
                RefreshPlaylistCombo();
                Playlist p = CurrentPlaylist();
                viewList = p != null ? p.Items : new List<Track>();
            }
            lst.Items.Clear();
            RefreshList();
        }

        Playlist CurrentPlaylist()
        {
            int i = cmbPl.SelectedIndex;
            if (i < 0 || i >= playlists.Count) return null;
            return playlists[i];
        }

        void RefreshPlaylistCombo()
        {
            // so recria o combo se os nomes mudaram
            bool same = cmbPl.Items.Count == playlists.Count;
            if (same)
            {
                for (int i = 0; i < playlists.Count; i++)
                    if ((string)cmbPl.Items[i] != playlists[i].Name) { same = false; break; }
            }
            if (same) return;
            int keep = cmbPl.SelectedIndex;
            updatingCombo = true;
            try
            {
                cmbPl.Items.Clear();
                foreach (Playlist p in playlists) cmbPl.Items.Add(p.Name);
                if (cmbPl.Items.Count > 0)
                    cmbPl.SelectedIndex = Math.Max(0, Math.Min(keep, cmbPl.Items.Count - 1));
            }
            finally { updatingCombo = false; }
        }

        void RefreshList()
        {
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
                lblInfo.Text = favorites.Count > 0 ? "Favoritos (" + favorites.Count + ")" : "Nenhum favorito ainda. Clique no coracao de uma musica.";
            else
            {
                Playlist p = CurrentPlaylist();
                if (p == null) lblInfo.Text = "Nenhuma playlist. Clique em Nova, ou use o botao direito numa musica.";
                else lblInfo.Text = p.Items.Count + " musicas";
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
            if (view == 2)
            {
                RefreshPlaylistCombo();
                cmbPl.SelectedIndex = playlists.Count - 1;
            }
            else lblInfo.Text = "Playlist \"" + name + "\" criada.";
        }

        void DeletePlaylist()
        {
            Playlist p = CurrentPlaylist();
            if (p == null) return;
            if (MessageBox.Show(this, "Excluir a playlist \"" + p.Name + "\"?", "Beat", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
            playlists.Remove(p);
            Save();
            cmbPl.Items.Clear();
            SetView(2);
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
                if (e.X >= w - 116 && e.X <= w - 80) ToggleFav(viewList[i]);
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
            int tw = Math.Max(10, right - 150 - tx);
            Rectangle r1 = new Rectangle(tx, e.Bounds.Y + 13, tw, 22);
            Rectangle r2 = new Rectangle(tx, e.Bounds.Y + 37, tw, 18);
            Rectangle rDur = new Rectangle(right - 76, e.Bounds.Y + 24, 60, 20);
            Rectangle rHeart = new Rectangle(right - 118, e.Bounds.Y + 20, 36, 28);

            TextRenderer.DrawText(e.Graphics, (cur ? "\u266A  " : "") + t.Title, fontBold, r1, tc, f);
            TextRenderer.DrawText(e.Graphics, t.Channel, fontSmall, r2, Dim, f);
            bool fav = IsFav(t.Id);
            TextRenderer.DrawText(e.Graphics, fav ? "\u2665" : "\u2661", fontSym, rHeart,
                fav ? Accent : Color.FromArgb(95, 95, 95),
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
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
                sb.AppendLine("Q\t" + Clean(lastQuery));
                sb.AppendLine("V\t" + trkVol.Value);
                sb.AppendLine("S\t" + (shuffle ? 1 : 0));
                sb.AppendLine("R\t" + repeat);
                sb.AppendLine("L\t" + (related ? 1 : 0));
                foreach (Track t in favorites) sb.AppendLine("F\t" + TrackLine(t));
                foreach (Playlist p in playlists)
                {
                    sb.AppendLine("P\t" + Clean(p.Name));
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
                    if (k == "Q") lastQuery = p[1];
                    else if (k == "V") { int v; if (int.TryParse(p[1], out v)) savedVolume = Math.Max(0, Math.Min(100, v)); }
                    else if (k == "S") shuffle = p[1] == "1";
                    else if (k == "R") { int r; if (int.TryParse(p[1], out r)) repeat = Math.Max(0, Math.Min(2, r)); }
                    else if (k == "L") related = p[1] == "1";
                    else if (k == "F") { Track t = ParseTrack(p, 1); if (t != null) favorites.Add(t); }
                    else if (k == "P") { curPl = new Playlist(); curPl.Name = p[1]; playlists.Add(curPl); }
                    else if (k == "T") { Track t = ParseTrack(p, 1); if (t != null && curPl != null) curPl.Items.Add(t); }
                }
            }
            catch (Exception) { }
        }

        void ApplyLoaded()
        {
            txtSearch.Text = lastQuery;
            trkVol.Value = savedVolume;
            try { if (wmp != null) wmp.settings.volume = savedVolume; } catch (Exception) { }
            UpdateModeButtons();
            UpdateRelatedButton();
        }
    }
}
