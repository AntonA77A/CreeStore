using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CreeStore
{
    // GitHub-API mit Token (braucht Schreibrechte fürs Repo)
    class GitHubApi
    {
        readonly string token;

        public GitHubApi(string token)
        {
            this.token = token;
        }

        WebClient Client()
        {
            var wc = Util.NewClient();
            wc.Headers[HttpRequestHeader.Accept] = "application/vnd.github+json";
            wc.Headers[HttpRequestHeader.Authorization] = "Bearer " + token;
            wc.Headers["X-GitHub-Api-Version"] = "2022-11-28";
            return wc;
        }

        public object Get(string path)
        {
            using (var wc = Client())
                return Util.ParseJson(wc.DownloadString("https://api.github.com" + path));
        }

        public object Send(string method, string path, object body)
        {
            using (var wc = Client())
            {
                wc.Headers[HttpRequestHeader.ContentType] = "application/json";
                return Util.ParseJson(wc.UploadString("https://api.github.com" + path, method, Util.ToJson(body)));
            }
        }

        // Lesbare Fehlermeldung aus einer GitHub-Antwort
        public static string Describe(Exception ex)
        {
            var we = ex as WebException;
            var resp = we != null ? we.Response as HttpWebResponse : null;
            if (resp == null) return ex.Message;
            string message = null;
            try
            {
                using (var r = new StreamReader(resp.GetResponseStream()))
                {
                    var d = Util.ParseJson(r.ReadToEnd()) as Dictionary<string, object>;
                    if (d != null) message = Util.Str(d, "message");
                }
            }
            catch { }
            return "GitHub " + (int)resp.StatusCode + ": " + (message ?? resp.StatusDescription);
        }
    }

    static class TokenStore
    {
        static string FilePath
        {
            get { return Path.Combine(Util.DataDir, "admin-token.bin"); }
        }

        // Verschlüsselt mit dem Windows-Konto, nur dieser Benutzer kann es lesen
        public static string Load()
        {
            try
            {
                if (!File.Exists(FilePath)) return null;
                return Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(FilePath), null, DataProtectionScope.CurrentUser));
            }
            catch
            {
                return null;
            }
        }

        public static void Save(string token)
        {
            Directory.CreateDirectory(Util.DataDir);
            File.WriteAllBytes(FilePath, ProtectedData.Protect(Encoding.UTF8.GetBytes(token), null, DataProtectionScope.CurrentUser));
        }

        public static void Clear()
        {
            try { File.Delete(FilePath); } catch { }
        }

        // Den GitHub-Login benutzen, den Git schon gespeichert hat
        public static string FromGit()
        {
            try
            {
                var psi = new ProcessStartInfo("git", "credential fill")
                {
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                psi.EnvironmentVariables["GIT_TERMINAL_PROMPT"] = "0";
                using (Process p = Process.Start(psi))
                {
                    // .NET schreibt ein BOM vor die Eingabe; es landet in der ersten Zeile,
                    // deren unbekannten Schlüssel Git ignoriert
                    byte[] request = Encoding.ASCII.GetBytes("creestore=1\nprotocol=https\nhost=github.com\n\n");
                    p.StandardInput.BaseStream.Write(request, 0, request.Length);
                    p.StandardInput.Close();
                    string output = p.StandardOutput.ReadToEnd();
                    p.WaitForExit(60000);
                    foreach (string line in output.Split('\n'))
                        if (line.StartsWith("password=")) return line.Substring("password=".Length).Trim();
                }
            }
            catch { }
            return null;
        }
    }

    // apps.json im Repo lesen und zurückschreiben
    class Catalog
    {
        Dictionary<string, object> root;
        string sha;
        public readonly List<Dictionary<string, object>> Apps = new List<Dictionary<string, object>>();

        static string ContentsPath
        {
            get { return "/repos/" + BuildInfo.Repo + "/contents/apps.json"; }
        }

        public static Catalog Load(GitHubApi api)
        {
            var file = (Dictionary<string, object>)api.Get(ContentsPath + "?ref=" + BuildInfo.Branch);
            string json = Encoding.UTF8.GetString(Convert.FromBase64String(Util.Str(file, "content").Replace("\n", ""))).TrimStart('﻿');
            object parsed = Util.ParseJson(json);

            var c = new Catalog { sha = Util.Str(file, "sha") };
            c.root = parsed as Dictionary<string, object> ?? new Dictionary<string, object>();
            object[] arr = parsed as object[] ?? (c.root.ContainsKey("apps") ? c.root["apps"] as object[] : null);
            if (arr != null)
                foreach (object o in arr)
                {
                    var d = o as Dictionary<string, object>;
                    if (d != null) c.Apps.Add(d);
                }
            return c;
        }

        public int IndexOf(string name)
        {
            for (int i = 0; i < Apps.Count; i++)
                if (string.Equals(Util.Str(Apps[i], "name"), name, StringComparison.OrdinalIgnoreCase)) return i;
            return -1;
        }

        public void Save(GitHubApi api, string message)
        {
            var arr = new object[Apps.Count];
            for (int i = 0; i < arr.Length; i++) arr[i] = Apps[i];
            root["apps"] = arr;

            string json = Json.Pretty(root) + "\n";
            var body = new Dictionary<string, object>
            {
                { "message", message },
                { "content", Convert.ToBase64String(Encoding.UTF8.GetBytes(json)) },
                { "sha", sha },
                { "branch", BuildInfo.Branch }
            };
            var res = (Dictionary<string, object>)api.Send("PUT", ContentsPath, body);
            sha = Util.Str((Dictionary<string, object>)res["content"], "sha");
        }
    }

    // Schreibt JSON eingerückt wie die handgeschriebene apps.json
    static class Json
    {
        public static string Pretty(object value)
        {
            var sb = new StringBuilder();
            Write(sb, value, "");
            return sb.ToString();
        }

        static void Write(StringBuilder sb, object v, string indent)
        {
            string inner = indent + "  ";
            var dict = v as Dictionary<string, object>;
            var list = v as object[];
            if (v == null)
            {
                sb.Append("null");
            }
            else if (v is string)
            {
                Quote(sb, (string)v);
            }
            else if (v is bool)
            {
                sb.Append((bool)v ? "true" : "false");
            }
            else if (dict != null)
            {
                if (dict.Count == 0) { sb.Append("{}"); return; }
                sb.Append("{\n");
                int i = 0;
                foreach (var kv in dict)
                {
                    sb.Append(inner);
                    Quote(sb, kv.Key);
                    sb.Append(": ");
                    Write(sb, kv.Value, inner);
                    if (++i < dict.Count) sb.Append(',');
                    sb.Append('\n');
                }
                sb.Append(indent).Append('}');
            }
            else if (list != null)
            {
                if (list.Length == 0) { sb.Append("[]"); return; }
                sb.Append("[\n");
                for (int i = 0; i < list.Length; i++)
                {
                    sb.Append(inner);
                    Write(sb, list[i], inner);
                    if (i < list.Length - 1) sb.Append(',');
                    sb.Append('\n');
                }
                sb.Append(indent).Append(']');
            }
            else
            {
                sb.Append(Convert.ToString(v, CultureInfo.InvariantCulture));
            }
        }

        static void Quote(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.AppendFormat("\\u{0:x4}", (int)c);
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }
    }

    // Formular mit den Feldern eines apps.json-Eintrags
    class AppEditor : UserControl
    {
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, string lParam);

        // Diese Felder von apps.json bearbeitet das Formular; alle anderen bleiben unangetastet
        public static readonly string[] Keys = { "name", "description", "author", "repo", "asset", "url", "version", "args", "type" };

        readonly TextBox name, description, author, repo, asset, url, args, version;
        readonly ComboBox type;

        public AppEditor()
        {
            var grid = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                AutoScroll = true,
                Padding = new Padding(8)
            };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            name = AddRow(grid, "Name *", "");
            description = AddRow(grid, "Beschreibung", "");
            description.Multiline = true;
            description.Height = 64;
            description.ScrollBars = ScrollBars.Vertical;
            author = AddRow(grid, "Autor", "leer = Besitzer des Repos");
            repo = AddRow(grid, "GitHub-Repo", "besitzer/name");
            asset = AddRow(grid, "Datei im Release", "*.exe");
            url = AddRow(grid, "Direkter Link", "http(s)://… (statt Repo)");
            args = AddRow(grid, "Parameter (still)", "/S, /VERYSILENT, /quiet …");
            version = AddRow(grid, "Version", "optional, für Update-Hinweis");
            type = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top };
            type.Items.AddRange(new object[] { "Installer", "Portabel (nur eine exe)" });
            type.SelectedIndex = 0;
            AddRow(grid, "Art", type);
            // Leere Zeile nimmt den restlichen Platz, damit die Felder oben bleiben
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            grid.RowCount++;

            Controls.Add(grid);
        }

        TextBox AddRow(TableLayoutPanel grid, string label, string hint)
        {
            var tb = new TextBox { Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top };
            if (hint.Length > 0) tb.HandleCreated += (s, e) => SendMessage(tb.Handle, 0x1501, (IntPtr)1, hint);
            AddRow(grid, label, tb);
            return tb;
        }

        void AddRow(TableLayoutPanel grid, string label, Control c)
        {
            int row = grid.RowCount++;
            grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            grid.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left | AnchorStyles.Top, Margin = new Padding(3, 7, 3, 3) }, 0, row);
            grid.Controls.Add(c, 1, row);
        }

        public void SetApp(Dictionary<string, string> f)
        {
            name.Text = Get(f, "name");
            description.Text = Get(f, "description").Replace("\r", "").Replace("\n", "\r\n");
            author.Text = Get(f, "author");
            repo.Text = Get(f, "repo");
            asset.Text = Get(f, "asset");
            url.Text = Get(f, "url");
            args.Text = Get(f, "args");
            version.Text = Get(f, "version");
            type.SelectedIndex = Get(f, "type") == "portable" ? 1 : 0;
        }

        public void Clear()
        {
            SetApp(new Dictionary<string, string>());
        }

        public static Dictionary<string, string> FromApp(Dictionary<string, object> app)
        {
            var f = new Dictionary<string, string>();
            foreach (string key in Keys) f[key] = Util.Str(app, key);
            return f;
        }

        // Bearbeitete Felder übernehmen, Reihenfolge und unbekannte Felder (z.B. uninstallArgs) behalten
        public static Dictionary<string, object> Merge(Dictionary<string, object> original, Dictionary<string, object> edited)
        {
            var result = new Dictionary<string, object>();
            foreach (var kv in original)
            {
                if (Array.IndexOf(Keys, kv.Key) < 0) result[kv.Key] = kv.Value;
                else if (edited.ContainsKey(kv.Key)) result[kv.Key] = edited[kv.Key];
            }
            foreach (var kv in edited)
                if (!result.ContainsKey(kv.Key)) result[kv.Key] = kv.Value;
            return result;
        }

        static string Get(Dictionary<string, string> f, string key)
        {
            string v;
            return f.TryGetValue(key, out v) && v != null ? v : "";
        }

        // Gibt eine Fehlermeldung zurück oder null, wenn alles passt
        public string TryGetApp(out Dictionary<string, object> app)
        {
            app = null;
            string n = name.Text.Trim(), r = NormalizeRepo(repo.Text), u = url.Text.Trim();
            if (n.Length == 0) return "Bitte einen Namen eingeben.";
            if (r.Length == 0 && u.Length == 0) return "Bitte ein GitHub-Repo oder einen direkten Link angeben.";
            if (r.Length > 0 && !Regex.IsMatch(r, @"^[\w.-]+/[\w.-]+$")) return "Das GitHub-Repo muss so aussehen: besitzer/name";
            if (u.Length > 0 && !u.StartsWith("https://", StringComparison.OrdinalIgnoreCase) && !u.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
                return "Der direkte Link muss mit http:// oder https:// anfangen.";

            app = new Dictionary<string, object>();
            Put(app, "name", n);
            Put(app, "description", description.Text.Replace("\r", ""));
            Put(app, "author", author.Text);
            Put(app, "repo", r);
            Put(app, "asset", asset.Text);
            Put(app, "url", u);
            Put(app, "version", version.Text);
            Put(app, "args", args.Text);
            if (type.SelectedIndex == 1) app["type"] = "portable";
            return null;
        }

        static void Put(Dictionary<string, object> app, string key, string value)
        {
            value = value.Trim();
            if (value.Length > 0) app[key] = value;
        }

        // "https://github.com/a/b.git" -> "a/b"
        public static string NormalizeRepo(string s)
        {
            s = s.Trim();
            Match m = Regex.Match(s, @"github\.com/([\w.-]+/[\w.-]+)");
            if (m.Success) s = m.Groups[1].Value;
            if (s.EndsWith(".git", StringComparison.OrdinalIgnoreCase)) s = s.Substring(0, s.Length - 4);
            return s.Trim('/');
        }
    }

    // Kleines Eingabefenster
    static class InputDialog
    {
        public static string Ask(IWin32Window owner, string title, string text, bool password, string linkText, string linkUrl)
        {
            using (var f = new Form())
            {
                f.Text = title;
                f.ClientSize = new Size(460, linkText != null ? 150 : 128);
                f.FormBorderStyle = FormBorderStyle.FixedDialog;
                f.StartPosition = FormStartPosition.CenterParent;
                f.MaximizeBox = f.MinimizeBox = false;
                f.Font = new Font("Segoe UI", 9.5f);

                var label = new Label { Text = text, Location = new Point(12, 12), Size = new Size(436, 38) };
                var box = new TextBox { Location = new Point(12, 54), Width = 436, UseSystemPasswordChar = password };
                var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Location = new Point(282, f.ClientSize.Height - 40), Width = 80 };
                var cancel = new Button { Text = "Abbrechen", DialogResult = DialogResult.Cancel, Location = new Point(368, f.ClientSize.Height - 40), Width = 80 };
                f.Controls.AddRange(new Control[] { label, box, ok, cancel });
                if (linkText != null)
                {
                    var link = new LinkLabel { Text = linkText, Location = new Point(12, 86), AutoSize = true };
                    link.LinkClicked += (s, e) => Process.Start(linkUrl);
                    f.Controls.Add(link);
                }
                f.AcceptButton = ok;
                f.CancelButton = cancel;
                return f.ShowDialog(owner) == DialogResult.OK ? box.Text.Trim() : null;
            }
        }
    }

    class AdminForm : Form
    {
        const string RequestLabel = "app-anfrage";

        GitHubApi api;
        bool busy;
        List<Dictionary<string, object>> catalogApps = new List<Dictionary<string, object>>();

        readonly Label status;
        readonly TabPage requestsTab, appsTab;
        readonly ListView requests, appList;
        readonly AppEditor editor, appEditor;
        readonly SplitContainer split, appsSplit;
        readonly Button reload, logout, accept, reject, check, openIssue, save, checkApp, remove;

        static string RepoPath
        {
            get { return "/repos/" + BuildInfo.Repo; }
        }

        public AdminForm()
        {
            Text = "CreeStore Admin";
            Size = new Size(1080, 680);
            MinimumSize = new Size(820, 480);
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Segoe UI", 9.5f);
            Icon = Util.AppIcon() ?? Icon;

            // Kopfzeile
            var top = new Panel { Dock = DockStyle.Top, Height = 56, BackColor = Color.FromArgb(32, 35, 37) };
            var title = new Label
            {
                Text = "CreeStore Admin",
                Font = new Font("Segoe UI Semibold", 16f),
                ForeColor = Color.FromArgb(94, 190, 70),
                AutoSize = true,
                Location = new Point(14, 12)
            };
            reload = TopButton("Neu laden");
            reload.Click += async (s, e) => await ReloadAll();
            logout = TopButton("Abmelden");
            logout.Click += async (s, e) => { TokenStore.Clear(); api = null; await SignIn(true); };
            top.Resize += (s, e) =>
            {
                logout.Location = new Point(top.Width - logout.Width - 14, 13);
                reload.Location = new Point(logout.Left - reload.Width - 8, 13);
            };
            top.Controls.AddRange(new Control[] { title, reload, logout });

            status = new Label { Dock = DockStyle.Bottom, Height = 26, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(10, 0, 0, 0) };

            // Tab "Anfragen"
            requests = MakeList(new[] { "#", "Titel", "Von", "Datum" }, new[] { 50, 250, 110, 90 });
            requests.SelectedIndexChanged += (s, e) => ShowRequest();
            editor = new AppEditor { Dock = DockStyle.Fill };
            accept = ActionButton("✓ Annehmen", Color.FromArgb(94, 190, 70));
            accept.Click += (s, e) => Accept();
            reject = ActionButton("✕ Ablehnen", Color.FromArgb(220, 90, 80));
            reject.Click += (s, e) => Reject();
            check = ActionButton("Download prüfen", SystemColors.Control);
            check.Click += (s, e) => CheckDownload(editor);
            openIssue = ActionButton("Auf GitHub öffnen", SystemColors.Control);
            openIssue.Click += (s, e) =>
            {
                var issue = SelectedIssue();
                if (issue != null) Process.Start(Util.Str(issue, "html_url"));
            };
            var requestButtons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 48, Padding = new Padding(6, 6, 6, 0) };
            requestButtons.Controls.AddRange(new Control[] { accept, reject, check, openIssue });
            var hint = new Label
            {
                Dock = DockStyle.Top,
                Height = 30,
                Padding = new Padding(10, 8, 0, 0),
                ForeColor = Color.DimGray,
                Text = "Felder vor dem Annehmen bei Bedarf anpassen – so landet die App in apps.json."
            };

            split = new SplitContainer { Dock = DockStyle.Fill, FixedPanel = FixedPanel.Panel1 };
            split.Panel1.Controls.Add(requests);
            split.Panel2.Controls.Add(editor);
            split.Panel2.Controls.Add(hint);
            split.Panel2.Controls.Add(requestButtons);
            requestsTab = new TabPage("Anfragen");
            requestsTab.Controls.Add(split);

            // Tab "Apps im Store"
            appList = MakeList(new[] { "Name", "Quelle", "Art", "Version" }, new[] { 140, 200, 70, 60 });
            appList.SelectedIndexChanged += (s, e) =>
            {
                var app = SelectedApp();
                if (app == null) appEditor.Clear();
                else appEditor.SetApp(AppEditor.FromApp(app));
            };
            appEditor = new AppEditor { Dock = DockStyle.Fill };
            save = ActionButton("✓ Speichern", Color.FromArgb(94, 190, 70));
            save.Click += (s, e) => SaveApp();
            checkApp = ActionButton("Download prüfen", SystemColors.Control);
            checkApp.Click += (s, e) => CheckDownload(appEditor);
            remove = ActionButton("App entfernen", Color.FromArgb(220, 90, 80));
            remove.Click += (s, e) => RemoveApp();
            var appButtons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 48, Padding = new Padding(6, 6, 6, 0) };
            appButtons.Controls.AddRange(new Control[] { save, checkApp, remove });
            var appHint = new Label
            {
                Dock = DockStyle.Top,
                Height = 30,
                Padding = new Padding(10, 8, 0, 0),
                ForeColor = Color.DimGray,
                Text = "App links auswählen, Felder ändern, Speichern – der Store zeigt es sofort."
            };

            appsSplit = new SplitContainer { Dock = DockStyle.Fill, FixedPanel = FixedPanel.Panel1 };
            appsSplit.Panel1.Controls.Add(appList);
            appsSplit.Panel2.Controls.Add(appEditor);
            appsSplit.Panel2.Controls.Add(appHint);
            appsSplit.Panel2.Controls.Add(appButtons);
            appsTab = new TabPage("Apps im Store");
            appsTab.Controls.Add(appsSplit);

            var tabs = new TabControl { Dock = DockStyle.Fill, Padding = new Point(14, 5) };
            tabs.TabPages.Add(requestsTab);
            tabs.TabPages.Add(appsTab);

            Controls.Add(tabs);
            Controls.Add(top);
            Controls.Add(status);

            Load += (s, e) =>
            {
                split.SplitterDistance = 480;
                appsSplit.SplitterDistance = 480;
            };
            Shown += async (s, e) => await SignIn(false);
            SetBusy(true);
        }

        Button TopButton(string text)
        {
            var b = new Button
            {
                Text = text,
                Size = new Size(100, 30),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(52, 56, 60),
                ForeColor = Color.Gainsboro
            };
            b.FlatAppearance.BorderSize = 0;
            return b;
        }

        Button ActionButton(string text, Color color)
        {
            var b = new Button { Text = text, AutoSize = true, MinimumSize = new Size(110, 32), BackColor = color, Margin = new Padding(4, 0, 4, 0) };
            if (color != SystemColors.Control)
            {
                b.FlatStyle = FlatStyle.Flat;
                b.FlatAppearance.BorderSize = 0;
                b.ForeColor = Color.Black;
            }
            return b;
        }

        static ListView MakeList(string[] columns, int[] widths)
        {
            var lv = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                MultiSelect = false,
                HideSelection = false
            };
            for (int i = 0; i < columns.Length; i++) lv.Columns.Add(columns[i], widths[i]);
            return lv;
        }

        void SetStatus(string text)
        {
            status.Text = text;
        }

        void SetBusy(bool value)
        {
            busy = value;
            foreach (Control c in new Control[] { reload, logout, accept, reject, check, openIssue, save, checkApp, remove })
                c.Enabled = !value;
            UseWaitCursor = value;
        }

        // Führt Arbeit im Hintergrund aus und zeigt Fehler an
        async Task<bool> Run(string text, Action work)
        {
            if (busy) return false;
            SetBusy(true);
            SetStatus(text);
            string error = null;
            try
            {
                await Task.Run(work);
            }
            catch (Exception ex)
            {
                error = GitHubApi.Describe(ex);
            }
            SetBusy(false);
            if (error != null)
            {
                SetStatus("Fehler: " + error);
                MessageBox.Show(this, error, "CreeStore Admin", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            else
            {
                SetStatus("Fertig.");
            }
            return error == null;
        }

        // ---------- Anmeldung ----------

        async Task SignIn(bool forceDialog)
        {
            SetBusy(true);
            SetStatus("Melde bei GitHub an…");
            string token = forceDialog ? null : TokenStore.Load();
            bool fromGit = false;
            if (token == null && !forceDialog)
            {
                token = await Task.Run(() => TokenStore.FromGit());
                fromGit = token != null;
            }

            while (true)
            {
                if (token == null)
                {
                    token = InputDialog.Ask(this, "GitHub-Anmeldung",
                        "GitHub-Token mit Schreibrechten für " + BuildInfo.Repo + " (Berechtigung „repo“):",
                        true, "Token auf GitHub erstellen",
                        "https://github.com/settings/tokens/new?scopes=repo&description=CreeStore%20Admin");
                    if (string.IsNullOrEmpty(token))
                    {
                        Close();
                        return;
                    }
                }

                var candidate = new GitHubApi(token);
                string error = null;
                bool canPush = false;
                try
                {
                    var repo = (Dictionary<string, object>)await Task.Run(() => candidate.Get(RepoPath));
                    object perms;
                    if (repo.TryGetValue("permissions", out perms) && perms is Dictionary<string, object>)
                    {
                        object push;
                        canPush = ((Dictionary<string, object>)perms).TryGetValue("push", out push) && Equals(push, true);
                    }
                }
                catch (Exception ex)
                {
                    error = GitHubApi.Describe(ex);
                }

                if (error == null && canPush)
                {
                    api = candidate;
                    if (!fromGit) TokenStore.Save(token);
                    break;
                }

                TokenStore.Clear();
                MessageBox.Show(this, error ?? "Dieses GitHub-Konto darf nicht in " + BuildInfo.Repo + " schreiben.",
                    "Anmeldung fehlgeschlagen", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                token = null;
                fromGit = false;
            }

            // Label für Anfragen anlegen (gibt es das schon, meldet GitHub nur einen Fehler)
            await Task.Run(() =>
            {
                try
                {
                    api.Send("POST", RepoPath + "/labels", new Dictionary<string, object>
                    {
                        { "name", RequestLabel },
                        { "color", "5ebe46" },
                        { "description", "Anfrage zum Veröffentlichen im CreeStore" }
                    });
                }
                catch (WebException) { }
            });

            SetBusy(false);
            await ReloadAll();
        }

        // ---------- Laden ----------

        async Task ReloadAll()
        {
            List<Dictionary<string, object>> issues = null;
            Catalog catalog = null;
            if (!await Run("Lade Anfragen und Apps…", () => { issues = FetchRequests(); catalog = Catalog.Load(api); }))
                return;

            requests.BeginUpdate();
            requests.Items.Clear();
            foreach (var issue in issues)
            {
                var user = issue["user"] as Dictionary<string, object>;
                DateTime created;
                DateTime.TryParse(Util.Str(issue, "created_at"), out created);
                var item = new ListViewItem(new[]
                {
                    "#" + Util.Str(issue, "number"),
                    Regex.Replace(Util.Str(issue, "title") ?? "", @"^\[App\]\s*", "", RegexOptions.IgnoreCase),
                    user != null ? Util.Str(user, "login") : "",
                    created == DateTime.MinValue ? "" : created.ToLocalTime().ToString("dd.MM.yyyy")
                }) { Tag = issue };
                requests.Items.Add(item);
            }
            requests.EndUpdate();
            editor.Clear();

            catalogApps = catalog.Apps;
            appList.BeginUpdate();
            appList.Items.Clear();
            foreach (var app in catalog.Apps)
            {
                string source = Util.Str(app, "repo");
                source = source != null ? "github.com/" + source : Util.Str(app, "url");
                appList.Items.Add(new ListViewItem(new[]
                {
                    Util.Str(app, "name"),
                    source,
                    Util.Str(app, "type") == "portable" ? "Portabel" : "Installer",
                    Util.Str(app, "version") ?? "neueste"
                }) { Tag = app });
            }
            appList.EndUpdate();
            appEditor.Clear();

            requestsTab.Text = "Anfragen (" + issues.Count + ")";
            appsTab.Text = "Apps im Store (" + catalog.Apps.Count + ")";
            SetStatus(issues.Count + " offene Anfragen · " + catalog.Apps.Count + " Apps im Store · github.com/" + BuildInfo.Repo);
        }

        List<Dictionary<string, object>> FetchRequests()
        {
            var result = new List<Dictionary<string, object>>();
            var arr = api.Get(RepoPath + "/issues?state=open&per_page=100") as object[];
            if (arr == null) return result;
            foreach (object o in arr)
            {
                var issue = o as Dictionary<string, object>;
                if (issue == null || issue.ContainsKey("pull_request")) continue;
                if (IsRequest(issue)) result.Add(issue);
            }
            return result;
        }

        static bool IsRequest(Dictionary<string, object> issue)
        {
            if ((Util.Str(issue, "title") ?? "").StartsWith("[App]", StringComparison.OrdinalIgnoreCase)) return true;
            var labels = issue.ContainsKey("labels") ? issue["labels"] as object[] : null;
            if (labels != null)
                foreach (object l in labels)
                {
                    var label = l as Dictionary<string, object>;
                    if (label != null && Util.Str(label, "name") == RequestLabel) return true;
                }
            return false;
        }

        // ---------- Anfragen ----------

        Dictionary<string, object> SelectedIssue()
        {
            return requests.SelectedItems.Count > 0 ? requests.SelectedItems[0].Tag as Dictionary<string, object> : null;
        }

        void ShowRequest()
        {
            var issue = SelectedIssue();
            if (issue == null) editor.Clear();
            else editor.SetApp(ParseIssue(issue));
        }

        // Liest die Felder aus dem Issue-Formular ("### Überschrift" + Wert)
        static Dictionary<string, string> ParseIssue(Dictionary<string, object> issue)
        {
            string body = Util.Str(issue, "body") ?? "";
            var form = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string heading = null;
            var value = new StringBuilder();
            foreach (string line in (body + "\n### ").Replace("\r", "").Split('\n'))
            {
                if (line.StartsWith("### "))
                {
                    if (heading != null)
                    {
                        string v = value.ToString().Trim();
                        form[heading] = v == "_No response_" ? "" : v;
                    }
                    heading = line.Substring(4).Trim();
                    value.Clear();
                }
                else if (heading != null)
                {
                    value.AppendLine(line);
                }
            }

            var f = new Dictionary<string, string>();
            string title = Regex.Replace(Util.Str(issue, "title") ?? "", @"^\[App\]\s*", "", RegexOptions.IgnoreCase);
            f["name"] = Pick(form, "App-Name") ?? title;
            f["description"] = form.Count > 1 ? Pick(form, "Beschreibung") : body.Trim();
            f["repo"] = AppEditor.NormalizeRepo(Pick(form, "GitHub-Repo") ?? "");
            f["asset"] = Pick(form, "Datei im Release");
            f["url"] = Pick(form, "Direkter Download-Link");
            f["args"] = Pick(form, "Parameter für stille Installation");
            f["type"] = (Pick(form, "Art") ?? "").StartsWith("Portabel", StringComparison.OrdinalIgnoreCase) ? "portable" : "";
            return f;
        }

        static string Pick(Dictionary<string, string> form, string key)
        {
            string v;
            return form.TryGetValue(key, out v) && v.Length > 0 ? v : null;
        }

        static string IssuePath(Dictionary<string, object> issue)
        {
            return RepoPath + "/issues/" + Util.Str(issue, "number");
        }

        void CloseIssue(Dictionary<string, object> issue, string comment, string reason)
        {
            api.Send("POST", IssuePath(issue) + "/comments", new Dictionary<string, object> { { "body", comment } });
            api.Send("PATCH", IssuePath(issue), new Dictionary<string, object> { { "state", "closed" }, { "state_reason", reason } });
        }

        async void Accept()
        {
            var issue = SelectedIssue();
            if (issue == null) return;
            Dictionary<string, object> app;
            string invalid = editor.TryGetApp(out app);
            if (invalid != null)
            {
                MessageBox.Show(this, invalid, "Annehmen", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            string name = (string)app["name"];
            bool exists = catalogApps.Exists(a => string.Equals(Util.Str(a, "name"), name, StringComparison.OrdinalIgnoreCase));
            string question = exists
                ? "„" + name + "“ gibt es schon im Store. Den Eintrag ersetzen?"
                : "„" + name + "“ im CreeStore veröffentlichen?";
            if (MessageBox.Show(this, question, "Annehmen", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;

            bool ok = await Run(name + " wird veröffentlicht…", () =>
            {
                Catalog catalog = Catalog.Load(api);
                int i = catalog.IndexOf(name);
                if (i >= 0) catalog.Apps[i] = app;
                else catalog.Apps.Add(app);
                catalog.Save(api, "App veröffentlicht: " + name + " (#" + Util.Str(issue, "number") + ")");
                CloseIssue(issue, "✅ Angenommen! **" + name + "** ist jetzt im CreeStore.", "completed");
            });
            if (ok)
            {
                await ReloadAll();
                SetStatus(name + " ist jetzt im Store.");
            }
        }

        async void Reject()
        {
            var issue = SelectedIssue();
            if (issue == null) return;
            string reason = InputDialog.Ask(this, "Anfrage ablehnen",
                "Grund für die Ablehnung (wird als Kommentar auf GitHub gepostet, darf leer sein):", false, null, null);
            if (reason == null) return;

            string comment = "❌ Abgelehnt." + (reason.Length > 0 ? "\n\nGrund: " + reason : "");
            if (await Run("Lehne Anfrage ab…", () => CloseIssue(issue, comment, "not_planned")))
            {
                await ReloadAll();
                SetStatus("Anfrage #" + Util.Str(issue, "number") + " abgelehnt.");
            }
        }

        async void CheckDownload(AppEditor source)
        {
            Dictionary<string, object> app;
            string invalid = source.TryGetApp(out app);
            if (invalid != null)
            {
                MessageBox.Show(this, invalid, "Download prüfen", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            string result = null;
            bool ok = await Run("Prüfe Download…", () =>
            {
                if (app.ContainsKey("repo"))
                {
                    ReleaseAsset found = Util.LatestAsset((string)app["repo"], app.ContainsKey("asset") ? (string)app["asset"] : null);
                    result = "Gefunden: " + found.FileName + "\nVersion: " + found.Version;
                }
                else
                {
                    var req = (HttpWebRequest)WebRequest.Create((string)app["url"]);
                    req.Method = "HEAD";
                    req.UserAgent = "CreeStore/" + BuildInfo.Version;
                    using (var resp = (HttpWebResponse)req.GetResponse())
                        result = "Link funktioniert (" + Math.Round(resp.ContentLength / 1048576.0, 1) + " MB).";
                }
            });
            if (ok) MessageBox.Show(this, result, "Download prüfen", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        // ---------- Apps bearbeiten & entfernen ----------

        Dictionary<string, object> SelectedApp()
        {
            return appList.SelectedItems.Count > 0 ? appList.SelectedItems[0].Tag as Dictionary<string, object> : null;
        }

        async void SaveApp()
        {
            var selected = SelectedApp();
            if (selected == null) return;
            Dictionary<string, object> edited;
            string invalid = appEditor.TryGetApp(out edited);
            if (invalid != null)
            {
                MessageBox.Show(this, invalid, "Speichern", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            string oldName = Util.Str(selected, "name");
            string newName = (string)edited["name"];
            bool renamed = !string.Equals(oldName, newName, StringComparison.OrdinalIgnoreCase);
            if (renamed && catalogApps.Exists(a => string.Equals(Util.Str(a, "name"), newName, StringComparison.OrdinalIgnoreCase)))
            {
                MessageBox.Show(this, "Es gibt schon eine App namens „" + newName + "“.", "Speichern", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            bool ok = await Run(newName + " wird gespeichert…", () =>
            {
                Catalog catalog = Catalog.Load(api);
                int i = catalog.IndexOf(oldName);
                if (i < 0) throw new Exception(oldName + " ist nicht mehr in apps.json.");
                catalog.Apps[i] = AppEditor.Merge(catalog.Apps[i], edited);
                catalog.Save(api, "App bearbeitet: " + (renamed ? oldName + " → " + newName : newName));
            });
            if (ok)
            {
                await ReloadAll();
                foreach (ListViewItem item in appList.Items)
                    if (item.Text == newName) item.Selected = true;
                SetStatus(newName + " wurde gespeichert.");
            }
        }

        async void RemoveApp()
        {
            if (appList.SelectedItems.Count == 0) return;
            string name = appList.SelectedItems[0].Text;
            if (MessageBox.Show(this, "„" + name + "“ aus dem Store entfernen?\n\nWer die App schon installiert hat, behält sie.",
                    "App entfernen", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                return;

            bool ok = await Run(name + " wird entfernt…", () =>
            {
                Catalog catalog = Catalog.Load(api);
                int i = catalog.IndexOf(name);
                if (i < 0) throw new Exception(name + " ist nicht mehr in apps.json.");
                catalog.Apps.RemoveAt(i);
                catalog.Save(api, "App entfernt: " + name);
            });
            if (ok)
            {
                await ReloadAll();
                SetStatus(name + " wurde aus dem Store entfernt.");
            }
        }
    }

    static class AdminProgram
    {
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new AdminForm());
        }
    }
}
