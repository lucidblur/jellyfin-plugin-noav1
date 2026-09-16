using System;
using System.Collections.Generic;

namespace NoAv1Plugin.Api
{
    // Some clients report codec names in a DeviceProfile that don't match FFmpeg/Jellyfin's
    // canonical ones. Observed live from Jellyfin Android TV 0.19.10: "mpeg" for what is
    // otherwise clearly MPEG-4 Part 2 Visual -- its own device decoder dump has a distinct
    // c2.*.mpeg4.decoder entry mapping to video/mp4v-es, and it separately reports "mpeg2video"
    // for actual MPEG-2, so "mpeg" isn't shorthand for that. FFmpeg's own codec_name for MPEG-4
    // Part 2 content is "mpeg4", never "mpeg" (confirmed against Jellyfin's own source -- no
    // "mpeg" codec identifier appears anywhere in it), so this is a client reporting quirk to
    // normalize, not an alternate canonical spelling we should add to KnownCodecs instead.
    //
    // Used both when reading a device's claimed codecs for display (Api/DeviceProfileCodecs.cs)
    // and when matching a device's reported codecs against a rule's allowed list during
    // enforcement (NoAv1PlaybackInfoFilter) -- both need to agree, or a rule that allows
    // "mpeg4" would silently fail to actually allow it for a device that calls it "mpeg".
    internal static class CodecNameAliases
    {
        private static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase)
        {
            ["mpeg"] = "mpeg4"
        };

        public static string Normalize(string codec) => Aliases.TryGetValue(codec, out var canonical) ? canonical : codec;
    }
}
