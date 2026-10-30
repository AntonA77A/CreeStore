package de.creestore;

import android.app.PendingIntent;
import android.content.Context;
import android.content.Intent;
import android.content.pm.PackageInstaller;
import android.os.Build;

import java.io.File;
import java.io.FileInputStream;
import java.io.IOException;
import java.io.InputStream;
import java.io.OutputStream;

// Installiert eine APK über den PackageInstaller; das Ergebnis kommt im InstallReceiver an
final class Installer {
    static final String EXTRA_APP = "de.creestore.APP";

    private Installer() {
    }

    static void install(Context context, File apk, String appName) throws IOException {
        PackageInstaller pi = context.getPackageManager().getPackageInstaller();
        PackageInstaller.SessionParams params = new PackageInstaller.SessionParams(PackageInstaller.SessionParams.MODE_FULL_INSTALL);
        params.setSize(apk.length());
        int id = pi.createSession(params);
        try (PackageInstaller.Session session = pi.openSession(id)) {
            try (InputStream in = new FileInputStream(apk); OutputStream out = session.openWrite("app.apk", 0, apk.length())) {
                byte[] buf = new byte[65536];
                int n;
                while ((n = in.read(buf)) > 0) out.write(buf, 0, n);
                session.fsync(out);
            }

            Intent intent = new Intent(context, InstallReceiver.class).putExtra(EXTRA_APP, appName);
            // Muss veränderbar sein, damit der PackageInstaller den Status eintragen kann
            int flags = PendingIntent.FLAG_UPDATE_CURRENT | (Build.VERSION.SDK_INT >= 31 ? PendingIntent.FLAG_MUTABLE : 0);
            PendingIntent pending = PendingIntent.getBroadcast(context, id, intent, flags);
            session.commit(pending.getIntentSender());
        } catch (IOException | RuntimeException e) {
            pi.abandonSession(id);
            throw e;
        }
    }
}
