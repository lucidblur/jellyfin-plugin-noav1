namespace NoAv1Plugin.Api
{
    // Canonical codec list offered in the admin UI. Devices may claim support for codecs
    // outside this list (via their reported DeviceProfile) but only these are selectable,
    // since they're what Jellyfin actually knows how to direct-play/transcode.
    public static class KnownCodecs
    {
        public static readonly string[] Video =
        {
            "h264", "hevc", "av1", "vp9", "vp8", "mpeg2video", "mpeg4", "vc1"
        };

        public static readonly string[] Audio =
        {
            "aac", "mp3", "ac3", "eac3", "flac", "opus", "vorbis", "pcm_s16le"
        };
    }
}
