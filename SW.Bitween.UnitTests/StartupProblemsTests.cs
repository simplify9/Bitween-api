using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SW.Bitween.Services;

namespace SW.Bitween.UnitTests;

[TestClass]
public class StartupProblemsTests
{
    [TestMethod]
    public void Every_problem_is_listed_together()
    {
        var problems = new StartupProblems();
        problems.Check(() => throw new StartupProblemException("Token:Key is not configured."));
        problems.Add("ConnectionStrings:RabbitMQ is not configured.");

        Assert.IsTrue(problems.Any);
        Assert.AreEqual(
            "Bitween can't start until these are fixed:" +
            $"{Environment.NewLine}  1. Token:Key is not configured." +
            $"{Environment.NewLine}  2. ConnectionStrings:RabbitMQ is not configured.",
            problems.ToString());
    }

    [TestMethod]
    public void Other_failures_are_not_mistaken_for_a_problem()
    {
        var problems = new StartupProblems();
        Assert.ThrowsException<InvalidOperationException>(() =>
            problems.Check(() => throw new InvalidOperationException("the database went away")));
        Assert.IsFalse(problems.Any);
    }
}
