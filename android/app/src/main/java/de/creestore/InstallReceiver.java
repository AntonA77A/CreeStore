package de.creestore;

import android.content.BroadcastReceiver;
import android.content.Context;
import android.content.Intent;
import android.content.pm.PackageInstaller;

public class InstallReceiver extends BroadcastReceiver {
    @Override
    public void onReceive(Context context, Intent intent) {
        int status = intent.getIntExtra(PackageInstaller.EXTRA_STATUS, PackageInstaller.STATUS_FAILURE);
        String app = intent.getStringExtra(Installer.EXTRA_APP);

        if (status == PackageInstaller.STATUS_PENDING_USER_ACTION) {
            // Android fragt den Nutzer, ob installiert werden soll
            @SuppressWarnings("deprecation")
            Intent confirm = intent.getParcelableExtra(Intent.EXTRA_INTENT);
            if (confirm != null) {
                confirm.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK);
                context.startActivity(confirm);
            }
            return;
        }

        String message = null;
        if (status != PackageInstaller.STATUS_SUCCESS) {
            message = intent.getStringExtra(PackageInstaller.EXTRA_STATUS_MESSAGE);
            if (status == PackageInstaller.STATUS_FAILURE_ABORTED) message = "Installation abgebrochen.";
            else if (status == PackageInstaller.STATUS_FAILURE_CONFLICT)
                message = "Die installierte Version ist mit einem anderen Schlüssel signiert. Bitte zuerst deinstallieren.";
            else if (message == null) message = "Installation fehlgeschlagen (Status " + status + ").";
        }
        MainActivity.onInstallResult(context, app, message);
    }
}
