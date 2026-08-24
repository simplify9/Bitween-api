using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SW.Bitween.Model;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.RetryPolicies;

/// <summary>
/// Dry-runs a (possibly unsaved) set of retry groups against a single simulated failure,
/// so the management UI can show "will this retry, and when" before saving.
/// </summary>
[HandlerName("test")]
public class Test : ICommandHandler<TestRetryPolicyRequest, object>
{
    private readonly BitweenDbContext _dbContext;
    private readonly RequestContext _requestContext;

    public Test(BitweenDbContext dbContext, RequestContext requestContext)
    {
        _dbContext = dbContext;
        _requestContext = requestContext;
    }

    public async Task<object> Handle(TestRetryPolicyRequest request)
    {
        // A pure simulation with no side effects, so viewing a policy is enough to dry-run one.
        await _requestContext.EnsurePermission(_dbContext, Model.Permissions.RetryPolicies.View);

        if (request.ResultType == XchangeResultType.Success)
            throw new SWValidationException("INVALID_RESULT_TYPE",
                "Choose Error or Bad result — a successful result is never retried.");

        var policy = new CustomRetryPolicy { Groups = request.Groups ?? [] };
        // In-memory budget: a dry-run must not spend any real integration's total.
        var evaluator = new RetryPolicyEvaluator(policy, new InMemoryRetryGroupBudget());
        var attemptsToSimulate = Math.Clamp(request.AttemptsToSimulate, 1, 20);

        var attempts = new List<TestRetryAttemptResult>();
        for (var attemptIndex = 0; attemptIndex < attemptsToSimulate; attemptIndex++)
        {
            var decision = await evaluator.Evaluate(request.ResultType, request.Content, attemptIndex);

            attempts.Add(new TestRetryAttemptResult
            {
                AttemptNumber = attemptIndex + 1,
                MatchedGroupName = decision.MatchedGroupName,
                ShouldRetry = decision.ShouldRetry,
                DelaySeconds = decision.ShouldRetry ? decision.Delay.TotalSeconds : null,
                Reason = decision.Reason
            });

            // Once blocked, every later attempt for this same message would be blocked
            // for the same reason (budgets never un-consume, and a Block action or "no
            // match" is structural) — no value in simulating further.
            if (!decision.ShouldRetry) break;
        }

        return new TestRetryPolicyResponse { Attempts = attempts };
    }
}
