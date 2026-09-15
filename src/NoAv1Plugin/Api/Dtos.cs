using System;
using System.Collections.Generic;

namespace NoAv1Plugin.Api
{
    public class CodecListDto
    {
        public IReadOnlyList<string> VideoCodecs { get; set; } = Array.Empty<string>();

        public IReadOnlyList<string> AudioCodecs { get; set; } = Array.Empty<string>();
    }

    public class DeviceCapabilityDto
    {
        public string Id { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;

        public string? AppName { get; set; }

        public string? LastUserName { get; set; }

        public DateTime? DateLastActivity { get; set; }

        // False when the device has never submitted a full DeviceProfile (e.g. it always relies
        // on server-side transcoding), in which case ClaimedVideoCodecs/ClaimedAudioCodecs are
        // empty and the admin UI should not grey anything out for it.
        public bool HasReportedProfile { get; set; }

        public IReadOnlyCollection<string> ClaimedVideoCodecs { get; set; } = Array.Empty<string>();

        public IReadOnlyCollection<string> ClaimedAudioCodecs { get; set; } = Array.Empty<string>();
    }

    // A client debug log uploaded via the "Send Logs" feature in official apps (stored
    // server-side as upload_{clientName}_{clientVersion}_{timestamp}_{guid}.log). The filename
    // has no device ID in it, so FileName is the only reliable way to fetch one's content --
    // ClientName/ClientVersion/Timestamp are a best-effort parse of the filename, used to let
    // the admin UI suggest (not guarantee) which device a given log probably came from.
    public class ClientLogFileDto
    {
        public string FileName { get; set; } = string.Empty;

        public string ClientName { get; set; } = string.Empty;

        public string ClientVersion { get; set; } = string.Empty;

        public DateTime? Timestamp { get; set; }

        public long SizeBytes { get; set; }
    }

    public class ClientLogContentDto
    {
        public string FileName { get; set; } = string.Empty;

        public string Content { get; set; } = string.Empty;

        public bool Truncated { get; set; }
    }
}
