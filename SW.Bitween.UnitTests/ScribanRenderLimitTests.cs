using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SW.Bitween.NativeAdapters.JsonMapper;

namespace SW.Bitween.UnitTests;

/// <summary>
/// The preview renders whatever template the caller sends, inside the API process. Scriban caps
/// each loop statement's iterations itself; the time limit covers whatever runs long anyway.
/// </summary>
[TestClass]
public class ScribanRenderLimitTests
{
    [TestMethod]
    public void A_render_past_its_time_limit_is_stopped()
    {
        var saved = ScribanJsonHelper.RenderTimeout;
        ScribanJsonHelper.RenderTimeout = TimeSpan.Zero;
        try
        {
            var ex = Assert.ThrowsException<InvalidOperationException>(() =>
                ScribanJsonHelper.Render("{{ for i in 1..500 }}{{ i }}{{ end }}", "{}"));

            StringAssert.Contains(ex.Message, "stopped");
        }
        finally
        {
            ScribanJsonHelper.RenderTimeout = saved;
        }
    }

    [TestMethod]
    public void Nested_loops_are_capped_by_Scriban_itself() =>
        Assert.ThrowsException<Scriban.Syntax.ScriptRuntimeException>(() => ScribanJsonHelper.Render(
            "{{ for a in 1..999 }}{{ for b in 1..999 }}{{ b }}{{ end }}{{ end }}", "{}"));
}
