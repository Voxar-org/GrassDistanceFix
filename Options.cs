using System;
using System.IO;
using System.Security;
using VRage.FileSystem;
using VRageRender;

namespace GrassDistanceFix
{
    [Flags]
    internal enum FadeOptions
    {
        Enabled = 1,
        DrawLimit = 2,
        Lod = 4
    }

    internal static class Options
    {
        internal const FadeOptions Default = FadeOptions.Enabled | FadeOptions.DrawLimit | FadeOptions.Lod;
        private static readonly Lazy<FadeOptions> Saved = new Lazy<FadeOptions>(Load);
        private static volatile int applied = -1;

        private static string SettingsPath => Path.Combine(MyFileSystem.UserDataPath, "GrassDistanceFix.cfg");

        internal static FadeOptions Current
        {
            get
            {
                int value = applied;
                return value < 0 ? Saved.Value : (FadeOptions)value;
            }
        }

        internal static void Apply(FadeOptions options)
        {
            if (options == Current)
                return;

            File.WriteAllText(SettingsPath, ((int)options).ToString(System.Globalization.CultureInfo.InvariantCulture));
            applied = (int)options;
            MyRenderProxy.ReloadEffects();
        }

        private static FadeOptions Load()
        {
            try
            {
                if (int.TryParse(File.ReadAllText(SettingsPath), out int value) && value >= 0 && value <= (int)Default)
                    return (FadeOptions)value;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is SecurityException)
            {
                // Missing or inaccessible settings use the default.
            }

            return Default;
        }
    }
}
