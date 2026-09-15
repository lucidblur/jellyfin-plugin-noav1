namespace NoAv1Plugin
{
    public class DeviceRule
    {
        // Exact DeviceId string (unique per client device) — preferred for per-device targeting
        public string? DeviceId { get; set; }

        // Regex to match the AppName / Client string reported by the session (case-insensitive)
        public string? AppNameRegex { get; set; }

        // Regex to match the reported device name
        public string? DeviceNameRegex { get; set; }

        // Partial match for remote endpoint (IP) if desired
        public string? RemoteAddress { get; set; }
    }
}
