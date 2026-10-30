package de.creestore;

import android.app.Activity;
import android.app.AlertDialog;
import android.content.Context;
import android.content.Intent;
import android.content.SharedPreferences;
import android.content.pm.PackageInfo;
import android.content.pm.PackageManager;
import android.graphics.Color;
import android.graphics.Typeface;
import android.graphics.drawable.GradientDrawable;
import android.net.Uri;
import android.os.Build;
import android.os.Bundle;
import android.os.Handler;
import android.os.Looper;
import android.provider.Settings;
import android.text.Editable;
import android.text.TextUtils;
import android.text.TextWatcher;
import android.util.TypedValue;
import android.view.Gravity;
import android.view.View;
import android.view.ViewGroup;
import android.widget.Button;
import android.widget.EditText;
import android.widget.LinearLayout;
import android.widget.ScrollView;
import android.widget.TextView;
import android.widget.Toast;

import java.io.File;
import java.util.ArrayList;
import java.util.List;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;

public class MainActivity extends Activity {
    static final int BG = Color.rgb(24, 26, 27);
    static final int BAR = Color.rgb(32, 35, 37);
    static final int CARD_BG = Color.rgb(38, 41, 44);
    static final int GREEN = Color.rgb(94, 190, 70);
    static final int FG = Color.rgb(225, 228, 230);
    static final int MUTED = Color.rgb(145, 150, 155);
    static final int GREY_BUTTON = Color.rgb(52, 56, 60);

    // Merkt sich installierte Apps: "pkg:<Name>" = Paketname, "ver:<Name>" = installierte Version
    static final String PREFS = "installed";
    static final String SELF = "\u0000CreeStore";

    private static MainActivity current;

