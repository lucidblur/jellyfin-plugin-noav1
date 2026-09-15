using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Jellyfin.Data.Queries;
using MediaBrowser.Common.Api;
using MediaBrowser.Common.Configuration;
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
        // Matches upload_{clientName}_{clientVersion}_{yyyyMMddHHmmss}_{32-hex-guid}.log, the
        // exact naming ClientEventLogger.WriteDocumentAsync uses for client "Send Logs" uploads.
        // Anchoring on this is also what keeps GetLogContent safe against path traversal: a
        // filename that doesn't match this can't be read, full stop.
        private static readonly Regex UploadLogFileNamePattern = new(
            @"^upload_(?<name>.+)_(?<version>[^_]+)_(?<timestamp>\d{14})_(?<guid>[0-9a-fA-F]{32})\.log$",
            RegexOptions.Compiled);

        private const int MaxLogContentBytes = 2_000_000;

        private readonly IDeviceManager _deviceManager;
        private readonly IApplicationPaths _applicationPaths;

        public NoAv1Controller(IDeviceManager deviceManager, IApplicationPaths applicationPaths)
        {
            _deviceManager = deviceManager;
            _applicationPaths = applicationPaths;
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

        [HttpGet("Logs")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public ActionResult<IEnumerable<ClientLogFileDto>> GetLogFiles()
        {
            if (!Directory.Exists(_applicationPaths.LogDirectoryPath))
            {
                return Ok(Array.Empty<ClientLogFileDto>());
            }

            var files = Directory.EnumerateFiles(_applicationPaths.LogDirectoryPath, "upload_*.log")
                .Select(ToLogFileDto)
                .Where(dto => dto is not null)
                .Select(dto => dto!)
                .OrderByDescending(dto => dto.Timestamp);

            return Ok(files);
        }

        [HttpGet("Logs/{fileName}")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public ActionResult<ClientLogContentDto> GetLogContent([FromRoute] string fileName)
        {
            // Reject anything that doesn't look like an upload_*.log filename before it ever
            // touches the filesystem -- this, not just Path.GetFileName, is what makes it safe
            // to read a client-supplied filename off disk.
            if (!UploadLogFileNamePattern.IsMatch(fileName))
            {
                return BadRequest("Not a recognized client log file name.");
            }

            var path = Path.Combine(_applicationPaths.LogDirectoryPath, Path.GetFileName(fileName));
            if (!System.IO.File.Exists(path))
            {
                return NotFound();
            }

            using var stream = System.IO.File.OpenRead(path);
            var truncated = stream.Length > MaxLogContentBytes;
            var buffer = new byte[Math.Min(stream.Length, MaxLogContentBytes)];
            var read = stream.Read(buffer, 0, buffer.Length);

            return new ClientLogContentDto
            {
                FileName = fileName,
                Content = System.Text.Encoding.UTF8.GetString(buffer, 0, read),
                Truncated = truncated
            };
        }

        private static ClientLogFileDto? ToLogFileDto(string path)
        {
            var fileName = Path.GetFileName(path);
            var match = UploadLogFileNamePattern.Match(fileName);
            if (!match.Success)
            {
                return null;
            }

            DateTime? timestamp = DateTime.TryParseExact(
                match.Groups["timestamp"].Value,
                "yyyyMMddHHmmss",
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var parsed)
                ? parsed
                : null;

            long size;
            try
            {
                size = new FileInfo(path).Length;
            }
            catch (IOException)
            {
                size = 0;
            }

            return new ClientLogFileDto
            {
                FileName = fileName,
                ClientName = match.Groups["name"].Value,
                ClientVersion = match.Groups["version"].Value,
                Timestamp = timestamp,
                SizeBytes = size
            };
        }

        private DeviceCapabilityDto ToDto(DeviceInfo device)
        {
            // DeviceManager.ToDeviceInfo (server-side) fetches a device's ClientCapabilities
            // but never assigns them onto the returned DeviceInfo.Capabilities -- it stays the
            // default empty ClientCapabilities for every device, regardless of what the device
            // actually reported. Fetch capabilities ourselves instead of trusting that field.
            var profile = _deviceManager.GetCapabilities(device.Id)?.DeviceProfile;

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
