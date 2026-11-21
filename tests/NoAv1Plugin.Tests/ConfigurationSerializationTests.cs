using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Serialization;
using Xunit;

namespace NoAv1Plugin.Tests
{
    public class ConfigurationSerializationTests
    {
        private static readonly XmlSerializer Serializer = new(typeof(PluginConfiguration));

        private static string Serialize(PluginConfiguration config)
        {
            using var writer = new StringWriter();
            Serializer.Serialize(writer, config);
            return writer.ToString();
        }

        private static PluginConfiguration Deserialize(string xml)
        {
            using var reader = new StringReader(xml);
            return (PluginConfiguration)Serializer.Deserialize(reader)!;
        }

        private static PluginConfiguration OneRuleConfig() => new()
        {
            Rules = new List<DeviceRule>
            {
                new()
                {
                    DeviceNameRegex = "Living Room TV",
                    AllowedVideoCodecs = new List<string> { "h264", "hevc", "vp9" },
                    AllowedAudioCodecs = new List<string> { "aac", "opus" }
                }
            },
            DeviceCodecSnapshots = new List<DeviceCodecSnapshot>
            {
                new()
                {
                    DeviceName = "Living Room TV",
                    AppName = "Jellyfin Android TV",
                    VideoCodecs = new List<string> { "av1", "h264", "hevc" },
                    AudioCodecs = new List<string> { "aac", "ac3" },
                    LastSeenUtc = new DateTime(2026, 11, 20, 0, 0, 0, DateTimeKind.Utc)
                }
            }
        };

        private static void AssertUnchanged(PluginConfiguration config)
        {
            var rule = Assert.Single(config.Rules);
            Assert.Equal(new[] { "h264", "hevc", "vp9" }, rule.AllowedVideoCodecs);
            Assert.Equal(new[] { "aac", "opus" }, rule.AllowedAudioCodecs);

            var snapshot = Assert.Single(config.DeviceCodecSnapshots);
            Assert.Equal(new[] { "av1", "h264", "hevc" }, snapshot.VideoCodecs);
            Assert.Equal(new[] { "aac", "ac3" }, snapshot.AudioCodecs);
        }

        [Fact]
        public void DeserializingTwiceInARow_LeavesListsUnchanged()
        {
            var xml = Serialize(OneRuleConfig());

            // Each deserialize is a fresh object graph, as on every Jellyfin startup.
            var first = Deserialize(xml);
            AssertUnchanged(first);
            var second = Deserialize(xml);
            AssertUnchanged(second);
        }

        [Fact]
        public void LoadSaveLoadCycles_DoNotAccumulateEntries()
        {
            // The real-world failure: load -> SaveConfiguration() -> load -> ... grew the lists.
            var xml = Serialize(OneRuleConfig());
            for (var i = 0; i < 3; i++)
            {
                var loaded = Deserialize(xml);
                AssertUnchanged(loaded);
                Assert.False(loaded.DeduplicateRuleCodecs());
                xml = Serialize(loaded);
            }
        }

        [Fact]
        public void RuleWithoutSavedCodecs_DeserializesToEmptyLists()
        {
            // Rules saved before the codec fields existed have no elements for them; they must
            // come back empty so the consumers' H.264/HEVC + AAC/MP3 fallback applies.
            var xml = Serialize(new PluginConfiguration { Rules = { new DeviceRule { AppNameRegex = "Roku" } } });
            xml = xml.Replace("<AllowedVideoCodecs />", string.Empty, StringComparison.Ordinal)
                .Replace("<AllowedAudioCodecs />", string.Empty, StringComparison.Ordinal);
            Assert.DoesNotContain("AllowedVideoCodecs", xml, StringComparison.Ordinal);

            var rule = Assert.Single(Deserialize(xml).Rules);
            Assert.Empty(rule.AllowedVideoCodecs);
            Assert.Empty(rule.AllowedAudioCodecs);
        }

        [Fact]
        public void ExistingDuplicates_AreRemovedKeepingFirstSeenOrder()
        {
            // Shape of a config written by <= 2.3.0 after a few restarts (from a live server log).
            var config = Deserialize(Serialize(new PluginConfiguration
            {
                Rules =
                {
                    new DeviceRule
                    {
                        DeviceNameRegex = "Shield",
                        AllowedVideoCodecs = new List<string> { "h264", "hevc", "h264", "hevc", "h264", "hevc", "vp9", "vp8", "mpeg2video", "mpeg4" },
                        AllowedAudioCodecs = new List<string> { "aac", "mp3", "aac", "mp3", "ac3" }
                    },
                    new DeviceRule
                    {
                        AppNameRegex = "Roku",
                        AllowedVideoCodecs = new List<string> { "H264", "hevc", "h264", "HEVC" },
                        AllowedAudioCodecs = new List<string> { "aac" }
                    }
                }
            }));

            Assert.True(config.DeduplicateRuleCodecs());

            Assert.Equal(new[] { "h264", "hevc", "vp9", "vp8", "mpeg2video", "mpeg4" }, config.Rules[0].AllowedVideoCodecs);
            Assert.Equal(new[] { "aac", "mp3", "ac3" }, config.Rules[0].AllowedAudioCodecs);
            Assert.Equal(new[] { "H264", "hevc" }, config.Rules[1].AllowedVideoCodecs);
            Assert.Equal(new[] { "aac" }, config.Rules[1].AllowedAudioCodecs);

            // Normalized result survives a save/load and reports nothing left to fix.
            var reloaded = Deserialize(Serialize(config));
            Assert.False(reloaded.DeduplicateRuleCodecs());
            Assert.Equal(new[] { "h264", "hevc", "vp9", "vp8", "mpeg2video", "mpeg4" }, reloaded.Rules[0].AllowedVideoCodecs);
        }

        [Fact]
        public void CleanConfig_ReportsNoChange()
        {
            var config = OneRuleConfig();
            Assert.False(config.DeduplicateRuleCodecs());
            AssertUnchanged(config);
            Assert.False(new PluginConfiguration().DeduplicateRuleCodecs());
        }
    }
}
