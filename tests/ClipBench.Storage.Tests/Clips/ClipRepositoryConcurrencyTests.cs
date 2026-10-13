using ClipBench.Core.Results;
using ClipBench.Storage.Tests.Support;

namespace ClipBench.Storage.Tests.Clips;

public sealed class ClipRepositoryConcurrencyTests : RepositoryTestBase
{
    [Fact]
    public async Task Many_writes_at_once_on_one_repository_all_succeed_and_none_are_lost()
    {
        var clips = Enumerable.Range(0, 120).Select(i => TestClips.Text($"clip {i}", FakeTimeProvider.Start.AddSeconds(i))).ToArray();
        await using var repository = await OpenAsync();

        var results = await Task.WhenAll(clips.Select(clip => Task.Run(() => repository.SaveAsync(clip, Ct), Ct)));

        results.ShouldAllBe(result => result.IsSuccess);
        (await repository.LoadAllAsync(Ct)).Value.Items.Length.ShouldBe(120);
    }

    [Fact]
    public async Task Mixed_saves_pins_and_deletes_at_once_leave_a_consistent_result()
    {
        var clips = Enumerable.Range(0, 60).Select(i => TestClips.Text($"clip {i}", FakeTimeProvider.Start.AddSeconds(i))).ToArray();
        await using var repository = await OpenAsync();
        await SaveAll(repository, clips);

        var work = new List<Task>();
        foreach (var clip in clips.Take(20))
        {
            work.Add(Task.Run(() => repository.SetPinnedAsync(clip.Id, true, Ct), Ct));
        }

        foreach (var clip in clips.Skip(20).Take(20))
        {
            work.Add(Task.Run(() => repository.DeleteAsync([clip.Id], Ct), Ct));
        }

        foreach (var index in Enumerable.Range(100, 20))
        {
            work.Add(Task.Run(() => repository.SaveAsync(TestClips.Text($"new {index}"), Ct), Ct));
        }

        await Task.WhenAll(work);

        var loaded = (await repository.LoadAllAsync(Ct)).Value;
        loaded.UnreadableCount.ShouldBe(0);
        loaded.Items.Length.ShouldBe(60);
        loaded.Items.Count(clip => clip.IsPinned).ShouldBe(20);
    }

    [Fact]
    public async Task Two_repositories_on_the_same_file_can_write_at_once()
    {
        await using var first = await OpenAsync();
        await using var second = await OpenAsync();

        var firstWrites = Enumerable.Range(0, 50).Select(i => Task.Run(() => first.SaveAsync(TestClips.Text($"first {i}"), Ct), Ct));
        var secondWrites = Enumerable.Range(0, 50).Select(i => Task.Run(() => second.SaveAsync(TestClips.Text($"second {i}"), Ct), Ct));
        var results = await Task.WhenAll(firstWrites.Concat(secondWrites));

        results.ShouldAllBe(result => result.IsSuccess);
        (await first.LoadAllAsync(Ct)).Value.Items.Length.ShouldBe(100);
        (await second.LoadAllAsync(Ct)).Value.Items.Length.ShouldBe(100);
    }

    [Fact]
    public async Task A_second_repository_sees_what_the_first_one_saved()
    {
        var clip = TestClips.Text("shared");
        await using var first = await OpenAsync();
        await using var second = await OpenAsync();

        await SaveAll(first, clip);

        (await second.GetAsync(clip.Id, Ct)).Value.ShouldBe(clip);
    }

    [Fact]
    public async Task Disposing_while_work_is_running_waits_for_it_and_loses_nothing()
    {
        var repository = await OpenAsync();
        var saves = Enumerable.Range(0, 30)
            .Select(i => Task.Run(() => repository.SaveAsync(TestClips.Text($"clip {i}"), Ct), Ct))
            .ToArray();

        await Task.Delay(1, Ct);
        await repository.DisposeAsync();
        var outcomes = await Task.WhenAll(saves.Select(Settle));

        outcomes.ShouldAllBe(outcome => outcome == "saved" || outcome == "refused");
        RawCount("SELECT COUNT(*) FROM clips").ShouldBe(outcomes.Count(outcome => outcome == "saved"));
    }

    // A save either completes or is refused with ObjectDisposedException, never half done.
    private static async Task<string> Settle(Task<Result> save)
    {
        try
        {
            return (await save).IsSuccess ? "saved" : "failed";
        }
        catch (ObjectDisposedException)
        {
            return "refused";
        }
    }
}
