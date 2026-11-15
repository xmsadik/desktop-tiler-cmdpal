using System.IO;

namespace DesktopTiler.Core.Rules;

/// <summary>Whole-file writes that never leave a torn file behind: the text goes to a temp file
/// next to the target, which then replaces it.</summary>
public static class AtomicFile
{
    public static void WriteAllText(string path, string text)
    {
        var temp = path + ".tmp";
        File.WriteAllText(temp, text);
        File.Move(temp, path, overwrite: true);
    }
}
