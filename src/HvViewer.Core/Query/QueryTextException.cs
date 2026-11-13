namespace HvViewer.Core.Query;

/// <summary>The one-line query text could not be understood. <see cref="Position"/> is the character index of the bad token.</summary>
public sealed class QueryTextException(int position, string message) : Exception(message)
{
    public int Position { get; } = position;
}
