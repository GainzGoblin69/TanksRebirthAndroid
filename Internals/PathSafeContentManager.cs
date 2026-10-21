using System;
using System.IO;
using System.Text;
using Microsoft.Xna.Framework.Content;

namespace TanksRebirth.Internals;

/// <summary>
/// A <see cref="ContentManager"/> that tidies asset paths before opening them: backslashes become '/', and
/// doubled slashes (e.g. "Assets/models/" + "/logo") collapse to one. Desktop file systems shrug those off,
/// but Android reads content out of the APK, where "models//logo" simply isn't found.
/// </summary>
public class PathSafeContentManager : ContentManager {
    public PathSafeContentManager(IServiceProvider services, string rootDirectory) : base(services, rootDirectory) { }

    protected override Stream OpenStream(string assetName) => base.OpenStream(Normalize(assetName));

    public static string Normalize(string path) {
        if (string.IsNullOrEmpty(path))
            return path;

        var sb = new StringBuilder(path.Length);
        char prev = '\0';
        foreach (var raw in path) {
            var c = raw == '\\' ? '/' : raw;
            if (c == '/' && prev == '/')
                continue;
            sb.Append(c);
            prev = c;
        }
        return sb.ToString();
    }
}
