using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SW.Bitween.Resources.XchangeExplanations;
using SW.Bitween.Services.Ai;

namespace SW.Bitween.UnitTests.Ai;

/// <summary>
/// The retry chain as an explanation sends it: numbered from the chain's real first attempt, each
/// error masked whole before it is cut, so half a value never goes out.
/// </summary>
[TestClass]
public class ExplainChainTests
{
    static readonly DateTime At = new(2026, 11, 3, 9, 0, 0);

    static Explain.Attempt Failed(string id, int number, string error) => new(id, number, At, false, false, error);

    [TestMethod]
    public void A_long_chain_is_numbered_from_its_real_first_attempt_and_says_what_it_left_out()
    {
        var chain = new Explain.RetryChain(
            [Failed("a", 41, "Timeout"), Failed("b", 42, "Timeout"), new("c", 43, At, true, false, null)],
            Position: 42, Total: 43, More: false, EarlierNotListed: 40);

        var lines = Explain.ChainLines(chain, "b", new Masking()).ToList();

        CollectionAssert.AreEqual(new[]
        {
            "(40 earlier attempts not listed)",
            "Attempt 41, from 2026-11-03 09:00 UTC: failed: Timeout",
            "Attempt 42 (this one), from 2026-11-03 09:00 UTC: failed: Timeout",
            "Attempt 43, from 2026-11-03 09:00 UTC: succeeded",
        }, lines);
    }

    [TestMethod]
    public void An_error_is_masked_whole_before_it_is_cut()
    {
        // A first line longer than what is read, with a quoted name right where the read stops.
        var read = new string('x', Explain.ErrorRead - 20) + " value 'Lina Haddad' was rejected";
        var error = read[..Explain.ErrorRead];
        var chain = new Explain.RetryChain([Failed("a", 1, error), Failed("b", 2, "Timeout")], 2, 2, false, 0);

        var line = Explain.ChainLines(chain, "b", new Masking()).First();

        Assert.IsFalse(line.Contains("lina"), line);
        Assert.IsTrue(line.Length < Explain.MaxLineLength + 100, $"{line.Length} characters");
    }

    [TestMethod]
    public void A_first_line_read_only_in_part_loses_its_last_word()
    {
        var text = "Refused for customer " + new string('y', Explain.ErrorRead);
        Assert.AreEqual("Refused for customer ...", Explain.WholeFirstLine(text[..Explain.ErrorRead]));
        var quoted = "Refused for 'Lina " + new string('y', Explain.ErrorRead);
        Assert.AreEqual("Refused for ...", Explain.WholeFirstLine(quoted[..Explain.ErrorRead]));
        Assert.AreEqual("Timeout", Explain.WholeFirstLine("Timeout\n   at Foo()"));
    }
}
