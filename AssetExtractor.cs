using System;
using System.IO;
using Android.Content;
using Android.Content.Res;

namespace TanksRebirth.Droid;

/// <summary>
/// The game reads textures, sounds, fonts and translations as ordinary files relative to its working directory
/// (e.g. "Content/Assets/fonts/en_US.ttf"). Android packs those inside the APK, so on first launch (and after each
/// update) they are copied out to the app's private storage, which then becomes the working directory.
/// Compiled .xnb content is left in the APK; MonoGame's ContentManager reads it from there directly.
/// </summary>
internal static class AssetExtractor {
    public static void ExtractIfNeeded(Context context, string gameDir, string saveDir, Action<string>? progress = null) {
        var assets = context.Assets!;

        // re-extract whenever the APK changes (install or update)
#pragma warning disable CA1422
        var info = context.PackageManager!.GetPackageInfo(context.PackageName!, 0)!;
#pragma warning restore CA1422
        var stamp = info.LastUpdateTime.ToString();
        var marker = Path.Combine(gameDir, ".extracted");

        if (!(File.Exists(marker) && File.ReadAllText(marker) == stamp)) {
            if (Directory.Exists(gameDir))
                Directory.Delete(gameDir, true);
            Directory.CreateDirectory(gameDir);

            int count = 0;
            CopyTree(assets, "Content", gameDir, ref count, progress);
            CopyTree(assets, "Localization", gameDir, ref count, progress);

            File.WriteAllText(marker, stamp);
        }

        // Seed the official campaign if it was bundled and the player doesn't have it yet.
        var campaigns = Path.Combine(saveDir, "Campaigns");
        Directory.CreateDirectory(campaigns);
        foreach (var name in assets.List("Bundled") ?? []) {
            var dest = Path.Combine(campaigns, name);
            if (File.Exists(dest))
                continue;
            using var input = assets.Open("Bundled/" + name);
            using var output = File.Create(dest);
            input.CopyTo(output);
        }
    }

    private static void CopyTree(AssetManager assets, string path, string root, ref int count, Action<string>? progress) {
        var entries = assets.List(path);
        if (entries is null || entries.Length == 0) {
            // a file (asset folders always have entries)
            if (path.EndsWith(".xnb", StringComparison.OrdinalIgnoreCase))
                return;

            var dest = Path.Combine(root, path);
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            try {
                using var input = assets.Open(path);
                using var output = File.Create(dest);
                input.CopyTo(output);
            }
            catch (Java.IO.FileNotFoundException) {
                // empty directory, nothing to copy
            }
            if (++count % 25 == 0)
                progress?.Invoke($"Preparing game files… {count}");
            return;
        }

        foreach (var entry in entries)
            CopyTree(assets, path + "/" + entry, root, ref count, progress);
    }
}
