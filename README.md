# CreeStore

Ein kleiner App-Store für Windows. Die App-Liste (`apps.json`) und die Programme kommen direkt von GitHub.

**Download:** `https://github.com/AntonA77A/CreeStore/releases/latest/download/CreeStoreSetup.exe`
Die Setup-exe installiert ohne Fragen (kein Admin nötig) nach `%LOCALAPPDATA%\Programs\CreeStore`, legt Startmenü- und Desktop-Verknüpfung an und startet den Store.

## Neue Version veröffentlichen

```
git tag v1.0.0
git push origin v1.0.0
```

GitHub Actions baut dann `CreeStoreSetup.exe` und hängt sie an das Release. Ältere CreeStore-Versionen zeigen automatisch einen Update-Button.

## Apps hinzufügen

Einfach `apps.json` im Repo bearbeiten – der Store lädt sie bei jedem Start neu:

| Feld          | Bedeutung                                                                 |
|---------------|---------------------------------------------------------------------------|
| `name`        | Anzeigename                                                               |
| `description` | Beschreibung                                                              |
| `repo`        | GitHub-Repo, aus dessen neuestem Release geladen wird (`owner/name`)      |
| `asset`       | Welche Datei aus dem Release, z.B. `*-x64.exe` (Standard: `*.exe`)       |
| `url`         | Statt `repo`: direkter Download-Link                                      |
| `args`        | Parameter für die stille Installation, z.B. `/S` oder `/VERYSILENT`       |
| `type`        | `portable` = exe nur kopieren + Startmenü-Verknüpfung, statt installieren |
| `version`     | Optional, damit der Store Updates anzeigen kann                          |
| `uninstallName` | Optional: Name unter „Apps & Features“, falls anders als `name`         |
| `uninstallArgs` | Optional: Parameter für stilles Deinstallieren (Inno/NSIS/MSI werden erkannt) |

## Admin: Anfragen & Apps verwalten

Im Store gibt es den Button **„App einreichen“**. Er öffnet ein Formular auf GitHub, und die Anfrage landet als Issue mit dem Label `app-anfrage` im Repo.

`dist\CreeStoreAdmin.exe` (wird von `build.ps1` gebaut, ist nicht im Release):

- **Anfragen**: Felder prüfen/anpassen, dann *Annehmen* (trägt die App in `apps.json` ein und schließt das Issue) oder *Ablehnen* (Kommentar mit Grund, Issue wird geschlossen). *Download prüfen* testet, ob die Datei gefunden wird.
- **Apps im Store**: Apps aus `apps.json` entfernen.

Anmeldung: nutzt automatisch den GitHub-Login von Git. Sonst fragt die App nach einem Token mit `repo`-Berechtigung (wird verschlüsselt gespeichert).

## Selbst bauen

```
.\build.ps1 -Repo "AntonA77A/CreeStore" -Version "1.0.0"
```

Braucht nichts außer Windows (nutzt den eingebauten C#-Compiler).
