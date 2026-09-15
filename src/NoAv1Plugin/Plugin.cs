using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
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
    public class Plugin : BasePlugin<PluginConfiguration>
    {
        public static Plugin Instance { get; private set; }

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
            _device_manager = deviceManager; // fallback if different casing
            _deviceManager = deviceManager;
            _logger = logger;

            // Subscribe to session started events
            _sessionManager.SessionStarted += OnSessionStarted;
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
                    ApplyOverride(session.DeviceId);
                    return;
                }

                // match by AppName regex
                if (!string.IsNullOrWhiteSpace(rule.AppNameRegex) && !string.IsNullOrEmpty(session.Client))
                {
                    if (Regex.IsMatch(session.Client, rule.AppNameRegex, RegexOptions.IgnoreCase))
                    {
                        if (string.IsNullOrWhiteSpace(rule.DeviceId) || string.Equals(rule.DeviceId, session.DeviceId, StringComparison.OrdinalIgnoreCase))
                        {
                            ApplyOverride(session.DeviceId);
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
                            ApplyOverride(session.DeviceId);
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
                            ApplyOverride(session.DeviceId);
                            return;
                        }
                    }
                }
            }
        }

        private void ApplyOverride(string deviceId)
        {
            var deviceProfile = new DeviceProfile
            {
                Name = "NoAV1-Override",
                DirectPlayProfiles = new[]
                {
                    new DirectPlayProfile
                    {
                        Container = "mp4",
                        Type = DlnaProfileType.Video,
                        VideoCodec = "h264,hevc", // exclude av1
                        AudioCodec = "aac,mp3"
                    }
                },
                CodecProfiles = new[]
                {
                    new CodecProfile { Type = DlnaProfileType.Video, Codec = "h264" },
                    new CodecProfile { Type = DlnaProfileType.Video, Codec = "hevc" }
                },
                TranscodingProfiles = new[]
                {
                    new TranscodingProfile { Container = "mp4", Type = "Video", VideoCodec = "h264", AudioCodec = "aac", Protocol = "http" }
                }
            };

            var caps = new ClientCapabilities
            {
                DeviceProfile = deviceProfile
            };

            try
            {
                _deviceManager.SaveCapabilities(deviceId, caps);
                _logger?.LogInformation("NoAv1Plugin: applied override for device {DeviceId}", deviceId);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "NoAv1Plugin: failed to apply override for device {DeviceId}", deviceId);
            }
        }
    }
}
