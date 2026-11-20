# Performance

PLAN.md Phase 9 sets the budgets: a history of 10,000 clips (the most the settings allow), ready in under a second, and the popup open in under 100 ms. The memory budget below comes from what was measured.

## Budgets and measurements (2026-11-20)

All measurements were taken on one machine: Intel Core i7-13700KF, 32 GB, SSD, Windows 11 build 26300, x64 Release build. The history held 10,000 clips: 9,900 short texts and 100 pictures of about a megabyte each, 84 MB on disk in all. Each figure is the median of 5 runs.

**These are warm starts on a fast desktop.** The first run comes right after the test wrote the history, and the other four come straight after the app was ended, so Windows already had the files in memory. PLAN.md asks for under a second on a *mid-range* PC, and that is **not yet measured**. A cold start (after a restart of Windows) on a mid-range laptop reads the 84 MB from disk and does the same decryption on a slower processor; with 646 ms on this machine, the margin is not proven. Measuring it is an open item for the owner before v1.0. The command is at the end of this page.

| What | Budget | Measured | Test |
|---|---|---|---|
| App ready, from process start (warm) | under 1 s on a mid-range PC: not yet measured there | 646 ms | `PerformanceSmokeTests.Startup_with_10000_clips_is_ready_in_under_a_second` |
| App ready, from the first line of `Main` | | 601 ms | the same test, `AppReady ms=` in the log |
| Popup shown, from the shortcut press | under 100 ms | 26 ms (138 ms the first time) | `PerformanceSmokeTests.The_popup_opens_in_under_100_ms` |
| Private memory after loading 10,000 clips and opening the popup once | 370 MB (291 MB measured plus a quarter, rounded up to 10 MB) | 283 to 292 MB; working set 340 to 347 MB | `PerformanceSmokeTests.Memory_after_loading_10000_clips_stays_within_its_budget` |
| Open and read 10,000 clips from storage (fake key protector) | under 500 ms | 299 ms | `LargeHistoryTests.Opening_and_reading_10000_clips_with_100_images_takes_under_500_ms` |
| Copy text from a 1000 × 100 region, every profile language (English and Arabic) and Tesseract | under 1 s | 86 ms (222 ms for the first read, which starts the engines) | `OcrLatencyTests.Reading_a_1000_by_100_region_with_every_profile_language_takes_under_1_s` |

Most of the memory holds the 100 pictures, kept decrypted in the history while the app runs. A history of text only needs much less.

## What Phase 9 changed

The first run measured startup at 1.3 s with 10,000 clips. Adding the loaded clips to the history one at a time copied and searched the whole list for every clip, which cost 709 ms for 10,000 clips. `ClipHistory.AddRange` now adds them in one pass, in 7 ms. Two property tests show the result is the same as adding them one by one. Startup fell to 646 ms and memory from 351 MB to 283 MB.

## How to measure again

These tests measure time, so they are tagged Integration and do not run in CI. The UI tests send real keys: nobody may type or click while they run.

```bash
dotnet build -c Release
dotnet test --project tests/ClipBench.UiSmokeTests -c Release --filter-trait "Category=Integration" --filter-class "ClipBench.UiSmokeTests.PerformanceSmokeTests" --output Detailed
dotnet test --project tests/ClipBench.Storage.Tests -c Release --filter-trait "Category=Integration" --output Detailed
dotnet test --project tests/ClipBench.Platform.Windows.IntegrationTests -c Release --filter-class "ClipBench.Platform.Windows.IntegrationTests.Screen.OcrLatencyTests" --output Detailed
```

The numbers are printed in the test output. The app also logs `AppReady ms=` and `PopupShown ms=` on every run, so the log of a real install shows the same timings. The log holds only numbers, never content.
