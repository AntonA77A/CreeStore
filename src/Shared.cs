using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace CreeStore
{
    // Ein Download aus dem neuesten GitHub-Release eines Repos
    class ReleaseAsset
    {
        public string Url;
        public string Version;
        public string FileName;
    }

    static class Util
    {
        public const string AppName = "CreeStore";
        public const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\CreeStore";

        static Util()
        {
            // GitHub braucht TLS 1.2
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
        }

        static string LocalAppData
        {
            get { return Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData); }
        }

        // Hier liegt CreeStore.exe selbst
        public static string InstallDir
        {
            get { return Path.Combine(LocalAppData, "Programs", AppName); }
        }

        // Hier liegen installed.json, Downloads und portable Apps
        public static string DataDir
        {
            get { return Path.Combine(LocalAppData, AppName); }
        }

        public static string StartMenuDir
        {
            get { return Environment.GetFolderPath(Environment.SpecialFolder.Programs); }
        }

        public static string DesktopDir
        {
            get { return Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory); }
        }

        public static WebClient NewClient()
        {
            var wc = new WebClient();
            wc.Headers[HttpRequestHeader.UserAgent] = "CreeStore/" + BuildInfo.Version;
            wc.Encoding = Encoding.UTF8;
            return wc;
        }

        public static object ParseJson(string json)
        {
            return new JavaScriptSerializer().DeserializeObject(json);
        }

        public static string ToJson(object value)
        {
            return new JavaScriptSerializer().Serialize(value);
        }

        public static string Str(Dictionary<string, object> d, string key)
        {
            object v;
            return d.TryGetValue(key, out v) && v != null ? v.ToString() : null;
        }

        public static string Clean(string version)
        {
            return (version ?? "").Trim().TrimStart('v', 'V');
        }

        public static bool IsNewer(string latest, string current)
        {
            Version a, b;
            if (Version.TryParse(Clean(latest), out a) && Version.TryParse(Clean(current), out b))
                return a > b;
            return !string.Equals(Clean(latest), Clean(current), StringComparison.OrdinalIgnoreCase);
        }

        public static string SafeName(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return name;
        }

        // Fragt die GitHub-API nach dem neuesten Release und sucht die passende Datei
        public static ReleaseAsset LatestAsset(string repo, string pattern)
        {
            string json;
            using (var wc = NewClient())
                json = wc.DownloadString("https://api.github.com/repos/" + repo + "/releases/latest");

            var release = (Dictionary<string, object>)ParseJson(json);
            var assets = release.ContainsKey("assets") ? release["assets"] as object[] : null;
            if (assets != null)
            {
                foreach (object o in assets)
                {
                    var asset = (Dictionary<string, object>)o;
                    string name = Str(asset, "name");
                    if (name != null && WildMatch(name, pattern))
                    {
                        return new ReleaseAsset
                        {
                            Url = Str(asset, "browser_download_url"),
                            Version = Str(release, "tag_name"),
                            FileName = name
                        };
                    }
                }
            }
            throw new Exception("Keine passende Datei (" + (pattern ?? "*.exe") + ") im neuesten Release von " + repo + " gefunden.");
        }

        static bool WildMatch(string name, string pattern)
        {
            if (string.IsNullOrEmpty(pattern)) pattern = "*.exe";
            if (pattern.IndexOf('*') < 0 && pattern.IndexOf('?') < 0) pattern = "*" + pattern;
            string rx = "^" + Regex.Escape(pattern).Replace(@"\*", ".*").Replace(@"\?", ".") + "$";
            return Regex.IsMatch(name, rx, RegexOptions.IgnoreCase);
        }

        // Das Icon mit allen Größen; ExtractAssociatedIcon liefert nur 32px und sieht in der Taskleiste matschig aus
        public static System.Drawing.Icon AppIcon()
        {
            try
            {
                using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream("app.ico"))
                    return s == null ? null : new System.Drawing.Icon(s);
            }
            catch
            {
                return null;
            }
        }

        // .lnk-Datei über WScript.Shell anlegen
        public static void CreateShortcut(string lnkPath, string target, string workDir)
        {
            Type shellType = Type.GetTypeFromProgID("WScript.Shell");
            object shell = Activator.CreateInstance(shellType);
            object lnk = shellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { lnkPath });
            Type lnkType = lnk.GetType();
            lnkType.InvokeMember("TargetPath", BindingFlags.SetProperty, null, lnk, new object[] { target });
            lnkType.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, lnk, new object[] { workDir });
            lnkType.InvokeMember("IconLocation", BindingFlags.SetProperty, null, lnk, new object[] { target + ",0" });
            lnkType.InvokeMember("Save", BindingFlags.InvokeMethod, null, lnk, null);
        }
    }
}
