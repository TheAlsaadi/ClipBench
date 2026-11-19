using FsCheck.Xunit;

// Every property in this assembly runs 1,000 cases from one fixed seed, so a run gives the same result every time
// (ADR 0020). To look for new failures, change the seed locally, and keep what it finds as a plain regression test.
[assembly: Properties(MaxTest = 1000, Replay = "(12721900694993151586,7705464558162640201)")]
