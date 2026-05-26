# Security Policy

## Supported Versions

Security fixes target the latest released version. Older releases are not
back-patched.

## Reporting a Vulnerability

Please **do not** open a public GitHub issue for security-sensitive reports.

Instead, use [GitHub's private vulnerability reporting][advisories] on this
repository. If that route is unavailable, open a minimal public issue asking
for a private channel and a maintainer will follow up.

[advisories]: https://github.com/aawhb/TeamsRelay/security/advisories/new

Include:

- A description of the issue and the impact
- Steps to reproduce
- Affected version (`teamsrelay --version`) and OS
- Any proof-of-concept code or logs (redact personal info)

You can expect an acknowledgement within a few business days. Coordinated
disclosure timelines are agreed per-report; ninety days is a reasonable default.

## Scope

In scope:

- The TeamsRelay CLI and libraries in this repository
- Default configuration as shipped

Out of scope:

- Vulnerabilities in KDE Connect, the Microsoft Teams client, or the .NET
  runtime — report those upstream
- Issues that require physical access to an unlocked machine on which the user
  has already authorised the application

## Threat Model Notes

TeamsRelay reads on-screen notification text from the Teams desktop app and
sends it to paired KDE Connect devices on the local network. It does not open
listening sockets, does not bypass Windows access controls, and does not store
credentials. Notification text is plaintext on the wire — KDE Connect's own
transport security applies.
