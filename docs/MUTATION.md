# Mutation testing

Stryker.NET changes the code one small piece at a time and runs the tests against each change. A `>` becomes `>=`, a statement is removed, `true` becomes `false`. These changed copies are called *mutants*. When a test fails, the mutant is *killed*. When every test still passes, the mutant *survived*, and that marks a place where the tests could miss a real bug.

- **Tool:** `dotnet-stryker` 5.1.0, Apache-2.0, by the Stryker Mutator project. It is pinned in `dotnet-tools.json` (ADR 0020).
- **How it runs:** by hand, not in CI.

```powershell
./scripts/run-mutation.ps1                 # all of ClipBench.Core, about 15 minutes
./scripts/run-mutation.ps1 -Project Storage # the encryption code in ClipBench.Storage
./scripts/run-mutation.ps1 -Mutate "**/Pasting/QueueRunner.cs"
```

The report is written to `tests/<project>/StrykerOutput/<time>/reports/mutation-report.html`, which git ignores.

## Settings

- **Mutation level:** Standard. Coverage analysis is per test, so each mutant runs only the tests that reach it.
- **`ConfigureAwait` is not mutated.** Changing `ConfigureAwait(false)` to `true` makes no difference outside a UI thread, and the Core and Storage tests have none. In the first run these mutants were 27 of the 41 survivors in `QueueRunner` alone.
- **The `break` threshold** in each `stryker-config.json` is the score recorded below, rounded down. A run that scores lower fails.

## Scores (2026-11-20)

| Run | Score | Killed | Survived or not reached |
|---|---|---|---|
| `ClipBench.Core`, first run | 82.79% | 3,277 | 681 |
| `ClipBench.Core`, after the new tests and with `ConfigureAwait` not mutated | 84.89% | 3,237 | 576 |
| `ClipBench.Storage` `Crypto/` and `Keys/`, first run | 83.62% | 97 | 19 |
| `ClipBench.Storage` `Crypto/` and `Keys/`, after the new tests | 92.24% | 107 | 9 |

The Core files with the most survivors:
- the code-language guesser (`CodeDetector`, 102 survivors, 38%);
- the OCR text tidying (`ReadingText`, 67, 72%, and `ReadingMerge`, 40, 78%);
- the JSON transforms and find-and-replace (21 each, 77% and 79%);
- `ClipLibrary` (20, 80%).

None of them decides what is captured, stored, masked or pasted. They are listed so a later phase can raise them.

## The critical files

PLAN.md names the capture filter, the paste queue and the crypto wrappers. The queue runner and the secret detector are included because they decide what is pasted and what is masked. Every mutant that survives in these files is either killed by a test added in Phase 9 or explained here.

### Killed by tests added in Phase 9

| File | Mutant | Test |
|---|---|---|
| `SensitiveDetector` | An end line straight after `-----BEGIN ` not taken as the end (`footer > 0`) | `An_end_line_right_after_the_begin_marker_still_ends_the_block_and_a_token_after_it_is_flagged` |
| `SensitiveDetector` | The block blanked only up to its end line, when that line is the last line | `A_block_whose_end_line_is_the_last_line_is_blanked_to_the_end_of_that_line` |
| `SensitiveDetector` | The block blanked short of its end line (`footer - lineEnd`) | `Every_line_of_a_block_is_blanked_up_to_its_end_line_whatever_the_length_of_that_line` |
| `SensitiveDetector` | The pooled copy of the text returned without clearing it | `The_copy_made_to_blank_certificate_blocks_is_cleared_before_it_goes_back_to_the_shared_pool` |
| `QueueRunner` | Going on after a failed re-claim of Ctrl+V | `When_ctrl_v_cannot_be_claimed_again_the_stop_is_a_failure_not_a_fault` |
| `QueueRunner` | No announcement when a failed paste stops the queue | `A_queue_stopped_by_a_failed_paste_announces_that_it_stopped` |
| `QueueRunner` | An exception while handling a key not caught, or logged without its type | `An_exception_while_handling_a_key_is_logged_by_type_and_goes_no_further` |
| `AesGcmCryptoService` | A disposed service answering a value too short to open | `A_disposed_service_refuses_even_a_value_too_short_to_open` |
| `MasterKeyStore` | A key file of exactly 4,096 bytes not overwritten before it is deleted | `A_key_file_of_the_largest_size_a_key_file_can_have_is_overwritten_before_it_is_deleted` |
| `MasterKeyStore` | Overwriting files of any size | `A_file_too_large_to_be_a_key_file_is_deleted_without_being_overwritten` |
| `MasterKeyStore` | A cancelled delete going ahead | `A_cancelled_delete_throws_and_leaves_the_file` |
| `MasterKeyStore` | The unwrapped copy used to check a new key not wiped | `The_copy_unwrapped_to_check_a_new_key_is_wiped` |
| `MasterKeyStore` | The disk-error handling in load and delete (never reached by a test before) | `A_key_file_another_program_holds_open_is_a_disk_error_for_reading_and_for_deleting` |

