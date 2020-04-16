namespace SS14.Watchdog.Components.Updates
{
    public sealed class RevisionDescription
    {
        public string Version { get; set; } = default!;

        public DownloadInfoPair? WindowsInfo { get; set; }
        public DownloadInfoPair? LinuxInfo { get; set; }
        public DownloadInfoPair? MacOSInfo { get; set; }

        public RevisionDescription()
        {
        }

        public RevisionDescription(string version,
            DownloadInfoPair? windowsInfo,
            DownloadInfoPair? linuxInfo,
            DownloadInfoPair? macOSInfo)
        {
            Version = version;
            WindowsInfo = windowsInfo;
            LinuxInfo = linuxInfo;
            MacOSInfo = macOSInfo;
        }
    }

    public sealed class DownloadInfoPair
    {
        public string Download { get; set; } = default!;
        public string Hash { get; set; } = default!;

        public DownloadInfoPair()
        {
        }

        public DownloadInfoPair(string download, string hash)
        {
            Download = download;
            Hash = hash;
        }
    }
}