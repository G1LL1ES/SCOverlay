namespace SCOverlay.Core.Profiles;

public sealed class ProfileValidationException : Exception
{
    public ProfileValidationException(IReadOnlyList<ProfileValidationIssue> issues)
        : base(string.Join(Environment.NewLine, issues.Select(issue => $"{issue.Path}: {issue.Message}")))
    {
        Issues = issues;
    }

    public IReadOnlyList<ProfileValidationIssue> Issues { get; }
}
