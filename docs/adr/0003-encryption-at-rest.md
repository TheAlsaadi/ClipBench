---
status: accepted
---

# Encryption at rest

Clip content, previews and source-app names are stored as AES-256-GCM ciphertext (`nonce(12) || ciphertext || tag(16)`), with the row id and column name as associated data so a value cannot be moved to another row or column. The 32-byte master key is wrapped with DPAPI (`CurrentUser` scope plus app-specific entropy) and stored in `%LOCALAPPDATA%\ClipBench\key.bin`. Separate encryption and HMAC subkeys are derived with HKDF. The duplicate-detection hash is HMAC-SHA256 under a derived key, so it cannot confirm a guessed clip. Ids, timestamps, format, pinned flag, board, size and the hash stay in plain columns so retention and listing work without decrypting. Search uses an in-memory index that is decrypted at startup and cleared on exit and on wipe.

This protects history against other Windows users, offline disk access, backups and a stolen disk while the user is logged out. It does not protect against malware running as the same user, which can read the clipboard directly, and timestamps and item counts remain visible.

## Considered options

- **Whole-database encryption (SQLCipher).** Rejected. It needs a different native SQLite build than the one Microsoft.Data.Sqlite ships by default, which means a build to maintain, sign and audit. Field-level encryption with the stock build keeps the dependency set small and binds each value to its row.
- **Relying on BitLocker or file permissions alone.** Rejected. BitLocker protects only a locked, powered-off disk, and file permissions do not follow the files into backups or off the machine.

## Consequences

- Plain metadata is a deliberate, documented leak (see the threat model).
- A corrupted or unreadable key file means the history cannot be decrypted. The app reports it clearly and offers to start fresh instead of crashing.
- Every crypto failure fails closed.
