package de.creestore;

import org.json.JSONArray;
import org.json.JSONObject;

import java.io.ByteArrayOutputStream;
import java.io.File;
import java.io.FileOutputStream;
import java.io.IOException;
import java.io.InputStream;
import java.io.OutputStream;
import java.net.HttpURLConnection;
import java.net.URL;
import java.net.URLDecoder;
import java.net.URLEncoder;
import java.util.ArrayList;
import java.util.List;
import java.util.regex.Matcher;
import java.util.regex.Pattern;

// Alles, was mit GitHub redet: Katalog, neuestes Release, Download
final class GitHub {
    // Ein Download aus dem neuesten GitHub-Release eines Repos
    static final class ReleaseAsset {
        String url;
        String version;
        String fileName;
    }

    interface Progress {
        void onProgress(int percent);
    }

    private GitHub() {
    }

    static String userAgent() {
        return "CreeStore-Android/" + BuildConfig.VERSION_NAME;
    }

    static HttpURLConnection open(String url) throws IOException {
        HttpURLConnection c = (HttpURLConnection) new URL(url).openConnection();
        c.setRequestProperty("User-Agent", userAgent());
        c.setConnectTimeout(15000);
        c.setReadTimeout(30000);
        return c;
    }

    static String get(String url, String accept) throws IOException {
        HttpURLConnection c = open(url);
        try {
            if (accept != null) c.setRequestProperty("Accept", accept);
            int code = c.getResponseCode();
            if (code >= 400) throw new IOException("HTTP " + code + " bei " + url);
            try (InputStream in = c.getInputStream()) {
                ByteArrayOutputStream out = new ByteArrayOutputStream();
                byte[] buf = new byte[16384];
                int n;
                while ((n = in.read(buf)) > 0) out.write(buf, 0, n);
                return out.toString("UTF-8");
            }
        } finally {
            c.disconnect();
        }
    }

    // Lädt die Android-Apps aus apps.json (Schlüssel "android"; "apps" sind die Windows-Programme)
    static List<StoreApp> loadCatalog() throws Exception {
        String json;
        try {
            // Die API ist sofort aktuell, raw.githubusercontent.com cached bis zu 5 Minuten
            json = get("https://api.github.com/repos/" + BuildConfig.REPO + "/contents/apps.json?ref=" + BuildConfig.BRANCH,
                    "application/vnd.github.raw");
        } catch (IOException e) {
            // z.B. API-Limit erreicht
            json = get(catalogUrl(), null);
        }

        JSONObject root = new JSONObject(json);
        JSONArray arr = root.optJSONArray("android");
        List<StoreApp> apps = new ArrayList<>();
        if (arr == null) return apps;
        for (int i = 0; i < arr.length(); i++) {
            JSONObject d = arr.optJSONObject(i);
            if (d == null) continue;
            StoreApp a = new StoreApp();
            a.name = str(d, "name");
            a.description = str(d, "description");
            a.author = str(d, "author");
            a.repo = str(d, "repo");
            a.url = str(d, "url");
            a.asset = str(d, "asset");
            a.exclude = str(d, "exclude");
            a.packageName = str(d, "package");
            a.version = str(d, "version");
            if (a.name == null || (a.repo == null && a.url == null)) continue;
            apps.add(a);
        }
        return apps;
    }

    static String catalogUrl() {
        return "https://raw.githubusercontent.com/" + BuildConfig.REPO + "/" + BuildConfig.BRANCH + "/apps.json";
    }

    private static String str(JSONObject d, String key) {
        if (!d.has(key) || d.isNull(key)) return null;
        String s = d.optString(key, "").trim();
        return s.isEmpty() ? null : s;
    }

    // Sucht die passende Datei im neuesten Release. Zuerst über github.com,
    // weil die API ohne Anmeldung nur 60 Anfragen pro Stunde erlaubt.
    static ReleaseAsset latestAsset(String repo, String pattern, String exclude) throws Exception {
        try {
            return latestAssetWeb(repo, pattern, exclude);
        } catch (IOException e) {
            return latestAssetApi(repo, pattern, exclude);
        }
    }

    private static boolean wanted(String name, String pattern, String exclude) {
        return wildMatch(name, pattern) && (exclude == null || !wildMatch(name, exclude));
    }

