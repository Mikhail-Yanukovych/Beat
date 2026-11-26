using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using Microsoft.Win32;

namespace BeatSetup
{
    static class Program
    {
        public const string AppName = "Beat";
        public const string Version = "1.9";
        public const string UninstKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\Beat";

        [STAThread]
        static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            bool uninstall = false;
            string updDir = null;
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i].ToLowerInvariant();
                if (a == "/uninstall") uninstall = true;
                else if (a == "/update" && i + 1 < args.Length) updDir = args[i + 1];
            }
            if (updDir != null) Updater.Run(updDir);
            else if (uninstall) Uninstaller.Run();
            else Application.Run(new SetupForm());
        }

        public static string StartMenuLink()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Beat.lnk");
        }

        public static string DesktopLink()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Beat.lnk");
        }

        public static void MakeShortcut(string linkPath, string target, string workDir)
        {
            Type t = Type.GetTypeFromProgID("WScript.Shell");
            dynamic sh = Activator.CreateInstance(t);
            dynamic lnk = sh.CreateShortcut(linkPath);
            lnk.TargetPath = target;
            lnk.WorkingDirectory = workDir;
            lnk.IconLocation = target + ",0";
            lnk.Description = "Beat - player de musica";
            lnk.Save();
        }

        // registra o link beat://musica/ID no Windows (so para o usuario atual)
        public static void RegisterProtocol(string exe)
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.CreateSubKey(@"Software\Classes\beat"))
                {
                    k.SetValue("", "URL:Beat");
                    k.SetValue("URL Protocol", "");
                }
                using (RegistryKey k = Registry.CurrentUser.CreateSubKey(@"Software\Classes\beat\DefaultIcon"))
                    k.SetValue("", "\"" + exe + "\",0");
                using (RegistryKey k = Registry.CurrentUser.CreateSubKey(@"Software\Classes\beat\shell\open\command"))
                    k.SetValue("", "\"" + exe + "\" \"%1\"");
            }
            catch (Exception) { }
        }

        public static void UnregisterProtocol()
        {
            try { Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\beat", false); } catch (Exception) { }
        }

        public static void ExtractBeat(string exe)
        {
            TryDelete(exe);
            using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream("Beat.exe"))
            using (FileStream fs = new FileStream(exe, FileMode.Create, FileAccess.Write))
            {
                byte[] buf = new byte[81920];
                int n;
                while ((n = s.Read(buf, 0, buf.Length)) > 0) fs.Write(buf, 0, n);
            }
        }

        public static void KillBeat()
        {
            try
            {
                foreach (Process p in Process.GetProcessesByName("Beat"))
                {
                    try { p.Kill(); p.WaitForExit(3000); } catch { }
                }
            }
            catch { }
        }

        public static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }
    }

    class SetupForm : Form
    {
        RadioButton rbInstall, rbRun;
        CheckBox chkDesktop, chkStart, chkRun;
        Panel pnlAdv;
        TextBox txtDir;
        Button btnBrowse, btnGo;
        Label lblWarn, lblStatus;
        ProgressBar bar;

        public SetupForm()
        {
            Text = "Instalação do Beat";
            ClientSize = new Size(495, 400);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = SystemColors.Control;
            Font = new Font("Segoe UI", 9f);
            Icon appIcon = null;
            try { appIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); Icon = appIcon; } catch { }

            Panel header = new Panel();
            header.SetBounds(0, 0, 495, 70);
            header.BackColor = Color.White;
            Controls.Add(header);

            Label title = new Label();
            title.Text = "Bem-vindo ao Beat";
            title.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
            title.AutoSize = true;
            title.Location = new Point(16, 14);
            header.Controls.Add(title);

            Label sub = new Label();
            sub.Text = "Player de música leve, usa bem pouca memória RAM";
            sub.AutoSize = true;
            sub.Location = new Point(24, 38);
            header.Controls.Add(sub);

            if (appIcon != null)
            {
                PictureBox pb = new PictureBox();
                pb.SetBounds(433, 15, 40, 40);
                pb.SizeMode = PictureBoxSizeMode.Zoom;
                pb.Image = new Icon(appIcon, 48, 48).ToBitmap();
                header.Controls.Add(pb);
            }

            Panel line = new Panel();
            line.SetBounds(0, 70, 495, 1);
            line.BackColor = Color.FromArgb(160, 160, 160);
            Controls.Add(line);

            Label q = new Label();
            q.Text = "Como você quer continuar?";
            q.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
            q.AutoSize = true;
            q.Location = new Point(16, 92);
            Controls.Add(q);

            rbInstall = new RadioButton();
            rbInstall.Text = "Instalar o Beat (você escolhe a pasta)";
            rbInstall.Checked = true;
            rbInstall.AutoSize = true;
            rbInstall.Location = new Point(24, 120);
            rbInstall.CheckedChanged += delegate { UpdateState(); };
            Controls.Add(rbInstall);

            rbRun = new RadioButton();
            rbRun.Text = "Executar somente (sem instalar)";
            rbRun.AutoSize = true;
            rbRun.Location = new Point(24, 146);
            Controls.Add(rbRun);

            pnlAdv = new Panel();
            pnlAdv.SetBounds(16, 176, 463, 112);
            pnlAdv.Visible = true;
            Controls.Add(pnlAdv);

            Label l1 = new Label();
            l1.Text = "Pasta de instalação (clique em Procurar para escolher outro disco ou local):";
            l1.AutoSize = true;
            l1.Location = new Point(0, 2);
            pnlAdv.Controls.Add(l1);

            txtDir = new TextBox();
            txtDir.SetBounds(0, 22, 365, 23);
            txtDir.Text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Programs\Beat");
            txtDir.TextChanged += delegate { CheckWarn(); };
            pnlAdv.Controls.Add(txtDir);

            btnBrowse = new Button();
            btnBrowse.Text = "Procurar...";
            btnBrowse.SetBounds(373, 21, 90, 25);
            btnBrowse.Click += btnBrowse_Click;
            pnlAdv.Controls.Add(btnBrowse);

            lblWarn = new Label();
            lblWarn.SetBounds(0, 50, 463, 32);
            lblWarn.ForeColor = Color.FromArgb(180, 90, 0);
            pnlAdv.Controls.Add(lblWarn);

            chkStart = new CheckBox();
            chkStart.Text = "Atalho no menu Iniciar";
            chkStart.Checked = true;
            chkStart.AutoSize = true;
            chkStart.Location = new Point(0, 88);
            pnlAdv.Controls.Add(chkStart);

            chkRun = new CheckBox();
            chkRun.Text = "Abrir o Beat ao terminar";
            chkRun.Checked = true;
            chkRun.AutoSize = true;
            chkRun.Location = new Point(200, 88);
            pnlAdv.Controls.Add(chkRun);

            chkDesktop = new CheckBox();
            chkDesktop.Text = "Criar atalho na área de trabalho";
            chkDesktop.Checked = true;
            chkDesktop.AutoSize = true;
            chkDesktop.Location = new Point(16, 316);
            Controls.Add(chkDesktop);

            lblStatus = new Label();
            lblStatus.SetBounds(16, 362, 300, 16);
            Controls.Add(lblStatus);

            bar = new ProgressBar();
            bar.SetBounds(16, 380, 300, 8);
            bar.Visible = false;
            Controls.Add(bar);

            btnGo = new Button();
            btnGo.Text = "Instalar";
            btnGo.SetBounds(331, 362, 148, 28);
            btnGo.Click += btnGo_Click;
            Controls.Add(btnGo);
            AcceptButton = btnGo;

            CheckWarn();
            UpdateState();
        }

        void UpdateState()
        {
            bool inst = rbInstall.Checked;
            chkDesktop.Enabled = inst;
            pnlAdv.Visible = inst;
            btnGo.Text = inst ? "Instalar" : "Executar";
        }

        void CheckWarn()
        {
            string d = txtDir.Text.Trim().ToLowerInvariant();
            string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles).ToLowerInvariant();
            string pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86).ToLowerInvariant();
            bool bad = (pf.Length > 0 && d.StartsWith(pf)) || (pf86.Length > 0 && d.StartsWith(pf86));
            lblWarn.Text = bad
                ? "Atenção: em Arquivos de Programas o Beat não consegue baixar e atualizar o yt-dlp sem ser administrador. Prefira outra pasta."
                : "";
        }

        void btnBrowse_Click(object sender, EventArgs e)
        {
            using (FolderBrowserDialog f = new FolderBrowserDialog())
            {
                f.Description = "Escolha a pasta onde o Beat será instalado";
                if (f.ShowDialog(this) == DialogResult.OK)
                {
                    string p = f.SelectedPath;
                    if (!Path.GetFileName(p.TrimEnd('\\')).Equals("Beat", StringComparison.OrdinalIgnoreCase))
                        p = Path.Combine(p, "Beat");
                    txtDir.Text = p;
                }
            }
        }

        void Extract(string exe)
        {
            Program.TryDelete(exe);
            using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream("Beat.exe"))
            using (FileStream fs = new FileStream(exe, FileMode.Create, FileAccess.Write))
            {
                byte[] buf = new byte[81920];
                int n;
                while ((n = s.Read(buf, 0, buf.Length)) > 0) fs.Write(buf, 0, n);
            }
        }

        void Lock(bool on)
        {
            btnGo.Enabled = !on;
            rbInstall.Enabled = !on;
            rbRun.Enabled = !on;
            chkDesktop.Enabled = !on;
            pnlAdv.Enabled = !on;
            bar.Visible = on;
            if (!on) lblStatus.Text = "";
        }

        void btnGo_Click(object sender, EventArgs e)
        {
            string dir;
            bool install = rbInstall.Checked;
            if (install)
            {
                dir = txtDir.Text.Trim();
                if (dir.Length == 0) { MessageBox.Show(this, "Escolha uma pasta.", "Beat"); return; }
                try { dir = Path.GetFullPath(dir); }
                catch { MessageBox.Show(this, "Pasta inválida.", "Beat"); return; }
            }
            else
            {
                dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Beat\Portatil");
            }

            Lock(true);
            bar.Value = 0;
            try
            {
                lblStatus.Text = "Preparando..."; Application.DoEvents();
                Program.KillBeat();
                Directory.CreateDirectory(dir);
                bar.Value = 15;

                lblStatus.Text = "Copiando arquivos..."; Application.DoEvents();
                string exe = Path.Combine(dir, "Beat.exe");
                Extract(exe);
                bar.Value = 50;

                if (install)
                {
                    string unin = Path.Combine(dir, "Desinstalar.exe");
                    Program.TryDelete(unin);
                    File.Copy(Application.ExecutablePath, unin, true);
                    bar.Value = 65;

                    lblStatus.Text = "Criando atalhos..."; Application.DoEvents();
                    if (chkDesktop.Checked) Program.MakeShortcut(Program.DesktopLink(), exe, dir);
                    if (chkStart.Checked) Program.MakeShortcut(Program.StartMenuLink(), exe, dir);
                    bar.Value = 85;

                    lblStatus.Text = "Registrando no Windows..."; Application.DoEvents();
                    Program.RegisterProtocol(exe);
                    using (RegistryKey k = Registry.CurrentUser.CreateSubKey(Program.UninstKey))
                    {
                        k.SetValue("DisplayName", "Beat");
                        k.SetValue("DisplayVersion", Program.Version);
                        k.SetValue("Publisher", "Beat");
                        k.SetValue("InstallLocation", dir);
                        k.SetValue("DisplayIcon", exe);
                        k.SetValue("UninstallString", "\"" + unin + "\" /uninstall");
                        k.SetValue("NoModify", 1, RegistryValueKind.DWord);
                        k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                    }
                }
                bar.Value = 100;
                lblStatus.Text = "Pronto!"; Application.DoEvents();

                if (install)
                    MessageBox.Show(this, "O Beat foi instalado com sucesso.", "Beat", MessageBoxButtons.OK, MessageBoxIcon.Information);
                if (!install || chkRun.Checked)
                {
                    try { Process.Start(new ProcessStartInfo(exe) { WorkingDirectory = dir }); } catch { }
                }
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Não foi possível continuar:\n\n" + ex.Message, "Beat", MessageBoxButtons.OK, MessageBoxIcon.Error);
                Lock(false);
                UpdateState();
            }
        }
    }

    // atualizacao silenciosa: chamada pelo proprio Beat (BeatSetup.exe /update "pasta")
    static class Updater
    {
        public static void Run(string dir)
        {
            try
            {
                System.Threading.Thread.Sleep(1200);
                Program.KillBeat();
                System.Threading.Thread.Sleep(500);
                string exe = Path.Combine(dir, "Beat.exe");
                bool ok = false;
                for (int i = 0; i < 20 && !ok; i++)
                {
                    try { Program.ExtractBeat(exe); ok = true; }
                    catch (Exception) { System.Threading.Thread.Sleep(500); }
                }
                if (!ok)
                {
                    MessageBox.Show("Não foi possível atualizar o Beat. Feche o Beat e tente de novo.", "Beat", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
                Program.RegisterProtocol(exe);
                string unin = Path.Combine(dir, "Desinstalar.exe");
                if (File.Exists(unin))
                {
                    try { File.Copy(Application.ExecutablePath, unin, true); } catch (Exception) { }
                }
                try
                {
                    using (RegistryKey k = Registry.CurrentUser.OpenSubKey(Program.UninstKey, true))
                    {
                        if (k != null) k.SetValue("DisplayVersion", Program.Version);
                    }
                }
                catch (Exception) { }
                Process.Start(new ProcessStartInfo(exe) { WorkingDirectory = dir });
            }
            catch (Exception ex)
            {
                MessageBox.Show("Erro ao atualizar: " + ex.Message, "Beat", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }

    static class Uninstaller
    {
        public static void Run()
        {
            string self = Application.ExecutablePath;
            string dir = Path.GetDirectoryName(self);

            if (MessageBox.Show("Deseja desinstalar o Beat deste computador?", "Desinstalar o Beat",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;

            bool delData = MessageBox.Show("Apagar também seus favoritos, playlists e configurações?\n\n(Escolha Não para manter, caso queira reinstalar depois.)",
                "Desinstalar o Beat", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;

            Program.KillBeat();
            System.Threading.Thread.Sleep(500);

            string[] files = new string[] { "Beat.exe", "yt-dlp.exe", "LEIA-ME.txt" };
            for (int i = 0; i < files.Length; i++) Program.TryDelete(Path.Combine(dir, files[i]));
            Program.TryDelete(Program.DesktopLink());
            Program.TryDelete(Program.StartMenuLink());

            try { Registry.CurrentUser.DeleteSubKeyTree(Program.UninstKey, false); } catch { }
            Program.UnregisterProtocol();

            if (delData)
            {
                try
                {
                    string data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Beat");
                    if (Directory.Exists(data)) Directory.Delete(data, true);
                } catch { }
            }
            try
            {
                string tmp = Path.Combine(Path.GetTempPath(), "Beat");
                if (Directory.Exists(tmp)) Directory.Delete(tmp, true);
            } catch { }

            MessageBox.Show("O Beat foi desinstalado.", "Desinstalar o Beat", MessageBoxButtons.OK, MessageBoxIcon.Information);

            // apaga este proprio executavel e a pasta (se ficar vazia) depois que o programa fechar
            try
            {
                string cmd = "/c ping 127.0.0.1 -n 3 >nul & del /f /q \"" + self + "\" & rmdir \"" + dir + "\"";
                ProcessStartInfo psi = new ProcessStartInfo("cmd.exe", cmd);
                psi.CreateNoWindow = true;
                psi.UseShellExecute = false;
                psi.WindowStyle = ProcessWindowStyle.Hidden;
                Process.Start(psi);
            }
            catch { }
        }
    }
}
