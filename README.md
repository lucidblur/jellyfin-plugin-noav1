# NoAv1Plugin

A small Jellyfin server plugin that can apply an in-memory DeviceProfile override to prevent AV1 direct-play for selected devices.

See `build/container-build.sh` to build the plugin (uses podman and the official dotnet SDK image).

Quick steps

1. Build the plugin (containerized):

   ./build/container-build.sh

2. Package and get checksum:

   ./scripts/package_and_checksum.sh

3. Host the zip and a manifest.json (use `package/manifest.template.json` as a starting point). Update `sourceUrl` and `checksum` in the manifest.

4. In Jellyfin Admin: Plugins -> Repositories -> Add new repository. Point it at your hosted manifest.json.
   Then install the plugin from Admin -> Plugins -> Available.

Alternative: copy the published plugin folder directly into the Jellyfin config volume under `plugins` and restart the server.

Configuration

After install, go to Admin -> Plugins -> No AV1 Device Overrides -> Configuration. Add rules (DeviceId is preferred). Example configuration JSON:

{
  "Rules": [
    { "DeviceId": "abcdef1234567890" },
    { "AppNameRegex": "^Chromecast.*4K", "DeviceId": null }
  ]
}

Notes on distinguishing devices

- DeviceId (the per-client device identifier) is the most reliable selector. You can obtain it from Admin -> Sessions or via the Sessions API.
- If DeviceId is not available or not practical, match by AppNameRegex + DeviceNameRegex or by remote IP (RemoteAddress). Be careful; those are less reliable and may affect correct devices.

