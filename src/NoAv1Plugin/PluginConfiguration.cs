using System.Collections.Generic;
using MediaBrowser.Model.Plugins;

namespace NoAv1Plugin
{
    public class PluginConfiguration : BasePluginConfiguration
    {
        // Rules list; admin can edit the plugin configuration file to add/remove rules.
        public List<DeviceRule> Rules { get; set; } = new();
    }
}
