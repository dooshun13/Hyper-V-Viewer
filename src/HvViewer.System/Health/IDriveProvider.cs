namespace HvViewer.Machine.Health;

public interface IDriveProvider
{
    /// <summary>Fixed (non-removable, non-network) drives that are ready. <c>Name</c> looks like "C:".</summary>
    IReadOnlyList<(string Name, long Total, long Free)> FixedDrives();
}
