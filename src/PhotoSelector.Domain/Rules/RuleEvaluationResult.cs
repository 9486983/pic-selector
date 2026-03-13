namespace PhotoSelector.Domain.Rules;

public sealed class RuleEvaluationResult
{
    public string RuleName { get; set; } = string.Empty;
    public bool Passed { get; set; }
    public string Message { get; set; } = string.Empty;
}
