using ClipBench.Core.Results;

namespace ClipBench.Core.Transforms;

/// <summary>Runs a chain of transforms over text, in the order given.</summary>
public sealed class TransformPipeline
{
    private readonly int _maxCharacters;

    /// <summary>Creates a pipeline.</summary>
    /// <param name="maxCharacters">The most characters the text may have, going in and coming out of each step.</param>
    public TransformPipeline(int maxCharacters = Pasting.PasteComposer.DefaultMaxOutputCharacters)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxCharacters, 1);
        _maxCharacters = maxCharacters;
    }

    /// <summary>Applies each transform to the result of the one before. It stops at the first failure.</summary>
    /// <param name="text">The text to change.</param>
    /// <param name="chain">The transforms, in order. An empty chain returns the text as it is.</param>
    /// <returns>The changed text, or the first failure with the name of the transform that failed.</returns>
    public Result<string> Run(string text, IReadOnlyList<TextTransform> chain)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(chain);

        if (text.Length > _maxCharacters)
        {
            return TooLarge("The text");
        }

        var current = text;
        foreach (var transform in chain)
        {
            var step = transform.Apply(current);
            if (step.IsFailure)
            {
                return Result.Failure<string>(new ResultError(step.Error.Code, $"{transform.Name}: {step.Error.Message}"));
            }

            if (step.Value.Length > _maxCharacters)
            {
                return TooLarge(transform.Name);
            }

            current = step.Value;
        }

        return Result.Success(current);
    }

    private static Result<string> TooLarge(string what) =>
        Result.Failure<string>(new ResultError(ErrorCode.OutputTooLarge, $"{what}: the text is too large to change."));
}
