namespace ClipBench.Presentation.Transforms;

/// <summary>One line of the transform picker.</summary>
/// <param name="Id">The stable identifier of the transform.</param>
/// <param name="Name">What the picker shows.</param>
/// <param name="Description">One line that says what the transform does.</param>
/// <param name="NeedsInput">True when the transform asks the user for something first, as find and replace does.</param>
public sealed record TransformItemViewModel(string Id, string Name, string Description, bool NeedsInput = false);
