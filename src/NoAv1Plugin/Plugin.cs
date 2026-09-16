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

            var rules = Configuration?.Rules ?? new List<DeviceRule>();

            foreach (var rule in rules)
            {
                // match by DeviceId exact
                if (!string.IsNullOrWhiteSpace(rule.DeviceId) &&
                    string.Equals(rule.DeviceId, session.DeviceId, StringComparison.OrdinalIgnoreCase))
                {
                    ApplyOverride(session.DeviceId, rule);
                    return;
                }

                // match by AppName regex
                if (!string.IsNullOrWhiteSpace(rule.AppNameRegex) && !string.IsNullOrEmpty(session.Client))
                {
                    if (Regex.IsMatch(session.Client, rule.AppNameRegex, RegexOptions.IgnoreCase))
                    {
                        if (string.IsNullOrWhiteSpace(rule.DeviceId) || string.Equals(rule.DeviceId, session.DeviceId, StringComparison.OrdinalIgnoreCase))
                        {
                            ApplyOverride(session.DeviceId, rule);
                            return;
                        }
                    }
                }

                // match by DeviceName regex
                if (!string.IsNullOrWhiteSpace(rule.DeviceNameRegex) && !string.IsNullOrEmpty(session.DeviceName))
                {
                    if (Regex.IsMatch(session.DeviceName, rule.DeviceNameRegex, RegexOptions.IgnoreCase))
                    {
                        if (string.IsNullOrWhiteSpace(rule.DeviceId) || string.Equals(rule.DeviceId, session.DeviceId, StringComparison.OrdinalIgnoreCase))
                        {
                            ApplyOverride(session.DeviceId, rule);
                            return;
                        }
                    }
                }

                // match by remote IP if specified in rule (optional field)
                if (!string.IsNullOrWhiteSpace(rule.RemoteAddress) && !string.IsNullOrEmpty(session.RemoteEndPoint))
                {
                    if (session.RemoteEndPoint!.Contains(rule.RemoteAddress, StringComparison.OrdinalIgnoreCase))
                    {
                        if (string.IsNullOrWhiteSpace(rule.DeviceId) || string.Equals(rule.DeviceId, session.DeviceId, StringComparison.OrdinalIgnoreCase))
                        {
                            ApplyOverride(session.DeviceId, rule);
                            return;
                        }
                    }
                }
            }
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
