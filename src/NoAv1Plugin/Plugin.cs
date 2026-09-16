using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Jellyfin.Data.Enums;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;
using MediaBrowser.Controller.Session;
using MediaBrowser.Controller.Devices;
using Microsoft.Extensions.Logging;
using MediaBrowser.Model.Dlna;
using MediaBrowser.Model.Session;

namespace NoAv1Plugin
{
    public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
    {
        public static Plugin Instance { get; private set; } = null!;

        private readonly ISessionManager _sessionManager;
        private readonly IDeviceManager _deviceManager;
        private readonly ILogger<Plugin> _logger;

        public override Guid Id => new Guid("f3b6a9d6-6a7e-4b4c-9e9b-6d9b5c5a6f11");
        public override string Name => "No AV1 Device Overrides";
        public override string Description => "Apply a device profile override (remove AV1 direct-play) for configured devices.";

        public Plugin(
            IApplicationPaths applicationPaths,
            IXmlSerializer xmlSerializer,
            ISessionManager sessionManager,
            IDeviceManager deviceManager,
            ILogger<Plugin> logger)
            : base(applicationPaths, xmlSerializer)
        {
            Instance = this;
            _sessionManager = sessionManager;
            _deviceManager = deviceManager;
            _logger = logger;

            // Subscribe to session started events
            _sessionManager.SessionStarted += OnSessionStarted;
        }

        public IEnumerable<PluginPageInfo> GetPages()
        {
            yield return new PluginPageInfo
            {
                // The dashboard fetches this page from /web/ConfigurationPage?name=<Name>, and
                // that response carries no caching headers at all (nor can a plugin add any --
                // it's core Jellyfin code, and plugins only get DI registration hooks, not a
                // pipeline/middleware one). Folding the version into Name instead means every
                // update is a brand-new URL the browser has never cached, so it's always fetched
                // fresh after an update while still caching normally within one version's
                // lifetime -- no manual hard-refresh needed after installing a new version.
                Name = string.Format(CultureInfo.InvariantCulture, "NoAv1Plugin-{0}", Version),
                DisplayName = Name,
                EmbeddedResourcePath = string.Format(CultureInfo.InvariantCulture, "{0}.Configuration.configPage.html", GetType().Namespace)
            };
        }

        private void OnSessionStarted(object? sender, MediaBrowser.Controller.Session.SessionEventArgs e)
        {
            try
            {
                if (e?.SessionInfo is not null)
                {
                    EvaluateAndApply(e.SessionInfo);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "NoAv1Plugin: error handling SessionStarted");
            }
        }

        private void EvaluateAndApply(SessionInfo session)
        {
            if (string.IsNullOrEmpty(session.DeviceId))
            {
                return;
            }

            var rule = FindMatchingRule(Configuration?.Rules, session.Client, session.DeviceName, session.RemoteEndPoint);
            if (rule is not null)
            {
                ApplyOverride(session.DeviceId, rule);
            }
        }

        /// <summary>
        /// Finds the first configured rule matching the given device/session attributes.
        /// Shared by the SessionStarted-based capability override and the playback-info action
        /// filter (<see cref="Api.NoAv1PlaybackInfoFilter"/>), which is the mechanism that
        /// actually enforces restrictions against clients that submit their own DeviceProfile
        /// (SaveCapabilities-based overrides are only ever consulted by the server as a fallback
        /// for clients that submit none).
        /// </summary>
        /// <remarks>
        /// No DeviceId matching: it's a per-request, client-self-reported value
        /// (Jellyfin.Api.Auth.AuthorizationContext parses it straight off the auth header, never
        /// validated against a Devices row) and was observed live to change for the same
        /// physical device across a reconnect. AppNameRegex/DeviceNameRegex/RemoteAddress are
        /// what's actually reliable here.
        /// </remarks>
        public static DeviceRule? FindMatchingRule(
            IEnumerable<DeviceRule>? rules,
            string? appName,
            string? deviceName,
            string? remoteAddress)
        {
            if (rules is null)
            {
                return null;
            }

            foreach (var rule in rules)
            {
                if (!string.IsNullOrWhiteSpace(rule.AppNameRegex) && !string.IsNullOrEmpty(appName) &&
                    Regex.IsMatch(appName, rule.AppNameRegex, RegexOptions.IgnoreCase))
                {
                    return rule;
                }

                if (!string.IsNullOrWhiteSpace(rule.DeviceNameRegex) && !string.IsNullOrEmpty(deviceName) &&
                    Regex.IsMatch(deviceName, rule.DeviceNameRegex, RegexOptions.IgnoreCase))
                {
                    return rule;
                }

                if (!string.IsNullOrWhiteSpace(rule.RemoteAddress) && !string.IsNullOrEmpty(remoteAddress) &&
                    remoteAddress.Contains(rule.RemoteAddress, StringComparison.OrdinalIgnoreCase))
                {
                    return rule;
                }
            }

            return null;
        }

        private static readonly string[] DefaultVideoCodecs = { "h264", "hevc" };
        private static readonly string[] DefaultAudioCodecs = { "aac", "mp3" };

        private void ApplyOverride(string deviceId, DeviceRule rule)
        {
            var videoCodecs = rule.AllowedVideoCodecs is { Count: > 0 } ? rule.AllowedVideoCodecs.ToArray() : DefaultVideoCodecs;
            var audioCodecs = rule.AllowedAudioCodecs is { Count: > 0 } ? rule.AllowedAudioCodecs.ToArray() : DefaultAudioCodecs;

            var videoCodecList = string.Join(',', videoCodecs);
            var audioCodecList = string.Join(',', audioCodecs);

            var deviceProfile = new DeviceProfile
            {
                Name = "NoAV1-Override",
                DirectPlayProfiles = new[]
                {
                    new DirectPlayProfile
                    {
                        Container = "mp4",
                        Type = DlnaProfileType.Video,
                        VideoCodec = videoCodecList,
                        AudioCodec = audioCodecList
                    }
                },
                CodecProfiles = videoCodecs
                    .Select(codec => new CodecProfile { Type = CodecType.Video, Codec = codec })
                    .ToArray(),
                // One transcoding profile per allowed video codec, so the server can fall back to
                // HEVC (or whichever codecs the rule allows) instead of always transcoding to H.264.
                TranscodingProfiles = videoCodecs
                    .Select(codec => new TranscodingProfile
                    {
                        Container = "mp4",
                        Type = DlnaProfileType.Video,
                        VideoCodec = codec,
                        AudioCodec = audioCodecList,
                        Protocol = MediaStreamProtocol.http
                    })
                    .ToArray()
            };

            var caps = new ClientCapabilities
            {
                DeviceProfile = deviceProfile
            };

            try
            {
                _deviceManager.SaveCapabilities(deviceId, caps);
                _logger?.LogInformation(
                    "NoAv1Plugin: applied override for device {DeviceId} (video: {VideoCodecs}; audio: {AudioCodecs})",
                    deviceId,
                    videoCodecList,
                    audioCodecList);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "NoAv1Plugin: failed to apply override for device {DeviceId}", deviceId);
            }
        }
    }
}
