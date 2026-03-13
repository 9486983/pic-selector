using PhotoSelector.Application.Interfaces;
using PhotoSelector.Domain.Rules;

namespace PhotoSelector.Application.Services;

public sealed class RuleEngineService : IRuleEngine
{
    public RuleEvaluationResult Evaluate(string expression, RuleContext context, string name = "CustomRule")
    {
        var normalized = expression.Replace(" ", string.Empty, StringComparison.OrdinalIgnoreCase);
        var analysis = context.Photo.Analysis;

        // Simple baseline DSL parser, can be replaced by a full parser later.
        var passed = true;
        if (normalized.Contains("sharpness>"))
        {
            var threshold = ParseNumberAfter(normalized, "sharpness>");
            passed &= analysis.SharpnessScore > threshold;
        }

        if (normalized.Contains("eyes_closed==false", StringComparison.OrdinalIgnoreCase))
        {
            passed &= !analysis.EyesClosed;
        }

        if (normalized.Contains("duplicate==false", StringComparison.OrdinalIgnoreCase))
        {
            passed &= !analysis.IsDuplicate;
        }

        if (normalized.Contains("overall>"))
        {
            var threshold = ParseNumberAfter(normalized, "overall>");
            passed &= analysis.OverallScore > threshold;
        }

        if (normalized.Contains("facecount>"))
        {
            var threshold = (int)ParseNumberAfter(normalized, "facecount>");
            passed &= analysis.FaceCount > threshold;
        }

        if (normalized.Contains("is_waste==false", StringComparison.OrdinalIgnoreCase))
        {
            passed &= !analysis.IsWaste;
        }

        if (normalized.Contains("style=="))
        {
            var style = ParseStringAfter(normalized, "style==");
            passed &= analysis.StyleLabel.Equals(style, StringComparison.OrdinalIgnoreCase);
        }

        return new RuleEvaluationResult
        {
            RuleName = name,
            Passed = passed,
            Message = passed ? "Pass" : "Filtered"
        };
    }

    private static float ParseNumberAfter(string input, string marker)
    {
        var idx = input.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (idx < 0)
        {
            return 0;
        }

        idx += marker.Length;
        var end = idx;
        while (end < input.Length && (char.IsDigit(input[end]) || input[end] == '.'))
        {
            end++;
        }

        var slice = input[idx..end];
        return float.TryParse(slice, out var val) ? val : 0;
    }

    private static string ParseStringAfter(string input, string marker)
    {
        var idx = input.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (idx < 0)
        {
            return string.Empty;
        }

        idx += marker.Length;
        var end = idx;
        while (end < input.Length && (char.IsLetter(input[end]) || input[end] == '_'))
        {
            end++;
        }

        return input[idx..end];
    }
}
