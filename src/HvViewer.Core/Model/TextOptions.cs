namespace HvViewer.Core.Model;

/// <summary>Where the search text is looked for.</summary>
public enum TextTarget { Message, AllFields, Xml }

public sealed record TextOptions(bool MatchCase = false, bool WholeWord = false, bool Regex = false, bool IgnoreAccents = false);

/// <summary>A task category filter: tasks are numbered per provider.</summary>
public sealed record TaskFilter(string Provider, int Task);
