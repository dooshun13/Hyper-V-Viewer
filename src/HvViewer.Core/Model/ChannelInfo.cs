namespace HvViewer.Core.Model;

public enum ChannelType { Admin, Operational, Analytic, Debug }

public sealed record ChannelInfo(string Name, ChannelType Type, bool Enabled);
