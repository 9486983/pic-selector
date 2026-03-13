using PhotoSelector.Domain.Rules;

namespace PhotoSelector.Application.Interfaces;

public interface IRuleEngine
{
    RuleEvaluationResult Evaluate(string expression, RuleContext context, string name = "CustomRule");
}
