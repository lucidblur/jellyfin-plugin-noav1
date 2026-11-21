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
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace NoAv1Plugin.Api
{
    // Read-only helper endpoints for the plugin's admin config page: the canonical codec
    // list to render as checkboxes, and per-device codec support as claimed by each device's
    // last-reported DeviceProfile (used to grey out codecs a device never claimed to support).
    // The dashboard's config page fetches these on every load; unlike core Jellyfin's
    // /web/ConfigurationPage (which we can't add headers to at all -- see Plugin.GetPages),
    // this is our own controller, so we can and should stop the browser from ever serving a
    // stale cached response for devices/codecs/logs that have since changed.
    [ApiController]
    [Route("NoAv1")]
    [Authorize(Policy = Policies.RequiresElevation)]
    [ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
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
            var snapshots = Plugin.Instance?.Configuration?.DeviceCodecSnapshots ?? new List<DeviceCodecSnapshot>();
            var snapshotsByName = new Dictionary<string, DeviceCodecSnapshot>(StringComparer.OrdinalIgnoreCase);
            foreach (var snapshot in snapshots)
            {
                // First one wins on a duplicate name; in practice names shouldn't collide.
                snapshotsByName.TryAdd(snapshot.DeviceName, snapshot);
            }

            var fromKnownDevices = devices.Items.Select(device => ToDto(device, snapshotsByName)).ToList();

            // A device whose identity has drifted (its DeviceId changed -- see
            // NoAv1PlaybackInfoFilter's remarks) can have a codec snapshot with no corresponding
            // Devices row at all. Still worth surfacing in the picker.
            var coveredNames = new HashSet<string>(
                fromKnownDevices.Select(d => d.Name),
                StringComparer.OrdinalIgnoreCase);
            var synthetic = snapshots
                .Where(s => !coveredNames.Contains(s.DeviceName))
                .Select(ToSyntheticDto);

            return Ok(fromKnownDevices
                .Concat(synthetic)
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

        // Deliberately not using IDeviceManager.GetCapabilities(device.Id) here (as an earlier
        // version of this controller did): that store is purely in-memory, wiped on every
        // server restart, and separately, DeviceManager.ToDeviceInfo never assigns
        // ClientCapabilities onto the DeviceInfo it returns in the first place (a real bug --
        // see docs/jellyfin-devicemanager-capabilities-bug.md). The plugin's own persisted
        // DeviceCodecSnapshots, kept current by NoAv1PlaybackInfoFilter on every real playback
        // request, survives restarts and is what the admin UI should trust instead.
        private static DeviceCapabilityDto ToDto(DeviceInfo device, IReadOnlyDictionary<string, DeviceCodecSnapshot> snapshotsByName)
        {
            var name = string.IsNullOrWhiteSpace(device.Name) ? (device.Id ?? "Unknown device") : device.Name;
            DeviceCodecSnapshot? snapshot = null;
            if (!string.IsNullOrWhiteSpace(device.Name))
            {
                snapshotsByName.TryGetValue(device.Name, out snapshot);
            }

            return new DeviceCapabilityDto
            {
                Id = device.Id ?? string.Empty,
                Name = name,
                AppName = device.AppName,
                LastUserName = device.LastUserName,
                DateLastActivity = device.DateLastActivity,
                HasReportedProfile = snapshot is not null,
                ClaimedVideoCodecs = (IReadOnlyCollection<string>?)snapshot?.VideoCodecs ?? Array.Empty<string>(),
                ClaimedAudioCodecs = (IReadOnlyCollection<string>?)snapshot?.AudioCodecs ?? Array.Empty<string>()
            };
        }

        private static DeviceCapabilityDto ToSyntheticDto(DeviceCodecSnapshot snapshot)
        {
            return new DeviceCapabilityDto
            {
                // No real DeviceId to key on (that's exactly why this device has no Devices
                // row) -- the name is what rules actually match against anyway.
                Id = snapshot.DeviceName,
                Name = snapshot.DeviceName,
                AppName = snapshot.AppName,
                LastUserName = null,
                DateLastActivity = snapshot.LastSeenUtc,
                HasReportedProfile = true,
                ClaimedVideoCodecs = snapshot.VideoCodecs,
                ClaimedAudioCodecs = snapshot.AudioCodecs
            };
        }
    }
}
