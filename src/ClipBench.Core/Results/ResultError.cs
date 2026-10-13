namespace ClipBench.Core.Results;

/// <summary>An expected failure: a stable code for callers and a message for people.</summary>
/// <param name="Code">What went wrong, for code to act on.</param>
/// <param name="Message">A short explanation for the user. It never contains clip content.</param>
public readonly record struct ResultError(ErrorCode Code, string Message);
