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
}
