using System;
using System.IO;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using TanksRebirth.Internals;

namespace TanksRebirth.Droid;

/// <summary>
/// Installs a downloaded update APK with Android's PackageInstaller: the APK is written into an install session, and
/// Android shows its own "Do you want to update this app?" screen. The first time, Android asks to allow installs
/// from this app (Settings opens; come back and tap the update button again).
/// </summary>
internal static class AppInstaller {
    const string StatusAction = "com.tanksrebirth.unofficial.INSTALL_STATUS";
    static StatusReceiver? _receiver;

    public static void Register(Activity activity) {
        try {
            var info = activity.PackageManager!.GetPackageInfo(activity.PackageName!, 0)!;
#pragma warning disable CA1422
            long code = Build.VERSION.SdkInt >= BuildVersionCodes.P ? info.LongVersionCode : info.VersionCode;
#pragma warning restore CA1422
            AppUpdate.CurrentBuild = (int)code;
            _receiver = new StatusReceiver();
            var filter = new IntentFilter(StatusAction);
            if (Build.VERSION.SdkInt >= BuildVersionCodes.Tiramisu)
                activity.ApplicationContext!.RegisterReceiver(_receiver, filter, ReceiverFlags.NotExported);
            else
                activity.ApplicationContext!.RegisterReceiver(_receiver, filter);
            AppUpdate.Install = path => activity.RunOnUiThread(() => Install(activity, path));
        }
        catch (Exception e) {
            Android.Util.Log.Warn("TanksRebirth", "In-app updates unavailable: " + e.Message);
        }
    }

    static void Install(Activity activity, string path) {
        try {
            // Android 8+: this app has to be allowed to install apps (asked once, in Settings)
            if (Build.VERSION.SdkInt >= BuildVersionCodes.O && !activity.PackageManager!.CanRequestPackageInstalls()) {
                AppUpdate.NeedPermission();
                var settings = new Intent(Android.Provider.Settings.ActionManageUnknownAppSources, Android.Net.Uri.Parse("package:" + activity.PackageName));
                activity.StartActivity(settings);
                return;
            }
            var installer = activity.PackageManager!.PackageInstaller;
            var parameters = new PackageInstaller.SessionParams(PackageInstallMode.FullInstall);
            int id = installer.CreateSession(parameters);
            using var session = installer.OpenSession(id);
            using (var output = session.OpenWrite("TanksRebirth", 0, new FileInfo(path).Length))
            using (var input = File.OpenRead(path)) {
                input.CopyTo(output);
                session.Fsync(output);
            }
            var intent = new Intent(StatusAction).SetPackage(activity.PackageName);
            var flags = PendingIntentFlags.UpdateCurrent;
            if (Build.VERSION.SdkInt >= BuildVersionCodes.S)
                flags |= PendingIntentFlags.Mutable; // the installer adds its result to it
            var pending = PendingIntent.GetBroadcast(activity, 77, intent, flags)!;
            session.Commit(pending.IntentSender!);
        }
        catch (Exception e) {
            AppUpdate.Fail(e.Message);
        }
    }

    class StatusReceiver : BroadcastReceiver {
        public override void OnReceive(Context? context, Intent? intent) {
            if (context is null || intent is null)
                return;
            int status = intent.GetIntExtra(PackageInstaller.ExtraStatus, -999);
            switch ((PackageInstallStatus)status) {
                case PackageInstallStatus.PendingUserAction: {
                    // Android's own confirm screen
#pragma warning disable CA1422, CS0618
                    var confirm = intent.GetParcelableExtra(Intent.ExtraIntent) as Intent;
#pragma warning restore CA1422, CS0618
                    if (confirm is not null) {
                        confirm.AddFlags(ActivityFlags.NewTask);
                        context.StartActivity(confirm);
                    }
                    break;
                }
                case PackageInstallStatus.Success:
                    break; // (the app restarts as the new version)
                case PackageInstallStatus.FailureAborted:
                    AppUpdate.Cancelled();
                    break;
                default:
                    AppUpdate.Fail(intent.GetStringExtra(PackageInstaller.ExtraStatusMessage) ?? $"status {status}");
                    break;
            }
        }
    }
}
