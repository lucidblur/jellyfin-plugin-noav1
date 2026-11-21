using System;
using System.Collections.Generic;

namespace NoAv1Plugin
{
    // A device's most recently observed, self-submitted codec support, captured from a real
    // playback request (see Api.NoAv1PlaybackInfoFilter) and persisted in the plugin's own saved
    // configuration -- unlike IDeviceManager's capabilities store, which is purely in-memory and
    // is wiped on every server restart. Keyed by device *name* rather than device ID, since
    // DeviceId is a per-request, client-self-reported value with no server-side validation and
    // was observed to change for the same physical device across a reconnect.
    public class DeviceCodecSnapshot
    {
        public string DeviceName { get; set; } = string.Empty;

        public string? AppName { get; set; }

        public List<string> VideoCodecs { get; set; } = new();

        public List<string> AudioCodecs { get; set; } = new();

        public DateTime LastSeenUtc { get; set; }
    }
}
