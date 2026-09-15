using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Data.Queries;
using MediaBrowser.Common.Api;
using MediaBrowser.Controller.Devices;
using MediaBrowser.Model.Devices;
using MediaBrowser.Model.Dlna;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace NoAv1Plugin.Api
{
    // Read-only helper endpoints for the plugin's admin config page: the canonical codec
    // list to render as checkboxes, and per-device codec support as claimed by each device's
    // last-reported DeviceProfile (used to grey out codecs a device never claimed to support).
    [ApiController]
    [Route("NoAv1")]
    [Authorize(Policy = Policies.RequiresElevation)]
    public class NoAv1Controller : ControllerBase
    {
        private readonly IDeviceManager _deviceManager;

        public NoAv1Controller(IDeviceManager deviceManager)
        {
            _deviceManager = deviceManager;
        }

        [HttpGet("Codecs")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public ActionResult<CodecListDto> GetKnownCodecs()
        {
            return new CodecListDto
            {
                VideoCodecs = KnownCodecs.Video,
                AudioCodecs = KnownCodecs.Audio
            };
        }

        [HttpGet("Devices")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public ActionResult<IEnumerable<DeviceCapabilityDto>> GetDevices()
        {
            var devices = _deviceManager.GetDeviceInfos(new DeviceQuery { Limit = 1000 });

            return Ok(devices.Items
                .Select(ToDto)
                .OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase));
        }

        private static DeviceCapabilityDto ToDto(DeviceInfo device)
        {
            var profile = device.Capabilities?.DeviceProfile;

            var claimedVideo = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var claimedAudio = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (profile is not null)
            {
                foreach (var directPlay in profile.DirectPlayProfiles ?? Array.Empty<DirectPlayProfile>())
                {
                    AddCodecs(claimedVideo, directPlay.VideoCodec);
                    AddCodecs(claimedAudio, directPlay.AudioCodec);
                }

                foreach (var codecProfile in profile.CodecProfiles ?? Array.Empty<CodecProfile>())
                {
                    if (codecProfile.Type is CodecType.Video or CodecType.VideoAudio)
                    {
                        AddCodecs(claimedVideo, codecProfile.Codec);
                    }

                    if (codecProfile.Type is CodecType.Audio or CodecType.VideoAudio)
                    {
                        AddCodecs(claimedAudio, codecProfile.Codec);
                    }
                }
            }

            return new DeviceCapabilityDto
            {
                Id = device.Id ?? string.Empty,
                Name = string.IsNullOrWhiteSpace(device.Name) ? (device.Id ?? "Unknown device") : device.Name,
                AppName = device.AppName,
                LastUserName = device.LastUserName,
                DateLastActivity = device.DateLastActivity,
                HasReportedProfile = profile is not null,
                ClaimedVideoCodecs = claimedVideo,
                ClaimedAudioCodecs = claimedAudio
            };
        }

        private static void AddCodecs(HashSet<string> target, string? commaSeparatedCodecs)
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
