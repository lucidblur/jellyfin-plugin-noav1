# NoAv1Plugin

A small Jellyfin server plugin that applies a restricted DeviceProfile override to prevent AV1 (and any other codec you choose) from being offered to selected devices — either for direct-play or as a transcode target.

See `build/container-build.sh` to build the plugin (uses podman/docker and the official dotnet SDK image).

Quick steps

1. Build the plugin (containerized):

   ./build/container-build.sh

2. Package it as an installable zip + repository manifest:

   ./build/package-repo.sh [BASE_URL]

   BASE_URL defaults to `http://localhost:8095` and must be reachable from wherever the
   Jellyfin *server* itself runs, not just from your shell — see the comment at the top of
   `build/package-repo.sh` for the container-networking gotchas (Podman/Docker host-reachable
   hostnames).

3. Serve the `repo/` directory it wrote, e.g.:

   python3 -m http.server 8095 --directory repo

4. In Jellyfin Admin: Plugins -> Repositories -> Add Repository, pointing at your served
   `manifest.json`. Then install "No AV1 Device Overrides" from Plugins -> Catalog.

Alternative: copy the published plugin folder directly into the Jellyfin config volume under
`plugins` and restart the server.

Configuration

After install, go to Dashboard -> Plugins -> No AV1 Device Overrides to open its settings page.
Pick a previously-connected device from the dropdown to see which codecs it has actually
reported supporting (unsupported ones are greyed out); or build a rule from an app/device-name
regex or remote address pattern instead of a specific device. Rules are saved through the
standard plugin configuration API, same as any other plugin.

Notes on distinguishing devices

- DeviceId (the per-client device identifier) is the most reliable selector.
- If DeviceId is not available or not practical, match by AppNameRegex + DeviceNameRegex or by
  remote IP (RemoteAddress). Be careful; those are less reliable and may affect the wrong
  devices.

Versioning

This project follows [SemVer 2.0.0](https://semver.org/) (`MAJOR.MINOR.PATCH`). The single
source of truth is `"version"` in `src/NoAv1Plugin/plugin.json`; the csproj reads it directly
at build time so the compiled assembly can never drift out of sync with it.

One constraint: Jellyfin's plugin version is a plain 4-part `System.Version`, with no concept
of a SemVer `-prerelease` or `+build` suffix — so `plugin.json`'s version must stay plain
`MAJOR.MINOR.PATCH` (`package-repo.sh` enforces this and fails loudly otherwise). Bump it like
normal SemVer: `PATCH` for fixes, `MINOR` for backwards-compatible additions, `MAJOR` for
breaking changes (e.g. a `DeviceRule` config shape change existing installs can't read).
