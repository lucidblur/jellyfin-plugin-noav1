using System;
using System.Collections.Generic;
using System.Linq;
using MediaBrowser.Model.Plugins;

namespace NoAv1Plugin
{
    public class PluginConfiguration : BasePluginConfiguration
    {
        // Rules list; admin can edit the plugin configuration file to add/remove rules.
        // Every List<T> property in this configuration graph must be initialized empty: see
        // DeviceRule.AllowedVideoCodecs for how XmlSerializer appends to a pre-populated list.
        public List<DeviceRule> Rules { get; set; } = new();

        // Per-device codec snapshots captured from real playback requests, keyed by device name.
        // See DeviceCodecSnapshot for why this exists instead of relying on IDeviceManager's
        // (in-memory-only, restart-wiped) capabilities store.
        public List<DeviceCodecSnapshot> DeviceCodecSnapshots { get; set; } = new();

        /// <summary>
        /// Removes duplicate entries from every rule's AllowedVideoCodecs/AllowedAudioCodecs
        /// (case-insensitively, keeping the first-seen spelling and order). Cleans up configs
        /// written by versions before 2.3.1, where DeviceRule's pre-populated default lists made
        /// each load prepend another "h264,hevc" / "aac,mp3" that was then saved back.
        /// </summary>
        /// <returns><c>true</c> if anything was removed (i.e. the configuration should be saved).</returns>
        public bool DeduplicateRuleCodecs()
        {
            var changed = false;

            foreach (var rule in Rules)
            {
                if (rule is null)
                {
                    continue;
                }

                changed |= Deduplicate(rule.AllowedVideoCodecs, list => rule.AllowedVideoCodecs = list);
                changed |= Deduplicate(rule.AllowedAudioCodecs, list => rule.AllowedAudioCodecs = list);
            }

            return changed;
        }

        private static bool Deduplicate(List<string>? codecs, Action<List<string>> assign)
        {
            if (codecs is null)
            {
                assign(new List<string>());
                return true;
            }

            // Distinct() keeps the first occurrence of each value, in source order.
            var distinct = codecs.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (distinct.Count == codecs.Count)
            {
                return false;
            }

            assign(distinct);
            return true;
        }
    }
}
