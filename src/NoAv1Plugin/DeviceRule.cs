using System.Collections.Generic;

namespace NoAv1Plugin
{
    public class DeviceRule
    {
        // Regex to match the AppName / Client string reported by the session (case-insensitive)
        public string? AppNameRegex { get; set; }

        // Regex to match the reported device name
        public string? DeviceNameRegex { get; set; }

        // Partial match for remote endpoint (IP) if desired
        public string? RemoteAddress { get; set; }

        // Video codecs to keep offering to matched devices/sessions. Must start out *empty*:
        // XmlSerializer deserializes a List<T> property by calling Add() on whatever list the
        // constructor already created rather than replacing it, so any items initialized here
        // get prepended again to the saved ones on every config load (and the next
        // SaveConfiguration() writes those duplicates back to disk). An empty list falls back
        // to H.264/HEVC where it's consumed (Plugin.ApplyOverride and
        // NoAv1PlaybackInfoFilter.RestrictProfile), which preserves the plugin's original
        // behavior for rules saved before this field existed; the config page pre-checks those
        // same defaults for a new rule itself.
        public List<string> AllowedVideoCodecs { get; set; } = new();

        // Audio codecs to keep offering to matched devices/sessions. Empty for the same reason
        // as AllowedVideoCodecs; falls back to AAC/MP3 where it's consumed.
        public List<string> AllowedAudioCodecs { get; set; } = new();
    }
}
