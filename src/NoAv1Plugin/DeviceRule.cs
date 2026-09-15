using System.Collections.Generic;

namespace NoAv1Plugin
{
    public class DeviceRule
    {
        // Exact DeviceId string (unique per client device) — preferred for per-device targeting
        public string? DeviceId { get; set; }

        // Regex to match the AppName / Client string reported by the session (case-insensitive)
        public string? AppNameRegex { get; set; }

        // Regex to match the reported device name
        public string? DeviceNameRegex { get; set; }

        // Partial match for remote endpoint (IP) if desired
        public string? RemoteAddress { get; set; }

        // Friendly label shown in the admin UI, e.g. "Living Room Shield" or "All webOS TVs"
        public string? Label { get; set; }

        // Video codecs to keep offering to matched devices/sessions. Defaults preserve the
        // plugin's original behavior (H.264/HEVC, no AV1) for rules saved before this field existed.
        public List<string> AllowedVideoCodecs { get; set; } = new() { "h264", "hevc" };

        // Audio codecs to keep offering to matched devices/sessions.
        public List<string> AllowedAudioCodecs { get; set; } = new() { "aac", "mp3" };
    }
}
