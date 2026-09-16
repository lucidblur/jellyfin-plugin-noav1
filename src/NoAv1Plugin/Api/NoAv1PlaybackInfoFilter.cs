using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Jellyfin.Api.Models.MediaInfoDtos;
using MediaBrowser.Controller.Devices;
using MediaBrowser.Model.Dlna;
using Microsoft.AspNetCore.Mvc.Filters;

namespace NoAv1Plugin.Api
{
    /// <summary>
    /// Strips disallowed codecs from a matched device's own submitted DeviceProfile before
    /// MediaInfoController's playback-info action runs.
    /// </summary>
    /// <remarks>
    /// MediaInfoController.GetPostedPlaybackInfo prefers the DeviceProfile the *client* posts
    /// in its request body over anything saved server-side via IDeviceManager.SaveCapabilities
    /// -- it only falls back to the saved one when the client submits none at all. Since every
    /// capable client (this plugin was written to handle -- Android TV, web, etc.) submits its
    /// own profile on every playback request, a SaveCapabilities-based override never actually
    /// influenced a real playback decision for them; it only affected clients that submit no
    /// profile, which isn't the common case this plugin exists for. This filter is the actual
    /// enforcement point: it edits the incoming client-submitted DeviceProfile in place, before
    /// StreamBuilder ever sees it, so StreamBuilder computes PlayMethod/TranscodingUrl exactly
    /// as it normally would for a device that never claimed the disallowed codec -- rather than
    /// trying to patch SupportsDirectPlay after the fact, which would leave the response with no
    /// valid TranscodingUrl at all (SetDeviceSpecificData only computes one when SupportsDirectPlay
    /// is already false by the time StreamBuilder runs).
    /// </remarks>
    public class NoAv1PlaybackInfoFilter : IAsyncActionFilter
    {
        private readonly IDeviceManager _deviceManager;

        public NoAv1PlaybackInfoFilter(IDeviceManager deviceManager)
        {
            _deviceManager = deviceManager;
        }

        public Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            TryRestrictDeviceProfile(context);
            return next();
        }

        private void TryRestrictDeviceProfile(ActionExecutingContext context)
        {
            var dto = context.ActionArguments.Values.OfType<PlaybackInfoDto>().FirstOrDefault();
            if (dto?.DeviceProfile is null)
            {
                return;
            }

            var plugin = Plugin.Instance;
            if (plugin?.Configuration?.Rules is null || plugin.Configuration.Rules.Count == 0)
            {
                return;
            }

            // Jellyfin's own auth claim types aren't exposed on a stable public surface a
            // plugin can reference without pulling in the whole Jellyfin.Api project just for
            // constants (Jellyfin.Api.Constants.InternalClaimTypes); these string values are
            // that project's own auth wiring and have been stable across versions.
            var user = context.HttpContext.User;
            var deviceId = user.FindFirst("Jellyfin-DeviceId")?.Value;
            var appName = user.FindFirst("Jellyfin-Client")?.Value;
            var deviceName = string.IsNullOrEmpty(deviceId) ? null : _deviceManager.GetDevice(deviceId)?.Name;
            var remoteAddress = context.HttpContext.Connection.RemoteIpAddress?.ToString();

            var rule = Plugin.FindMatchingRule(plugin.Configuration.Rules, deviceId, appName, deviceName, remoteAddress);
            if (rule is null)
            {
                return;
            }

            RestrictProfile(dto.DeviceProfile, rule);
        }

        private static readonly string[] DefaultVideoCodecs = { "h264", "hevc" };
        private static readonly string[] DefaultAudioCodecs = { "aac", "mp3" };

        private static void RestrictProfile(DeviceProfile profile, DeviceRule rule)
        {
            var allowedVideo = new HashSet<string>(
                rule.AllowedVideoCodecs is { Count: > 0 } ? rule.AllowedVideoCodecs : DefaultVideoCodecs,
                StringComparer.OrdinalIgnoreCase);
            var allowedAudio = new HashSet<string>(
                rule.AllowedAudioCodecs is { Count: > 0 } ? rule.AllowedAudioCodecs : DefaultAudioCodecs,
                StringComparer.OrdinalIgnoreCase);

            if (profile.DirectPlayProfiles is not null)
            {
                foreach (var directPlay in profile.DirectPlayProfiles)
                {
                    if (directPlay.Type == DlnaProfileType.Video)
                    {
                        directPlay.VideoCodec = FilterCodecList(directPlay.VideoCodec, allowedVideo);
                        directPlay.AudioCodec = FilterCodecList(directPlay.AudioCodec, allowedAudio);
                    }
                    else if (directPlay.Type == DlnaProfileType.Audio)
                    {
                        directPlay.AudioCodec = FilterCodecList(directPlay.AudioCodec, allowedAudio);
                    }
                }
            }

            if (profile.CodecProfiles is not null)
            {
                profile.CodecProfiles = profile.CodecProfiles
                    .Where(codecProfile => codecProfile.Type switch
                    {
                        CodecType.Video => string.IsNullOrEmpty(codecProfile.Codec) || allowedVideo.Contains(codecProfile.Codec),
                        CodecType.Audio => string.IsNullOrEmpty(codecProfile.Codec) || allowedAudio.Contains(codecProfile.Codec),
                        CodecType.VideoAudio => string.IsNullOrEmpty(codecProfile.Codec) ||
                            allowedVideo.Contains(codecProfile.Codec) ||
                            allowedAudio.Contains(codecProfile.Codec),
                        _ => true
                    })
                    .ToArray();
            }
        }

        private static string? FilterCodecList(string? commaSeparatedCodecs, HashSet<string> allowed)
        {
            if (string.IsNullOrWhiteSpace(commaSeparatedCodecs))
            {
                return commaSeparatedCodecs;
            }

            var kept = commaSeparatedCodecs
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(codec => allowed.Contains(codec));

            return string.Join(',', kept);
        }
    }
}
