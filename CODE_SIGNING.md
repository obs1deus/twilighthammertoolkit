# Code signing policy

The project is applying for free code signing provided by [SignPath.io](https://about.signpath.io/), with a certificate from [SignPath Foundation](https://signpath.org/). Existing Toolkit releases remain unsigned.

- **Maintainer, committer, reviewer, and release approver** — [obs1deus](https://github.com/obs1deus).
- **Account security** — Maintainers must use multi-factor authentication for GitHub and SignPath. The maintainer reviews outside contributions before merging and manually approves each production signing request.
- **Build provenance** — Release candidates are built from this repository on GitHub-hosted Windows runners. The [build workflow](.github/workflows/toolkit-build.yml) uploads the unsigned Toolkit executable as an Actions artifact. The production signing policy will require verified GitHub build origin and maintainer approval.
- **Signing scope** — Signing is intended for Toolkit binaries built from this repository. Upstream addons and client mods retain their own publisher and distribution terms.
- **Privacy** — See the [privacy policy](PRIVACY.md) for network requests, Sync editions, and how to stop uploads.

## Release procedure

After SignPath approves the project, link the repository to the GitHub.com trusted build system and configure a release policy limited to reviewed release source. Require a manual approver and enforce the Toolkit product name and release version in the artifact configuration. Submit the `toolkit-unsigned` artifact through the trusted GitHub integration. Verify the signed file and its version before publishing. Include the build's release documentation in the Windows ZIP and publish the signed EXE as the standalone updater asset. Link this policy from the GitHub release notes and any download page hosting the signed build.

See the [SignPath terms](https://signpath.org/terms) and [GitHub integration documentation](https://docs.signpath.io/trusted-build-systems/github).