    private static ReleaseAsset latestAssetWeb(String repo, String pattern, String exclude) throws Exception {
        // /releases/latest leitet auf /releases/tag/<tag> weiter
        HttpURLConnection c = open("https://github.com/" + repo + "/releases/latest");
        String location;
        try {
            c.setRequestMethod("HEAD");
            c.setInstanceFollowRedirects(false);
            int code = c.getResponseCode();
            // z.B. 429 – dann über die API versuchen
            if (code >= 400) throw new IOException("HTTP " + code);
            location = c.getHeaderField("Location");
        } finally {
            c.disconnect();
        }
        if (location == null) location = "";

        int i = location.indexOf("/releases/tag/");
        if (i < 0) throw new Exception(repo + " hat noch kein Release.");
        String tag = URLDecoder.decode(location.substring(i + "/releases/tag/".length()), "UTF-8");

        String html = get("https://github.com/" + repo + "/releases/expanded_assets/"
                + URLEncoder.encode(tag, "UTF-8").replace("+", "%20"), null);

        Matcher m = Pattern.compile("href=\"(/[^\"]+/releases/download/[^\"]+)\"").matcher(html);
        while (m.find()) {
            String href = m.group(1).replace("&amp;", "&");
            String name = URLDecoder.decode(href.substring(href.lastIndexOf('/') + 1), "UTF-8");
            if (wanted(name, pattern, exclude)) {
                ReleaseAsset r = new ReleaseAsset();
                r.url = "https://github.com" + href;
                r.version = tag;
                r.fileName = name;
                return r;
            }
        }
        throw new Exception(noMatch(repo, pattern));
    }

    private static ReleaseAsset latestAssetApi(String repo, String pattern, String exclude) throws Exception {
        JSONObject release = new JSONObject(get("https://api.github.com/repos/" + repo + "/releases/latest", null));
        JSONArray assets = release.optJSONArray("assets");
        if (assets != null) {
            for (int i = 0; i < assets.length(); i++) {
                JSONObject asset = assets.getJSONObject(i);
                String name = asset.optString("name", null);
                if (name != null && wanted(name, pattern, exclude)) {
                    ReleaseAsset r = new ReleaseAsset();
                    r.url = asset.optString("browser_download_url");
                    r.version = release.optString("tag_name");
                    r.fileName = name;
                    return r;
                }
            }
        }
        throw new Exception(noMatch(repo, pattern));
    }

    private static String noMatch(String repo, String pattern) {
        return "Keine passende Datei (" + (pattern != null ? pattern : "*.apk") + ") im neuesten Release von " + repo + " gefunden.";
    }

    static boolean wildMatch(String name, String pattern) {
        if (pattern == null || pattern.isEmpty()) pattern = "*.apk";
        if (pattern.indexOf('*') < 0 && pattern.indexOf('?') < 0) pattern = "*" + pattern;
        StringBuilder rx = new StringBuilder("^");
        for (char ch : pattern.toCharArray()) {
            if (ch == '*') rx.append(".*");
            else if (ch == '?') rx.append('.');
            else rx.append(Pattern.quote(String.valueOf(ch)));
        }
        rx.append('$');
        return Pattern.compile(rx.toString(), Pattern.CASE_INSENSITIVE).matcher(name).matches();
    }

    static void download(String url, File target, Progress progress) throws IOException {
        HttpURLConnection c = open(url);
        try {
            int code = c.getResponseCode();
            if (code >= 400) throw new IOException("HTTP " + code + " beim Download");
            long total = c.getContentLengthLong();
            try (InputStream in = c.getInputStream(); OutputStream out = new FileOutputStream(target)) {
                byte[] buf = new byte[65536];
                long done = 0;
                int last = -1, n;
                while ((n = in.read(buf)) > 0) {
                    out.write(buf, 0, n);
                    done += n;
                    if (total > 0) {
                        int p = (int) (done * 100 / total);
                        if (p != last) progress.onProgress(last = p);
                    }
                }
            }
        } finally {
            c.disconnect();
        }
    }

    static String clean(String version) {
        if (version == null) return "";
        version = version.trim();
        while (version.startsWith("v") || version.startsWith("V")) version = version.substring(1);
        return version;
    }

    static boolean isNewer(String latest, String current) {
        int[] a = parse(clean(latest)), b = parse(clean(current));
        if (a == null || b == null) return !clean(latest).equalsIgnoreCase(clean(current));
        for (int i = 0; i < Math.max(a.length, b.length); i++) {
            int x = i < a.length ? a[i] : 0, y = i < b.length ? b[i] : 0;
            if (x != y) return x > y;
        }
        return false;
    }

    private static int[] parse(String v) {
        String[] parts = v.split("\\.");
        int[] r = new int[parts.length];
        try {
            for (int i = 0; i < parts.length; i++) r[i] = Integer.parseInt(parts[i]);
        } catch (NumberFormatException e) {
            return null;
        }
        return r;
    }
}
