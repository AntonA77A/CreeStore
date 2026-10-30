# CreeStore

Ein kleiner App-Store für Windows. Die App-Liste (`apps.json`) und die Programme kommen direkt von GitHub.

**Download:** `https://github.com/AntonA77A/CreeStore/releases/latest/download/CreeStoreSetup.exe`
Die Setup-exe installiert ohne Fragen (kein Admin nötig) nach `%LOCALAPPDATA%\Programs\CreeStore`, legt Startmenü- und Desktop-Verknüpfung an und startet den Store.

**Android:** `https://github.com/AntonA77A/CreeStore/releases/latest/download/CreeStore.apk`
Beim ersten Installieren einer App fragt Android, ob CreeStore Apps installieren darf.

## Neue Version veröffentlichen

```
git tag v1.0.0
git push origin v1.0.0
```

GitHub Actions baut dann `CreeStoreSetup.exe` und `CreeStore.apk` und hängt sie an das Release. Ältere CreeStore-Versionen zeigen automatisch einen Update-Button.

## Apps hinzufügen

Einfach `apps.json` im Repo bearbeiten – der Store lädt sie bei jedem Start neu:

| Feld          | Bedeutung                                                                 |
|---------------|---------------------------------------------------------------------------|
| `name`        | Anzeigename                                                               |
| `description` | Beschreibung                                                              |
| `repo`        | GitHub-Repo, aus dessen neuestem Release geladen wird (`owner/name`)      |
| `asset`       | Welche Datei aus dem Release, z.B. `*-x64.exe` (Standard: `*.exe`)       |
| `exclude`     | Optional: Dateien ausschließen, die auch zu `asset` passen, z.B. `*steam*` |
| `url`         | Statt `repo`: direkter Download-Link                                      |
| `args`        | Parameter für die stille Installation, z.B. `/S` oder `/VERYSILENT`       |
| `type`        | `portable` = exe nur kopieren + Startmenü-Verknüpfung, statt installieren |
| `version`     | Optional, damit der Store Updates anzeigen kann                          |
| `uninstallName` | Optional: Name unter „Apps & Features“, falls anders als `name`         |
| `uninstallArgs` | Optional: Parameter für stilles Deinstallieren (Inno/NSIS/MSI werden erkannt) |

### Android-Apps

Android-Apps stehen in `apps.json` in einer eigenen Liste `"android"` (die Windows-Version ignoriert sie):

```json
"android": [
  { "name": "NewPipe", "description": "…", "repo": "TeamNewPipe/NewPipe", "asset": "NewPipe_*.apk", "package": "org.schabi.newpipe" }
]
```

`name`, `description`, `author`, `repo`, `url`, `asset` (Standard: `*.apk`), `exclude` und `version` funktionieren wie oben.
`package` ist der Paketname der App – damit erkennt der Store auch Apps, die nicht über CreeStore installiert wurden.

## Admin: Anfragen & Apps verwalten

Im Store gibt es den Button **„App einreichen“**. Er öffnet ein Formular auf GitHub, und die Anfrage landet als Issue mit dem Label `app-anfrage` im Repo.

`dist\CreeStoreAdmin.exe` (wird von `build.ps1` gebaut, ist nicht im Release):

- **Anfragen**: Felder prüfen/anpassen, dann *Annehmen* (trägt die App in `apps.json` ein und schließt das Issue) oder *Ablehnen* (Kommentar mit Grund, Issue wird geschlossen). *Download prüfen* testet, ob die Datei gefunden wird.
- **Apps im Store**: Apps aus `apps.json` entfernen.

Anmeldung: nutzt automatisch den GitHub-Login von Git. Sonst fragt die App nach einem Token mit `repo`-Berechtigung (wird verschlüsselt gespeichert).

## Selbst bauen

Android (braucht JDK 17 und das Android SDK):

```
cd android
./gradlew assembleRelease
```

Die APK landet in `android/app/build/outputs/apk/release/`. Bei jeder Änderung unter `android/` baut GitHub Actions („Android“) eine Test-APK, die man unter *Actions* herunterladen kann.

**Signierschlüssel (wichtig für Updates):** Android installiert ein Update nur, wenn es mit demselben Schlüssel signiert ist. Einmalig einen Schlüssel erzeugen und als Repo-Secrets hinterlegen (*Settings → Secrets and variables → Actions*):

```
keytool -genkeypair -v -keystore creestore.jks -alias creestore -keyalg RSA -keysize 4096 -validity 10000
base64 -w0 creestore.jks
```

| Secret                      | Inhalt                                 |
|-----------------------------|----------------------------------------|
| `ANDROID_KEYSTORE_BASE64`   | Ausgabe von `base64`                   |
| `ANDROID_KEYSTORE_PASSWORD` | Passwort des Keystores                 |
| `ANDROID_KEY_ALIAS`         | `creestore`                            |
| `ANDROID_KEY_PASSWORD`      | Passwort des Schlüssels (falls anders) |

Ohne diese Secrets wird mit einem zufälligen Debug-Schlüssel signiert – die APK läuft, aber Store-Updates schlagen fehl.

Windows:

```
.\build.ps1 -Repo "AntonA77A/CreeStore" -Version "1.0.0"
```

Braucht nichts außer Windows (nutzt den eingebauten C#-Compiler).
