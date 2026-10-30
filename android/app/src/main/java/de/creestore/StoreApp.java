package de.creestore;

import android.widget.Button;
import android.widget.TextView;
import android.view.View;

// Ein Eintrag aus apps.json (Schlüssel "android")
final class StoreApp {
    String name;
    String description;
    String author;
    String repo;      // GitHub-Repo, aus dessen neuestem Release geladen wird
    String url;       // statt repo: direkter Download-Link
    String asset;     // Datei aus dem Release, Standard *.apk
    String exclude;
    String packageName; // optional, sonst nach der Installation aus der APK gelesen
    String version;

    View card;
    TextView status;
    Button button;
    Button uninstallButton;
}
