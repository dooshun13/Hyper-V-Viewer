namespace HvViewer.Machine.Health;

public sealed class WindowsDriveProvider : IDriveProvider
{
    public IReadOnlyList<(string Name, long Total, long Free)> FixedDrives() =>
        DriveInfo.GetDrives()
            .Where(d => d.DriveType == DriveType.Fixed && d.IsReady)
            .Select(d => (d.Name.TrimEnd('\\'), d.TotalSize, d.AvailableFreeSpace))
            .ToList();
}
