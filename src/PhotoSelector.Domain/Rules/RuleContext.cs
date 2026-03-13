using PhotoSelector.Domain.Models;

namespace PhotoSelector.Domain.Rules;

public sealed class RuleContext
{
    public required PhotoItem Photo { get; init; }
}
