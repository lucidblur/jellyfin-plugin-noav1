using System.Collections.Generic;
using MediaBrowser.Model.Plugins;

namespace NoAv1Plugin
{
    public class PluginConfiguration : BasePluginConfiguration
    {
        // Rules list; admin can edit the plugin configuration file to add/remove rules.
        public List<DeviceRule> Rules { get; set; } = new();

        // Per-device codec snapshots captured from real playback requests, keyed by device name.
        // See DeviceCodecSnapshot for why this exists instead of relying on IDeviceManager's
        // (in-memory-only, restart-wiped) capabilities store.
        public List<DeviceCodecSnapshot> DeviceCodecSnapshots { get; set; } = new();
    }
}
