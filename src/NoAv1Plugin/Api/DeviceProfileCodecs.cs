using System;
using System.Collections.Generic;
using MediaBrowser.Model.Dlna;

namespace NoAv1Plugin.Api
{
    // Shared by NoAv1Controller (reading a device's last-known claimed codecs for the admin UI)
    // and NoAv1PlaybackInfoFilter (snapshotting a device's freshly-submitted profile), so the
    // two stay in exact agreement about what "claimed codecs" means.
    internal static class DeviceProfileCodecs
    {
        public static (HashSet<string> Video, HashSet<string> Audio) Extract(DeviceProfile? profile)
        {
            var video = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var audio = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (profile is null)
            {
                return (video, audio);
            }

            foreach (var directPlay in profile.DirectPlayProfiles ?? Array.Empty<DirectPlayProfile>())
            {
                Add(video, directPlay.VideoCodec);
                Add(audio, directPlay.AudioCodec);
            }

            foreach (var codecProfile in profile.CodecProfiles ?? Array.Empty<CodecProfile>())
            {
                if (codecProfile.Type is CodecType.Video or CodecType.VideoAudio)
                {
                    Add(video, codecProfile.Codec);
                }

                if (codecProfile.Type is CodecType.Audio or CodecType.VideoAudio)
                {
                    Add(audio, codecProfile.Codec);
                }
            }

            return (video, audio);
        }

        private static void Add(HashSet<string> target, string? commaSeparatedCodecs)
        {
            if (string.IsNullOrWhiteSpace(commaSeparatedCodecs))
            {
                return;
            }

            foreach (var codec in commaSeparatedCodecs.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                target.Add(codec);
            }
        }
    }
}
