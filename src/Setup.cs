using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace CreeStore
{
    // CreeStoreSetup.exe: installiert ohne Rückfragen und startet danach CreeStore.
    // Mit /S läuft es komplett ohne Fenster (wird vom Store-Update benutzt).
    static class SetupProgram
    {
        [STAThread]
        static int Main(string[] args)
        {
            bool silent = Array.Exists(args, a => a.Equals("/S", StringComparison.OrdinalIgnoreCase)
                                               || a.Equals("--silent", StringComparison.OrdinalIgnoreCase));
            if (silent)
            {
                try
                {
                    Launch(Install());
                    return 0;
                }
                catch
                {
                    return 1;
                }
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            var form = new SetupForm();
            Application.Run(form);
            return form.Succeeded ? 0 : 1;
        }

        public static string Install()
        {
            // Laufenden Store beenden, damit die exe ersetzt werden kann
            foreach (Process p in Process.GetProcessesByName("CreeStore"))
            {
                try { p.Kill(); p.WaitForExit(5000); } catch { }
            }

            Directory.CreateDirectory(Util.InstallDir);
            string exe = Path.Combine(Util.InstallDir, "CreeStore.exe");
            using (Stream src = Assembly.GetExecutingAssembly().GetManifestResourceStream("CreeStore.exe"))
            using (FileStream dst = File.Create(exe))
                src.CopyTo(dst);

            Util.CreateShortcut(Path.Combine(Util.StartMenuDir, "CreeStore.lnk"), exe, Util.InstallDir);
            Util.CreateShortcut(Path.Combine(Util.DesktopDir, "CreeStore.lnk"), exe, Util.InstallDir);

            // Eintrag unter "Apps & Features", damit man deinstallieren kann
            using (RegistryKey k = Registry.CurrentUser.CreateSubKey(Util.UninstallKey))
            {
                k.SetValue("DisplayName", "CreeStore");
                k.SetValue("DisplayVersion", BuildInfo.Version);
                k.SetValue("Publisher", BuildInfo.Repo.Split('/')[0]);
                k.SetValue("DisplayIcon", exe);
                k.SetValue("InstallLocation", Util.InstallDir);
                k.SetValue("UninstallString", "\"" + exe + "\" --uninstall");
                k.SetValue("URLInfoAbout", "https://github.com/" + BuildInfo.Repo);
                k.SetValue("EstimatedSize", (int)(new FileInfo(exe).Length / 1024), RegistryValueKind.DWord);
                k.SetValue("NoModify", 1, RegistryValueKind.DWord);
                k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
            }
            return exe;
        }

        public static void Launch(string exe)
        {
            Process.Start(new ProcessStartInfo(exe) { WorkingDirectory = Util.InstallDir });
        }
    }

    class SetupForm : Form
    {
        public bool Succeeded;
        readonly Label label;
        readonly ProgressBar bar;

        public SetupForm()
        {
            Text = "CreeStore Setup";
            ClientSize = new Size(420, 130);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.FromArgb(24, 26, 27);
            ForeColor = Color.FromArgb(225, 228, 230);
            Font = new Font("Segoe UI", 10f);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            var title = new Label
            {
                Text = "CreeStore",
                Font = new Font("Segoe UI Semibold", 16f),
                ForeColor = Color.FromArgb(94, 190, 70),
                AutoSize = true,
                Location = new Point(20, 14)
            };
            label = new Label
            {
                Text = "Wird installiert…",
                AutoSize = true,
                Location = new Point(22, 54)
            };
            bar = new ProgressBar
            {
                Style = ProgressBarStyle.Marquee,
                Location = new Point(22, 86),
                Size = new Size(376, 16)
            };
            Controls.AddRange(new Control[] { title, label, bar });

            Shown += async (s, e) => await Run();
        }

        async Task Run()
        {
            string error = null, exe = null;
            try
            {
                exe = await Task.Run(() => SetupProgram.Install());
            }
            catch (Exception ex)
            {
                error = ex.Message;
            }

            if (error != null)
            {
                bar.Style = ProgressBarStyle.Continuous;
                label.Text = "Installation fehlgeschlagen.";
                MessageBox.Show(this, error, "CreeStore Setup", MessageBoxButtons.OK, MessageBoxIcon.Error);
                Close();
                return;
            }

            Succeeded = true;
            bar.Style = ProgressBarStyle.Continuous;
            bar.Value = 100;
            label.Text = "Fertig! CreeStore wird gestartet…";
            await Task.Delay(800);
            SetupProgram.Launch(exe);
            Close();
        }
    }
}