    private final ExecutorService worker = Executors.newSingleThreadExecutor();
    private final Handler main = new Handler(Looper.getMainLooper());
    private final List<StoreApp> apps = new ArrayList<>();
    private LinearLayout list;
    private TextView status;
    private EditText search;
    private Button updateButton;
    private String busyApp;

    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);
        current = this;
        setContentView(buildLayout());
        loadCatalog();
        checkSelfUpdate();
    }

    @Override
    protected void onResume() {
        super.onResume();
        // z.B. nach Deinstallieren in den Systemeinstellungen
        for (StoreApp a : apps) refreshCard(a);
    }

    @Override
    protected void onDestroy() {
        if (current == this) current = null;
        worker.shutdownNow();
        super.onDestroy();
    }

    private int dp(float v) {
        return (int) TypedValue.applyDimension(TypedValue.COMPLEX_UNIT_DIP, v, getResources().getDisplayMetrics());
    }

    private View buildLayout() {
        LinearLayout root = new LinearLayout(this);
        root.setOrientation(LinearLayout.VERTICAL);
        root.setBackgroundColor(BG);
        root.setFitsSystemWindows(true);

        LinearLayout bar = new LinearLayout(this);
        bar.setOrientation(LinearLayout.HORIZONTAL);
        bar.setGravity(Gravity.CENTER_VERTICAL);
        bar.setBackgroundColor(BAR);
        bar.setPadding(dp(16), dp(10), dp(8), dp(10));

        TextView title = new TextView(this);
        title.setText("CreeStore");
        title.setTextColor(Color.WHITE);
        title.setTextSize(20);
        title.setTypeface(Typeface.DEFAULT_BOLD);
        bar.addView(title, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1));

        updateButton = makeButton("Store-Update");
        updateButton.setVisibility(View.GONE);
        updateButton.setOnClickListener(v -> selfUpdate());
        bar.addView(updateButton);

        Button submit = makeButton("Einreichen");
        submit.setBackground(rounded(GREY_BUTTON));
        submit.setTextColor(FG);
        submit.setOnClickListener(v -> openUrl("https://github.com/" + BuildConfig.REPO + "/issues/new?template=app-anfrage.yml"));
        bar.addView(submit);

        Button reload = makeButton("↻");
        reload.setBackground(rounded(GREY_BUTTON));
        reload.setTextColor(FG);
        reload.setOnClickListener(v -> loadCatalog());
        bar.addView(reload);
        root.addView(bar);

        search = new EditText(this);
        search.setHint("Suchen…");
        search.setSingleLine(true);
        search.setTextColor(FG);
        search.setHintTextColor(MUTED);
        search.setBackground(rounded(CARD_BG));
        search.setPadding(dp(14), dp(10), dp(14), dp(10));
        search.addTextChangedListener(new TextWatcher() {
            public void beforeTextChanged(CharSequence s, int a, int b, int c) {
            }

            public void onTextChanged(CharSequence s, int a, int b, int c) {
            }

            public void afterTextChanged(Editable s) {
                applyFilter();
            }
        });
        LinearLayout.LayoutParams sp = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT);
        sp.setMargins(dp(12), dp(12), dp(12), dp(4));
        root.addView(search, sp);

        ScrollView scroll = new ScrollView(this);
        list = new LinearLayout(this);
        list.setOrientation(LinearLayout.VERTICAL);
        list.setPadding(dp(12), dp(4), dp(12), dp(12));
        scroll.addView(list);
        root.addView(scroll, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, 0, 1));

        status = new TextView(this);
        status.setTextColor(MUTED);
        status.setTextSize(12);
        status.setBackgroundColor(BAR);
        status.setPadding(dp(16), dp(8), dp(16), dp(8));
        status.setSingleLine(true);
        status.setEllipsize(TextUtils.TruncateAt.END);
        root.addView(status);
        return root;
    }

    private GradientDrawable rounded(int color) {
        GradientDrawable d = new GradientDrawable();
        d.setColor(color);
        d.setCornerRadius(dp(8));
        return d;
    }

    private Button makeButton(String text) {
        Button b = new Button(this);
        b.setText(text);
        b.setAllCaps(false);
        b.setTextColor(Color.BLACK);
        b.setBackground(rounded(GREEN));
        b.setMinWidth(0);
        b.setMinimumWidth(0);
        b.setMinHeight(0);
        b.setMinimumHeight(0);
        b.setPadding(dp(14), dp(8), dp(14), dp(8));
        LinearLayout.LayoutParams lp = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.WRAP_CONTENT, ViewGroup.LayoutParams.WRAP_CONTENT);
        lp.setMargins(dp(4), 0, dp(4), 0);
        b.setLayoutParams(lp);
        return b;
    }

    // ---------- Katalog von GitHub ----------

    private void loadCatalog() {
        if (busyApp != null) return;
        status.setText("Lade Apps von GitHub…");
        list.removeAllViews();
        apps.clear();
        worker.execute(() -> {
            try {
                List<StoreApp> loaded = GitHub.loadCatalog();
                main.post(() -> {
                    for (StoreApp a : loaded) {
                        apps.add(a);
                        list.addView(makeCard(a));
                    }
                    if (loaded.isEmpty()) showMessage("In apps.json sind noch keine Android-Apps eingetragen.");
                    status.setText(apps.size() + " Apps · github.com/" + BuildConfig.REPO + " · CreeStore v" + BuildConfig.VERSION_NAME);
                    applyFilter();
                });
            } catch (Exception e) {
                main.post(() -> {
                    status.setText("Fehler beim Laden: " + e.getMessage());
                    showMessage("Die App-Liste konnte nicht von GitHub geladen werden.\n\n" + GitHub.catalogUrl() + "\n\n" + e.getMessage());
                });
            }
        });
    }

    private void showMessage(String text) {
        TextView t = new TextView(this);
        t.setText(text);
        t.setTextColor(MUTED);
        t.setPadding(dp(8), dp(16), dp(8), dp(8));
        list.addView(t);
    }

    private View makeCard(StoreApp a) {
        LinearLayout card = new LinearLayout(this);
        card.setOrientation(LinearLayout.VERTICAL);
        card.setBackground(rounded(CARD_BG));
        card.setPadding(dp(14), dp(12), dp(14), dp(12));
        LinearLayout.LayoutParams cp = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT);
        cp.setMargins(0, dp(8), 0, 0);
        card.setLayoutParams(cp);

        TextView name = new TextView(this);
        name.setText(a.name);
        name.setTextColor(Color.WHITE);
        name.setTextSize(17);
        name.setTypeface(Typeface.DEFAULT_BOLD);
        card.addView(name);

        String author = a.author != null ? a.author : (a.repo != null ? a.repo.split("/")[0] : "");
        TextView meta = new TextView(this);
        meta.setText(author + (a.version != null ? "  ·  v" + GitHub.clean(a.version) : ""));
        meta.setTextColor(MUTED);
        meta.setTextSize(12);
        card.addView(meta);

        TextView desc = new TextView(this);
        desc.setText(a.description != null ? a.description : "");
        desc.setTextColor(FG);
        desc.setPadding(0, dp(6), 0, dp(6));
        card.addView(desc);

        LinearLayout row = new LinearLayout(this);
        row.setOrientation(LinearLayout.HORIZONTAL);
        row.setGravity(Gravity.CENTER_VERTICAL);
        a.status = new TextView(this);
        a.status.setTextColor(GREEN);
        a.status.setSingleLine(true);
        a.status.setEllipsize(TextUtils.TruncateAt.END);
        row.addView(a.status, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1));

        a.uninstallButton = makeButton("Deinstallieren");
        a.uninstallButton.setBackground(rounded(GREY_BUTTON));
        a.uninstallButton.setTextColor(FG);
        a.uninstallButton.setOnClickListener(v -> uninstall(a));
        row.addView(a.uninstallButton);

        a.button = makeButton("Installieren");
        a.button.setOnClickListener(v -> onAppButton(a));
        row.addView(a.button);
        card.addView(row);

        a.card = card;
        refreshCard(a);
        return card;
    }

    private String packageOf(StoreApp a) {
        return a.packageName != null ? a.packageName : prefs(this).getString("pkg:" + a.name, null);
    }

    private PackageInfo installedInfo(StoreApp a) {
        String pkg = packageOf(a);
        if (pkg == null) return null;
        try {
            return getPackageManager().getPackageInfo(pkg, 0);
        } catch (PackageManager.NameNotFoundException e) {
            return null;
        }
    }

    private void refreshCard(StoreApp a) {
        if (a.button == null) return;
        PackageInfo info = installedInfo(a);
        boolean installed = info != null;
        a.uninstallButton.setVisibility(installed ? View.VISIBLE : View.GONE);
        if (a.name.equals(busyApp)) return;

        String v = prefs(this).getString("ver:" + a.name, null);
        if (installed && TextUtils.isEmpty(v)) v = info.versionName;

        a.button.setEnabled(true);
        if (!installed) {
            a.status.setText("");
            setButton(a.button, "Installieren", true);
        } else if (!TextUtils.isEmpty(v) && a.version != null && GitHub.isNewer(a.version, v)) {
            a.status.setText("Update verfügbar");
            setButton(a.button, "Aktualisieren", true);
        } else {
            a.status.setText("✓ Installiert" + (TextUtils.isEmpty(v) ? "" : " v" + GitHub.clean(v)));
            setButton(a.button, "Öffnen", false);
        }
    }

    private void setButton(Button b, String text, boolean primary) {
        b.setText(text);
        b.setBackground(rounded(primary ? GREEN : GREY_BUTTON));
        b.setTextColor(primary ? Color.BLACK : FG);
    }

    private void applyFilter() {
        String q = search.getText().toString().trim().toLowerCase();
        for (StoreApp a : apps) {
            String hay = (a.name + " " + a.description + " " + a.author + " " + a.repo).toLowerCase();
            a.card.setVisibility(q.isEmpty() || hay.contains(q) ? View.VISIBLE : View.GONE);
        }
    }

    // ---------- Installieren ----------

    private void onAppButton(StoreApp a) {
        if ("Öffnen".contentEquals(a.button.getText())) {
            String pkg = packageOf(a);
            Intent launch = pkg != null ? getPackageManager().getLaunchIntentForPackage(pkg) : null;
            if (launch != null) startActivity(launch);
            else Toast.makeText(this, a.name + " hat kein Startsymbol.", Toast.LENGTH_SHORT).show();
            return;
        }
        if (busyApp != null) {
            status.setText("Bitte warten – es läuft schon eine Installation.");
            return;
        }
        if (!canInstall()) return;

        busyApp = a.name;
        a.button.setEnabled(false);
        a.status.setText("Suche Release…");
        download(a.name, a.repo, a.url, a.asset, a.exclude, a.version, a.status);
    }

    // Ab Android 8 muss der Nutzer CreeStore einmal erlauben, Apps zu installieren
    private boolean canInstall() {
        if (Build.VERSION.SDK_INT >= 26 && !getPackageManager().canRequestPackageInstalls()) {
            Toast.makeText(this, "Bitte erlaube CreeStore, Apps zu installieren, und tippe dann noch einmal.", Toast.LENGTH_LONG).show();
            startActivity(new Intent(Settings.ACTION_MANAGE_UNKNOWN_APP_SOURCES, Uri.parse("package:" + getPackageName())));
            return false;
        }
        return true;
    }

    private void download(String appName, String repo, String url, String asset, String exclude, String version, TextView progress) {
        worker.execute(() -> {
            try {
                String u = url, ver = version, file = null;
                if (u == null) {
                    GitHub.ReleaseAsset rel = GitHub.latestAsset(repo, asset, exclude);
                    u = rel.url;
                    ver = rel.version;
                    file = rel.fileName;
                }
                if (file == null) file = Uri.parse(u).getLastPathSegment();
                if (file == null) file = "app.apk";

                File dir = new File(getCacheDir(), "downloads");
                dir.mkdirs();
                File apk = new File(dir, file.replaceAll("[^A-Za-z0-9._-]", "_"));
                GitHub.download(u, apk, p -> main.post(() -> progress.setText("Lade… " + p + "%")));

                PackageInfo archive = getPackageManager().getPackageArchiveInfo(apk.getPath(), 0);
                if (archive == null) throw new Exception("Die heruntergeladene Datei ist keine gültige APK.");

                // Wird nach erfolgreicher Installation übernommen (siehe onInstallResult)
                prefs(this).edit()
                        .putString("pending-pkg:" + appName, archive.packageName)
                        .putString("pending-ver:" + appName, ver)
                        .apply();

                main.post(() -> {
                    progress.setText("Installiere…");
                    status.setText(appName.equals(SELF) ? "CreeStore wird aktualisiert…" : appName + " wird installiert…");
                });
                Installer.install(this, apk, appName);
            } catch (Exception e) {
                main.post(() -> fail(appName, e.getMessage()));
            }
        });
    }

    // Vom InstallReceiver; die Activity kann inzwischen neu erstellt worden sein
    static void onInstallResult(Context context, String appName, String error) {
        if (appName == null) return;
        SharedPreferences p = prefs(context);
        if (error == null) {
            p.edit()
                    .putString("pkg:" + appName, p.getString("pending-pkg:" + appName, null))
                    .putString("ver:" + appName, p.getString("pending-ver:" + appName, null))
                    .apply();
        }
        p.edit().remove("pending-pkg:" + appName).remove("pending-ver:" + appName).apply();

        MainActivity a = current;
        if (a == null) return;
        if (error != null) {
            a.fail(appName, error);
            return;
        }
        a.busyApp = null;
        if (appName.equals(SELF)) {
            a.updateButton.setVisibility(View.GONE);
            return;
        }
        for (StoreApp app : a.apps) if (app.name.equals(appName)) a.refreshCard(app);
        a.status.setText(appName + " wurde installiert.");
    }

    private void fail(String appName, String error) {
        busyApp = null;
        if (error == null) error = "Unbekannter Fehler";
        if (appName.equals(SELF)) {
            updateButton.setEnabled(true);
        } else {
            for (StoreApp a : apps) {
                if (!a.name.equals(appName)) continue;
                refreshCard(a);
                a.status.setText("Fehler");
            }
        }
        String title = appName.equals(SELF) ? "Update fehlgeschlagen" : appName + " – Installation fehlgeschlagen";
        status.setText(title + ": " + error);
        if (!isFinishing()) new AlertDialog.Builder(this).setTitle(title).setMessage(error).setPositiveButton("OK", null).show();
    }

    private void uninstall(StoreApp a) {
        String pkg = packageOf(a);
        if (pkg == null) return;
        startActivity(new Intent(Intent.ACTION_DELETE, Uri.parse("package:" + pkg)));
    }

    // ---------- Store-Update über GitHub-Releases ----------

    private void checkSelfUpdate() {
        worker.execute(() -> {
            try {
                GitHub.ReleaseAsset rel = GitHub.latestAsset(BuildConfig.REPO, "CreeStore.apk", null);
                if (GitHub.isNewer(rel.version, BuildConfig.VERSION_NAME)) {
                    main.post(() -> {
                        updateButton.setText("Update auf v" + GitHub.clean(rel.version));
                        updateButton.setVisibility(View.VISIBLE);
                    });
                }
            } catch (Exception ignored) {
                // Kein Release oder offline – dann eben kein Update-Button
            }
        });
    }

    private void selfUpdate() {
        if (busyApp != null || !canInstall()) return;
        busyApp = SELF;
        updateButton.setEnabled(false);
        download(SELF, BuildConfig.REPO, null, "CreeStore.apk", null, null, status);
    }

    private void openUrl(String url) {
        try {
            startActivity(new Intent(Intent.ACTION_VIEW, Uri.parse(url)));
        } catch (Exception e) {
            Toast.makeText(this, url, Toast.LENGTH_LONG).show();
        }
    }

    static SharedPreferences prefs(Context context) {
        return context.getSharedPreferences(PREFS, Context.MODE_PRIVATE);
    }
}
