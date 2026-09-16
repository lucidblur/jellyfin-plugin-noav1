using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Jellyfin.Api.Models.MediaInfoDtos;
using MediaBrowser.Controller.Devices;
using MediaBrowser.Model.Dlna;
using MediaBrowser.Model.Session;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Logging;

namespace NoAv1Plugin.Api
{
    /// <summary>
    /// Strips disallowed codecs from a matched device's own submitted DeviceProfile before
    /// MediaInfoController's playback-info action runs, and snapshots every device's
    /// as-submitted profile for the admin UI regardless of whether a rule matched.
    /// </summary>
    /// <remarks>
    /// MediaInfoController.GetPostedPlaybackInfo prefers the DeviceProfile the *client* posts
    /// in its request body over anything saved server-side via IDeviceManager.SaveCapabilities
    /// -- it only falls back to the saved one when the client submits none at all. Since every
    /// capable client (this plugin was written to handle -- Android TV, web, etc.) submits its
    /// own profile on every playback request, a SaveCapabilities-based override never actually
    /// influenced a real playback decision for them; it only affected clients that submit no
    /// profile, which isn't the common case this plugin exists for. This filter is the actual
    /// enforcement point: before StreamBuilder ever sees it, it replaces the incoming client
    /// profile's DirectPlayProfiles/CodecProfiles with filtered copies, so StreamBuilder computes
    /// PlayMethod/TranscodingUrl exactly as it normally would for a device that never claimed the
    /// disallowed codec -- rather than trying to patch SupportsDirectPlay after the fact, which
    /// would leave the response with no valid TranscodingUrl at all (SetDeviceSpecificData only
    /// computes one when SupportsDirectPlay is already false by the time StreamBuilder runs).
    ///
    /// It also opportunistically saves the device's *original, unrestricted* profile via
    /// SaveCapabilities -- the same store NoAv1Controller.GetDevices reads "claimed codecs" from
    /// for the admin UI. That store otherwise only reflects whatever a client registered once at
    /// session-start capability negotiation, which can go stale for a long-running session; a
    /// profile captured from an actual playback attempt is fresher and more likely to reflect
    /// what the device would really try to do. Copies are built rather than mutating the
    /// client's original DirectPlayProfile/CodecProfile objects in place, specifically so this
    /// snapshot -- taken before any restriction -- can't end up capturing our own restricted
    /// values instead of the device's real ones.
    /// </remarks>
    public class NoAv1PlaybackInfoFilter : IAsyncActionFilter
    {
        private readonly IDeviceManager _deviceManager;
        private readonly ILogger<NoAv1PlaybackInfoFilter> _logger;

        public NoAv1PlaybackInfoFilter(IDeviceManager deviceManager, ILogger<NoAv1PlaybackInfoFilter> logger)
        {
            _deviceManager = deviceManager;
            _logger = logger;
        }

        public Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            TryRestrictDeviceProfile(context);
            return next();
        }