### Explained survivors

These changes make no difference anyone could observe: a test that kills them would test nothing a user or an attacker could notice.

| File | Line | Mutant | Why it makes no observable difference |
|---|---|---|---|
| `CaptureFilter` | 71 | The message of the "unknown clip format" exception emptied | A bug guard for a value the enum cannot hold; the type is what callers see |
| `PasteQueue` | 103, 108, 132, 134, 147 | Result messages emptied | Callers act on the error code. The text is never shown: the runner and the popup write their own |
| `QueueRunner` | 99 | `ThrowIfNull(clips)` removed | `PasteQueue.Arm` throws the same exception a line later |
| `QueueRunner` | 107 | The running queue not cancelled before a new one is armed | `Arm` replaces an armed queue anyway, and the keys are released on the next line |
| `QueueRunner` | 176, 187 | The second-dispose guard or the semaphore release removed | After disposal nothing waits on the runner, and a second release of keys already released does nothing |
| `QueueRunner` | 203, 238, 245 | Early returns for a press after disposal, a stale key id or an empty queue (not reached) | Race guards: a test with synchronous fakes cannot interleave a press between the key check and the step lock. The paths before them are tested |
| `QueueRunner` | 252, 311, 320 | Going on after a failed paste | The queue is already cancelled, so `Complete` fails and the step returns with the same state, keys and messages |
| `SensitiveDetector` | 71 | The pooled copy not returned at all | Nothing is left in the pool for anyone to read; the clearing itself is tested |
| `SensitiveDetector` | 78 | `offset < text.Length` becomes `<=` | At the end, the remaining text is empty and the search returns at once |
| `SensitiveDetector` | 94 | `lineEnd < 0` becomes `<= 0` | The end line starts with `-----END `, so a line break is never at its first character |
| `AesGcmCryptoService` | 44 | The wrong-key-size message emptied | A bug guard; the exception type is what matters |
| `AesGcmCryptoService` | 146 | The second-dispose guard removed | The keys are wiped again, which is harmless |
| `MasterKey` | 23, 59 | The size message emptied; the second-dispose guard removed | As above |
| `MasterKeyStore` | 77 | `length > 0` becomes `>= 0` | An empty file is opened and nothing is written |
| `MasterKeyStore` | 85, 214 | `WriteThrough \| Asynchronous` becomes `&` | Whether the bytes reach the disk before the call returns cannot be seen from a test; the content written is the same |
| `MasterKeyStore` | 107 | The "could not protect the key" message emptied | The code `KeyProtectionFailed` is what the start-up screen acts on |
| `MasterKeyStore` | 182 | A new key that was not stored is not wiped | The key object is dropped and never reachable again; wiping it is defence in depth that no test can see without reaching into its memory |
