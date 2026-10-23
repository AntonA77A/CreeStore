using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace CreeStore
{
    // Ein Eintrag aus apps.json
    class StoreApp
    {
        public string Name, Description, Author, Repo, Url, Asset, Args, Type, Version, UninstallName, UninstallArgs;
        public Panel Card;
        public Button Button, UninstallButton;
        public Label Status;

        // So heißt die App unter "Apps & Features"
        public string RegistryName
        {
            get { return UninstallName ?? Name; }
        }

        public string ShortcutPath
        {
            get { return Path.Combine(Util.StartMenuDir, Util.SafeName(Name) + ".lnk"); }
        }

        public bool Portable
        {
            get { return string.Equals(Type, "portable", StringComparison.OrdinalIgnoreCase); }
        }

        public string PortableDir
        {
            get { return Path.Combine(Util.DataDir, "apps", Util.SafeName(Name)); }
        }

        public string PortableExe
        {
            get
            {
                if (!Directory.Exists(PortableDir)) return null;
                string[] exes = Directory.GetFiles(PortableDir, "*.exe");
                return exes.Length > 0 ? exes[0] : null;
            }
        }
    }

    // Eintrag unter "Apps & Features" (Uninstall-Schlüssel in der Registry)
    class UninstallEntry
    {
        const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Uninstall";
        public string KeyName, DisplayName, Version, UninstallString, QuietUninstallString;

        public static UninstallEntry Find(string name)
        {
            foreach (RegistryHive hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
            {
                foreach (RegistryView view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
                {
                    try
                    {
                        using (RegistryKey root = RegistryKey.OpenBaseKey(hive, view))
                        using (RegistryKey key = root.OpenSubKey(KeyPath))
                        {
                            if (key == null) continue;
                            foreach (string sub in key.GetSubKeyNames())
                            {
                                using (RegistryKey k = key.OpenSubKey(sub))
                                {
                                    if (k == null) continue;
                                    string displayName = k.GetValue("DisplayName") as string;
                                    string uninstall = k.GetValue("UninstallString") as string;
                                    if (displayName == null || uninstall == null) continue;
                                    if (!displayName.StartsWith(name, StringComparison.OrdinalIgnoreCase)) continue;
                                    if (Equals(k.GetValue("SystemComponent"), 1)) continue;
                                    return new UninstallEntry
                                    {
                                        KeyName = sub,
                                        DisplayName = displayName,
                                        Version = k.GetValue("DisplayVersion") as string,
                                        UninstallString = uninstall,
                                        QuietUninstallString = k.GetValue("QuietUninstallString") as string
                                    };
                                }
                            }
                        }
                    }
                    catch { }
                }
            }
            return null;
        }

        // Baut den Befehl für eine stille Deinstallation
        public ProcessStartInfo Command(string extraArgs)
        {
            string exe, args;
            Split(QuietUninstallString ?? UninstallString, out exe, out args);

            if (Path.GetFileName(exe).Equals("msiexec.exe", StringComparison.OrdinalIgnoreCase))
            {
                Match code = Regex.Match(UninstallString, @"\{[0-9A-Fa-f\-]+\}");
                exe = "msiexec.exe";
                args = "/x " + code.Value + " /passive /norestart";
            }
            else if (QuietUninstallString == null)
            {
                if (extraArgs != null)
                    args += " " + extraArgs;
                else if (KeyName.EndsWith("_is1", StringComparison.OrdinalIgnoreCase))
                    args += " /VERYSILENT /SUPPRESSMSGBOXES /NORESTART"; // Inno Setup
                else if (Path.GetFileName(exe).StartsWith("unins", StringComparison.OrdinalIgnoreCase))
                    args += " /S"; // NSIS
            }
            return new ProcessStartInfo(exe, args.Trim()) { UseShellExecute = true };
        }

        static void Split(string command, out string exe, out string args)
        {
            command = command.Trim();
            int end;
            if (command.StartsWith("\"") && (end = command.IndexOf('"', 1)) > 0)
            {
                exe = command.Substring(1, end - 1);
                args = command.Substring(end + 1);
            }
            else if ((end = command.IndexOf(".exe", StringComparison.OrdinalIgnoreCase)) >= 0)
            {
                exe = command.Substring(0, end + 4);
                args = command.Substring(end + 4);
            }
            else
            {
                exe = command;
                args = "";
            }
        }
    }

    class StoreForm : Form
    {
        static readonly Color Bg = Color.FromArgb(24, 26, 27);
        static readonly Color Bar = Color.FromArgb(32, 35, 37);
        static readonly Color CardBg = Color.FromArgb(38, 41, 44);
        static readonly Color Green = Color.FromArgb(94, 190, 70);
        static readonly Color Fg = Color.FromArgb(225, 228, 230);
        static readonly Color Muted = Color.FromArgb(145, 150, 155);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, string lParam);

        readonly FlowLayoutPanel list;
        readonly TextBox search;
        readonly Label status;
        readonly Button updateButton;
        readonly List<StoreApp> apps = new List<StoreApp>();
        readonly string installedFile = Path.Combine(Util.DataDir, "installed.json");
        Dictionary<string, string> installed = new Dictionary<string, string>();
        bool busy;

        public StoreForm()
        {
            Text = "CreeStore";
            Size = new Size(1010, 680);
            MinimumSize = new Size(660, 420);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Bg;
            ForeColor = Fg;
            Font = new Font("Segoe UI", 9.5f);
            Icon = Util.AppIcon() ?? Icon;

            var top = new Panel { Dock = DockStyle.Top, Height = 64, BackColor = Bar };
            var title = new Label
            {
                Text = "CreeStore",
                Font = new Font("Segoe UI Semibold", 18f),
                ForeColor = Green,
                AutoSize = true,
                Location = new Point(16, 13)
            };
            search = new TextBox
            {
                Width = 280,
                Location = new Point(200, 20),
                BackColor = CardBg,
                ForeColor = Fg,
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Segoe UI", 11f)
            };
            search.HandleCreated += (s, e) => SendMessage(search.Handle, 0x1501, (IntPtr)1, "Apps suchen…");
            search.TextChanged += (s, e) => ApplyFilter();

            var refresh = MakeButton("Neu laden", 110);
            refresh.Location = new Point(494, 18);
            refresh.Click += (s, e) => LoadCatalog();

            updateButton = MakeButton("Store-Update", 170);
            updateButton.Visible = false;
            updateButton.BackColor = Green;
            updateButton.ForeColor = Color.Black;
            updateButton.Click += (s, e) => SelfUpdate();
            top.Resize += (s, e) => updateButton.Location = new Point(top.Width - updateButton.Width - 16, 18);

            top.Controls.AddRange(new Control[] { title, search, refresh, updateButton });

            list = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                Padding = new Padding(12),
                BackColor = Bg
            };

            status = new Label
            {
                Dock = DockStyle.Bottom,
                Height = 26,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(12, 0, 0, 0),
                ForeColor = Muted,
                BackColor = Bar
            };

            // Fill zuerst, damit Top/Bottom vorher angedockt werden
            Controls.Add(list);
            Controls.Add(top);
            Controls.Add(status);

            LoadInstalled();
            Shown += (s, e) => { LoadCatalog(); CheckSelfUpdate(); };
        }

        Button MakeButton(string text, int width)
        {
            var b = new Button
            {
                Text = text,
                Size = new Size(width, 30),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(52, 56, 60),
                ForeColor = Fg,
                Cursor = Cursors.Hand
            };
            b.FlatAppearance.BorderSize = 0;
            return b;
        }

        // ---------- Katalog von GitHub ----------

        async void LoadCatalog()
        {
            if (busy) return;
            status.Text = "Lade Apps von GitHub…";
            list.Controls.Clear();
            apps.Clear();

            string url = "https://raw.githubusercontent.com/" + BuildInfo.Repo + "/" + BuildInfo.Branch + "/apps.json";
            string error = null;
            try
            {
                string json = null;
                try
                {
                    // Die API ist sofort aktuell, raw.githubusercontent.com cached bis zu 5 Minuten
                    using (var wc = Util.NewClient())
                    {
                        wc.Headers[HttpRequestHeader.Accept] = "application/vnd.github.raw";
                        json = await wc.DownloadStringTaskAsync(new Uri("https://api.github.com/repos/" + BuildInfo.Repo +
                            "/contents/apps.json?ref=" + BuildInfo.Branch));
                    }
                }
                catch (WebException) { }
                if (json == null)
                {
                    // z.B. API-Limit erreicht
                    using (var wc = Util.NewClient())
                        json = await wc.DownloadStringTaskAsync(new Uri(url));
                }

                object root = Util.ParseJson(json);
                var arr = root as object[];
                var dict = root as Dictionary<string, object>;
                if (arr == null && dict != null && dict.ContainsKey("apps")) arr = dict["apps"] as object[];
                if (arr == null) throw new Exception("apps.json hat kein gültiges Format.");

                foreach (object o in arr)
                {
                    var d = o as Dictionary<string, object>;
                    if (d == null) continue;
                    var a = new StoreApp
                    {
                        Name = Util.Str(d, "name"),
                        Description = Util.Str(d, "description"),
                        Author = Util.Str(d, "author"),
                        Repo = Util.Str(d, "repo"),
                        Url = Util.Str(d, "url"),
                        Asset = Util.Str(d, "asset"),
                        Args = Util.Str(d, "args"),
                        Type = Util.Str(d, "type"),
                        Version = Util.Str(d, "version"),
                        UninstallName = Util.Str(d, "uninstallName"),
                        UninstallArgs = Util.Str(d, "uninstallArgs")
                    };
                    if (string.IsNullOrEmpty(a.Name) || (a.Repo == null && a.Url == null)) continue;
                    apps.Add(a);
                    list.Controls.Add(MakeCard(a));
                }
                status.Text = apps.Count + " Apps · Quelle: github.com/" + BuildInfo.Repo + " · CreeStore v" + BuildInfo.Version;
                ApplyFilter();
            }
            catch (Exception ex)
            {
                error = ex.Message;
            }

            if (error != null)
            {
                status.Text = "Fehler beim Laden: " + error;
                list.Controls.Add(new Label
                {
                    Text = "Die App-Liste konnte nicht von GitHub geladen werden.\n\n" + url + "\n\n" + error,
                    ForeColor = Muted,
                    AutoSize = true,
                    Margin = new Padding(8)
                });
            }
        }

        Panel MakeCard(StoreApp a)
        {
            var card = new Panel { Size = new Size(300, 194), Margin = new Padding(8), BackColor = CardBg };

            var name = new Label
            {
                Text = a.Name,
                Font = new Font("Segoe UI Semibold", 12.5f),
                ForeColor = Color.White,
                Location = new Point(14, 12),
                Size = new Size(272, 28),
                AutoEllipsis = true
            };
            string author = a.Author ?? (a.Repo != null ? a.Repo.Split('/')[0] : "");
            var meta = new Label
            {
                Text = author + (a.Version != null ? "  ·  v" + Util.Clean(a.Version) : "") + (a.Portable ? "  ·  portabel" : ""),
                ForeColor = Muted,
                Location = new Point(14, 40),
                Size = new Size(272, 18),
                AutoEllipsis = true
            };
            var desc = new Label
            {
                Text = a.Description ?? "",
                ForeColor = Fg,
                Location = new Point(14, 62),
                Size = new Size(272, 54),
                AutoEllipsis = true
            };
            a.Status = new Label
            {
                Location = new Point(14, 120),
                Size = new Size(272, 24),
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = Green,
                AutoEllipsis = true
            };
            a.UninstallButton = MakeButton("Deinstallieren", 130);
            a.UninstallButton.Location = new Point(14, 150);
            a.UninstallButton.Click += (s, e) => OnUninstall(a);
            a.Button = MakeButton("Installieren", 116);
            a.Button.Location = new Point(170, 150);
            a.Button.Click += (s, e) => OnAppButton(a);

            card.Controls.AddRange(new Control[] { name, meta, desc, a.Status, a.UninstallButton, a.Button });
            a.Card = card;
            RefreshCard(a);
            return card;
        }

        void RefreshCard(StoreApp a)
        {
            string v;
            bool isInstalled = installed.TryGetValue(a.Name, out v);
            if (a.Portable)
            {
                if (a.PortableExe == null) isInstalled = false;
            }
            else
            {
                // Windows weiß am besten, ob die App (noch) installiert ist
                UninstallEntry entry = UninstallEntry.Find(a.RegistryName);
                isInstalled = entry != null;
                if (entry != null && string.IsNullOrEmpty(v)) v = entry.Version;
            }
            a.UninstallButton.Visible = isInstalled;

            if (!isInstalled)
            {
                a.Status.Text = "";
                a.Button.Text = "Installieren";
                a.Button.BackColor = Green;
                a.Button.ForeColor = Color.Black;
            }
            else if (!string.IsNullOrEmpty(v) && a.Version != null && Util.IsNewer(a.Version, v))
            {
                a.Status.Text = "Update verfügbar";
                a.Button.Text = "Aktualisieren";
                a.Button.BackColor = Green;
                a.Button.ForeColor = Color.Black;
            }
            else
            {
                a.Status.Text = "✓ Installiert" + (string.IsNullOrEmpty(v) ? "" : " v" + Util.Clean(v));
                a.Button.Text = a.Portable ? "Öffnen" : "Neu installieren";
                a.Button.BackColor = Color.FromArgb(52, 56, 60);
                a.Button.ForeColor = Fg;
            }
        }

        void ApplyFilter()
        {
            string q = search.Text.Trim();
            foreach (var a in apps)
            {
                string hay = a.Name + " " + a.Description + " " + a.Author + " " + a.Repo;
                a.Card.Visible = q.Length == 0 || hay.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0;
            }
        }

        // ---------- Installieren ----------

        async void OnAppButton(StoreApp a)
        {
            if (a.Button.Text == "Öffnen")
            {
                string exe = a.PortableExe;
                if (exe != null) Process.Start(new ProcessStartInfo(exe) { WorkingDirectory = a.PortableDir });
                return;
            }
            if (busy)
            {
                status.Text = "Bitte warten – es läuft schon eine Installation.";
                return;
            }

            busy = true;
            a.Button.Enabled = false;
            string error = null;
            try
            {
                string url = a.Url, version = a.Version, file = null;
                if (url == null)
                {
                    a.Status.Text = "Suche Release…";
                    ReleaseAsset rel = await Task.Run(() => Util.LatestAsset(a.Repo, a.Asset));
                    url = rel.Url;
                    version = rel.Version;
                    file = rel.FileName;
                }
                if (file == null) file = Path.GetFileName(new Uri(url).LocalPath);

                string downloads = Path.Combine(Util.DataDir, "downloads");
                Directory.CreateDirectory(downloads);
                string path = Path.Combine(downloads, file);

                using (var wc = Util.NewClient())
                {
                    wc.DownloadProgressChanged += (s, e) => a.Status.Text = "Lade… " + e.ProgressPercentage + "%";
                    await wc.DownloadFileTaskAsync(new Uri(url), path);
                }

                a.Status.Text = "Installiere…";
                status.Text = a.Name + " wird installiert…";
                if (a.Portable)
                    InstallPortable(a, path, file);
                else
                    await RunInstaller(a, path, file);

                installed[a.Name] = version;
                SaveInstalled();
                status.Text = a.Name + " wurde installiert.";
            }
            catch (Win32Exception ex)
            {
                // 1223 = UAC-Abfrage abgelehnt
                error = ex.NativeErrorCode == 1223 ? "Installation abgebrochen." : ex.Message;
            }
            catch (Exception ex)
            {
                error = ex.Message;
            }

            busy = false;
            a.Button.Enabled = true;
            RefreshCard(a);
            if (error != null)
            {
                a.Status.Text = "Fehler";
                status.Text = a.Name + ": " + error;
                MessageBox.Show(this, error, a.Name + " – Installation fehlgeschlagen", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        void InstallPortable(StoreApp a, string path, string file)
        {
            if (Directory.Exists(a.PortableDir))
                foreach (string old in Directory.GetFiles(a.PortableDir, "*.exe")) File.Delete(old);
            Directory.CreateDirectory(a.PortableDir);
            string target = Path.Combine(a.PortableDir, file);
            File.Copy(path, target, true);
            File.Delete(path);
            Util.CreateShortcut(a.ShortcutPath, target, a.PortableDir);
        }

        // ---------- Deinstallieren ----------

        async void OnUninstall(StoreApp a)
        {
            if (busy)
            {
                status.Text = "Bitte warten – es läuft schon eine Installation.";
                return;
            }
            if (MessageBox.Show(this, a.Name + " wirklich deinstallieren?", "CreeStore",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;

            busy = true;
            a.Button.Enabled = a.UninstallButton.Enabled = false;
            a.Status.Text = "Deinstalliere…";
            status.Text = a.Name + " wird deinstalliert…";
            string error = null;
            try
            {
                if (a.Portable)
                {
                    if (Directory.Exists(a.PortableDir)) Directory.Delete(a.PortableDir, true);
                    File.Delete(a.ShortcutPath);
                }
                else
                {
                    UninstallEntry entry = UninstallEntry.Find(a.RegistryName);
                    if (entry == null) throw new Exception("Kein Deinstallationseintrag in Windows gefunden.");

                    using (Process p = Process.Start(entry.Command(a.UninstallArgs)))
                        if (p != null) await Task.Run(() => p.WaitForExit());

                    // Viele Uninstaller kopieren sich nach %TEMP% und laufen dort weiter
                    for (int i = 0; i < 120 && UninstallEntry.Find(a.RegistryName) != null; i++)
                        await Task.Delay(1000);
                    if (UninstallEntry.Find(a.RegistryName) != null)
                        throw new Exception("Die Deinstallation wurde nicht abgeschlossen.");
                }

                installed.Remove(a.Name);
                SaveInstalled();
                status.Text = a.Name + " wurde deinstalliert.";
            }
            catch (Win32Exception ex)
            {
                error = ex.NativeErrorCode == 1223 ? "Deinstallation abgebrochen." : ex.Message;
            }
            catch (Exception ex)
            {
                error = ex.Message;
            }

            busy = false;
            a.Button.Enabled = a.UninstallButton.Enabled = true;
            RefreshCard(a);
            if (error != null)
            {
                status.Text = a.Name + ": " + error;
                MessageBox.Show(this, error, a.Name + " – Deinstallation fehlgeschlagen", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        async Task RunInstaller(StoreApp a, string path, string file)
        {
            var psi = new ProcessStartInfo { UseShellExecute = true };
            if (file.EndsWith(".msi", StringComparison.OrdinalIgnoreCase))
            {
                psi.FileName = "msiexec.exe";
                psi.Arguments = "/i \"" + path + "\" " + (a.Args ?? "/passive");
            }
            else
            {
                psi.FileName = path;
                psi.Arguments = a.Args ?? "";
            }

            using (Process p = Process.Start(psi))
            {
                await Task.Run(() => p.WaitForExit());
                // 3010 = erfolgreich, Neustart nötig
                if (p.ExitCode != 0 && p.ExitCode != 3010)
                    throw new Exception("Der Installer wurde mit Code " + p.ExitCode + " beendet.");
            }
            try { File.Delete(path); } catch { }
        }

        void LoadInstalled()
        {
            try
            {
                if (!File.Exists(installedFile)) return;
                var d = Util.ParseJson(File.ReadAllText(installedFile)) as Dictionary<string, object>;
                if (d == null) return;
                foreach (var kv in d) installed[kv.Key] = kv.Value as string;
            }
            catch { }
        }

        void SaveInstalled()
        {
            Directory.CreateDirectory(Util.DataDir);
            File.WriteAllText(installedFile, Util.ToJson(installed));
        }

        // ---------- Store-Update über GitHub-Releases ----------

        async void CheckSelfUpdate()
        {
            try
            {
                ReleaseAsset rel = await Task.Run(() => Util.LatestAsset(BuildInfo.Repo, "CreeStoreSetup.exe"));
                if (Util.IsNewer(rel.Version, BuildInfo.Version))
                {
                    updateButton.Text = "Update auf v" + Util.Clean(rel.Version);
                    updateButton.Tag = rel;
                    updateButton.Visible = true;
                }
            }
            catch { }
        }

        async void SelfUpdate()
        {
            var rel = updateButton.Tag as ReleaseAsset;
            if (rel == null) return;
            updateButton.Enabled = false;
            try
            {
                string path = Path.Combine(Path.GetTempPath(), "CreeStoreSetup.exe");
                using (var wc = Util.NewClient())
                {
                    wc.DownloadProgressChanged += (s, e) => updateButton.Text = "Lade… " + e.ProgressPercentage + "%";
                    await wc.DownloadFileTaskAsync(new Uri(rel.Url), path);
                }
                Process.Start(path, "/S");
                Application.Exit();
            }
            catch (Exception ex)
            {
                updateButton.Enabled = true;
                updateButton.Text = "Update auf v" + Util.Clean(rel.Version);
                MessageBox.Show(this, ex.Message, "Update fehlgeschlagen", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
    }

    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            if (args.Length > 0 && args[0].Equals("--uninstall", StringComparison.OrdinalIgnoreCase))
            {
                Uninstall();
                return;
            }
            Application.Run(new StoreForm());
        }

        static void Uninstall()
        {
            if (MessageBox.Show("CreeStore wirklich deinstallieren?\n\n(Apps, die du über CreeStore installiert hast, bleiben erhalten.)",
                    "CreeStore", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;

            try { File.Delete(Path.Combine(Util.StartMenuDir, "CreeStore.lnk")); } catch { }
            try { File.Delete(Path.Combine(Util.DesktopDir, "CreeStore.lnk")); } catch { }
            try { Registry.CurrentUser.DeleteSubKeyTree(Util.UninstallKey, false); } catch { }

            MessageBox.Show("CreeStore wurde deinstalliert.", "CreeStore");

            // Ordner erst löschen, wenn diese exe beendet ist
            Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = "/c ping 127.0.0.1 -n 3 >nul & rmdir /s /q \"" + Util.InstallDir + "\"",
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            });
        }
    }
}