        private void TryRestrictDeviceProfile(ActionExecutingContext context)
        {
            var dto = context.ActionArguments.Values.OfType<PlaybackInfoDto>().FirstOrDefault();
            var profile = dto?.DeviceProfile;
            if (profile is null)
            {
                _logger.LogDebug(
                    "NoAv1Plugin: filter invoked for {Action} but found no PlaybackInfoDto.DeviceProfile action argument (args: {Args})",
                    context.ActionDescriptor.DisplayName,
                    string.Join(", ", context.ActionArguments.Keys));
                return;
            }

            // Jellyfin's own auth claim types aren't exposed on a stable public surface a
            // plugin can reference without pulling in the whole Jellyfin.Api project just for
            // constants (Jellyfin.Api.Constants.InternalClaimTypes); these string values are
            // that project's own auth wiring and have been stable across versions.
            var user = context.HttpContext.User;
            var deviceId = user.FindFirst("Jellyfin-DeviceId")?.Value;

            if (!string.IsNullOrEmpty(deviceId))
            {
                SnapshotCapabilities(deviceId, profile);
            }

            var plugin = Plugin.Instance;
            if (plugin?.Configuration?.Rules is null || plugin.Configuration.Rules.Count == 0)
            {
                _logger.LogInformation("NoAv1Plugin: filter saw playback request from device {DeviceId} but no rules are configured", deviceId);
                return;
            }

            var appName = user.FindFirst("Jellyfin-Client")?.Value;
            var deviceName = string.IsNullOrEmpty(deviceId) ? null : _deviceManager.GetDevice(deviceId)?.Name;
            var remoteAddress = context.HttpContext.Connection.RemoteIpAddress?.ToString();

            var rule = Plugin.FindMatchingRule(plugin.Configuration.Rules, deviceId, appName, deviceName, remoteAddress);
            if (rule is null)
            {
                _logger.LogInformation(
                    "NoAv1Plugin: filter saw playback request from device {DeviceId} (app: {AppName}; device name: {DeviceName}; remote: {RemoteAddress}) but no configured rule matched",
                    deviceId,
                    appName,
                    deviceName,
                    remoteAddress);
                return;
            }

            _logger.LogInformation(
                "NoAv1Plugin: filter matched device {DeviceId} to rule {RuleLabel}; restricting submitted DeviceProfile to video=[{AllowedVideo}] audio=[{AllowedAudio}]",
                deviceId,
                rule.Label,
                rule.AllowedVideoCodecs is { Count: > 0 } ? string.Join(',', rule.AllowedVideoCodecs) : "h264,hevc (default)",
                rule.AllowedAudioCodecs is { Count: > 0 } ? string.Join(',', rule.AllowedAudioCodecs) : "aac,mp3 (default)");

            RestrictProfile(profile, rule);
        }

        private void SnapshotCapabilities(string deviceId, DeviceProfile profile)
        {
            try
            {
                // Only the two fields NoAv1Controller.ToDto actually reads -- no need to clone
                // (or risk missing a field of) the rest of DeviceProfile for this purpose. The
                // referenced arrays/objects are the client's original, untouched ones: this call
                // happens before RestrictProfile ever runs for this request.
                _deviceManager.SaveCapabilities(deviceId, new ClientCapabilities
                {
                    DeviceProfile = new DeviceProfile
                    {
                        DirectPlayProfiles = profile.DirectPlayProfiles,
                        CodecProfiles = profile.CodecProfiles
                    }
                });
            }
            catch (Exception ex)
            {
                // Best-effort only -- this must never block real playback.
                _logger.LogWarning(ex, "NoAv1Plugin: failed to snapshot capabilities for device {DeviceId}", deviceId);
            }
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

            // Build new DirectPlayProfile instances rather than mutating the client's own --
            // SnapshotCapabilities above has already captured (by reference) the originals, so
            // mutating them in place would corrupt that snapshot with our restricted values.
            if (profile.DirectPlayProfiles is not null)
            {
                profile.DirectPlayProfiles = profile.DirectPlayProfiles
                    .Select(directPlay => directPlay.Type switch
                    {
                        DlnaProfileType.Video => new DirectPlayProfile
                        {
                            Container = directPlay.Container,
                            Type = directPlay.Type,
                            VideoCodec = FilterCodecList(directPlay.VideoCodec, allowedVideo),
                            AudioCodec = FilterCodecList(directPlay.AudioCodec, allowedAudio)
                        },
                        DlnaProfileType.Audio => new DirectPlayProfile
                        {
                            Container = directPlay.Container,
                            Type = directPlay.Type,
                            VideoCodec = directPlay.VideoCodec,
                            AudioCodec = FilterCodecList(directPlay.AudioCodec, allowedAudio)
                        },
                        _ => directPlay
                    })
                    .ToArray();
            }

            // CodecProfile entries are only ever kept, not modified, so reusing the client's
            // original instances in this new (filtered) array is safe -- nothing about them is
            // mutated, only which ones are included.
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
