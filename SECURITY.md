# Security

DeckHost runs plugins, serves the Web Deck on your network when you turn sharing on, installs
updates and drives USB devices, so security problems matter. Thank you for reporting them privately.

## Reporting a vulnerability

Use **[Report a vulnerability](https://github.com/vman26/DeckHost/security/advisories/new)**
(GitHub's private advisories). Please include what's affected, how to reproduce it and what an
attacker could do. Don't open a public issue for it.

You'll get an answer within a week. Fixes go into the next release; you're credited in the release
notes unless you'd rather not be.

## Supported versions

Only the latest release gets security fixes. DeckHost tells you when an update is available.

## What DeckHost does to stay safe

- Updates are checked against the release's `SHA256SUMS.txt`; plugin catalog downloads against
  their `sha256`; firmware downloads must come over HTTPS and match their checksum.
- DeckHost plugins run in their own process by default; Stream Deck and OpenAction plugins always
  do. Plugins still run with your user's rights; their declared permissions are shown, not enforced.
- The Web Deck listens only on this computer unless you turn sharing on, and every request needs
  its pairing code.
- Crash reports and support bundles stay on your computer; nothing is sent by itself.
