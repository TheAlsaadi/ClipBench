# Security policy

ClipBench handles everything you copy, including passwords and tokens, so security
reports are taken seriously.

## Reporting a vulnerability

**Do not open a public issue.** Report it privately with GitHub's vulnerability
reporting form:

<https://github.com/TheAlsaadi/ClipBench/security/advisories/new>

Only the maintainers can see the report. Please include:

- what you found and which version or commit it affects
- steps to reproduce it, or a proof of concept
- what an attacker could do with it
- whether you want to be credited

Never include real clipboard contents, passwords or keys in a report. Describe
them, or use dummy values.

## What to expect

This is a small project, so these are targets, not guarantees:

- an acknowledgement within 7 days
- an initial assessment within 14 days
- a fix or a mitigation plan, and a coordinated disclosure date, agreed with you
  (the default window is 90 days)

## Supported versions

ClipBench has not had a release yet. Once it does, only the latest release is
supported.

## Scope

In scope:

- the application, its installer and its portable build
- the encrypted history: key handling, the database, wipe and expiry
- clipboard capture, paste injection and the privacy markers
- the build and release workflows in this repository

Out of scope, because they are limits of the design rather than bugs (see
[the threat model](docs/security/THREAT_MODEL.md)):

- malware already running as the same Windows user, which can read the clipboard
  directly
- an attacker who is logged in to your unlocked session
- timestamps and item counts in the history database, which are visible metadata

## Good-faith research

If you follow this policy, avoid harming other people's data, and give us a
reasonable time to respond, we will not take action against you for your
research.
