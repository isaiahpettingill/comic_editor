namespace ComicEditor.Format;

/// <summary>Case-sensitive, single-variable conditions, with preprocessor-style defined/undefined semantics.</summary>
public static class FramePlayback
{
    public const int DefaultDurationMs = 1000;
    public const int MaximumDurationMs = 3_600_000;

    public static bool IsVariable(string value) => value.Length is > 0 and <= 64 &&
        (char.IsAsciiLetter(value[0]) || value[0] == '_') &&
        value.All(c => char.IsAsciiLetterOrDigit(c) || c == '_') && value is not ("always" or "never" or "not");

    public static string? Variable(string requirement) => requirement is "always" or "never" ? null :
        requirement.StartsWith("not ", StringComparison.Ordinal) ? requirement[4..] : requirement;

    public static bool IsRequirement(string requirement) => requirement is "always" or "never" ||
        requirement is not null && IsVariable(Variable(requirement)!);

    public static bool Matches(string requirement, ISet<string> variables)
    {
        if (!IsRequirement(requirement)) throw new InvalidDataException("Invalid frame requirement.");
        return requirement switch
        {
            "always" => true,
            "never" => false,
            _ when requirement.StartsWith("not ", StringComparison.Ordinal) => !variables.Contains(requirement[4..]),
            _ => variables.Contains(requirement)
        };
    }

    public static string[] Variables(Cutscene scene) => scene.Frames.Select(f => Variable(f.Requirement))
        .OfType<string>().Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();

    public static int[] SelectFrames(Cutscene scene, IEnumerable<string> variables)
    {
        var defined = variables.ToHashSet(StringComparer.Ordinal);
        return scene.Frames.Select((frame, index) => (frame, index))
            .Where(item => Matches(item.frame.Requirement, defined)).Select(item => item.index).ToArray();
    }
}
