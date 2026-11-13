namespace HvViewer.Core.Model;

/// <summary>A log to read: a channel by its internal name, or an .evtx file path.</summary>
public readonly record struct LogTarget(string Name, bool IsFile)
{
    public static LogTarget Channel(string name) => new(name, false);

    public static LogTarget File(string path) => new(path, true);
}
