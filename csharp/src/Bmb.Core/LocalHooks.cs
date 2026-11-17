namespace Bmb.Core;

/// optional local additions to the build and the game's Amax plugins (none in this source)
static partial class LocalHooks
{
    static partial void Boot(List<KeyValuePair<string, byte[]>> boot);
    static partial void Plugins(string plugins);

    /// more files for the boot pack Blur REBORN reads
    internal static void BootFiles(List<KeyValuePair<string, byte[]>> boot) => Boot(boot);

    /// more Amax plugins next to the QuickStart one
    internal static void PluginFiles(string plugins) => Plugins(plugins);
}
