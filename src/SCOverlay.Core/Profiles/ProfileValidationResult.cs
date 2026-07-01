namespace SCOverlay.Core.Profiles;

public sealed record ProfileValidationResult(IReadOnlyList<ProfileValidationIssue> Issues)
{
    public bool IsValid => Issues.Count == 0;

    public static ProfileValidationResult Valid { get; } = new(Array.Empty<ProfileValidationIssue>());
}
