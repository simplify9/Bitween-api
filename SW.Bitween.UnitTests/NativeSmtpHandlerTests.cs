using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;
using SW.Bitween.Model;
using SW.Bitween.NativeAdapters;
using SW.Bitween.NativeAdapters.SmtpHandler;

namespace SW.Bitween.UnitTests;

[TestClass]
public class NativeSmtpHandlerTests
{
    // ─── Subject and body templating ────────────────────────────────────────────

    [TestMethod]
    public void Fill_SubstitutesPayloadFields()
    {
        var payload = JsonConvert.SerializeObject(new {GroupName = "timeouts", MaxAttemptsTotal = 8});

        var result = NativeSmtpHandler.Fill("{{ GroupName }} used all {{ MaxAttemptsTotal }} retries", payload);

        Assert.AreEqual("timeouts used all 8 retries", result);
    }

    [TestMethod]
    public void Fill_RendersTheRealAlertPayload()
    {
        // The shape RetryAlertService actually sends, serialised the same way (PascalCase).
        var payload = JsonConvert.SerializeObject(new RetryBudgetExhaustedNotification
        {
            SubscriptionName = "QA - ShipaDelivery - CreateOrder",
            GroupName = "FRT charges cannot be found",
            MaxAttemptsTotal = 8
        });

        var result = NativeSmtpHandler.Fill(
            "Retries stopped for {{ SubscriptionName }}: {{ GroupName }} ({{ MaxAttemptsTotal }})", payload);

        Assert.AreEqual(
            "Retries stopped for QA - ShipaDelivery - CreateOrder: FRT charges cannot be found (8)", result);
    }

    [TestMethod]
    public void Fill_LeavesTemplateAloneForNonJsonPayload()
    {
        // Normal for a pipeline handler shipping a flat file — there are no fields to substitute.
        Assert.AreEqual("Nightly export", NativeSmtpHandler.Fill("Nightly export", "id,name\n1,alpha"));
    }

    [TestMethod]
    public void Fill_LeavesTemplateAloneForEmptyPayload()
    {
        Assert.AreEqual("Nightly export", NativeSmtpHandler.Fill("Nightly export", ""));
    }

    [TestMethod]
    public void Fill_MissingFieldRendersEmptyRatherThanThePlaceholder()
    {
        var result = NativeSmtpHandler.Fill("Group: {{ GroupName }}", "{\"Other\":1}");

        Assert.AreEqual("Group: ", result);
    }

    // ─── Startup values ─────────────────────────────────────────────────────────

    [TestMethod]
    public void StartupValues_ParseNumbersAndFlags()
    {
        var input = new Dictionary<string, string>
        {
            ["Host"] = "smtp.example.com",
            ["Port"] = "465",
            ["UseTls"] = "true",
            ["IsHtml"] = "false",
            ["From"] = "alerts@example.com",
            ["To"] = "ops@example.com"
        }.ConvertTo<SmtpHandlerInput>();

        Assert.AreEqual("smtp.example.com", input.Host);
        Assert.AreEqual(465, input.Port);
        Assert.IsTrue(input.UseTls);
        Assert.IsFalse(input.IsHtml);
    }

    [TestMethod]
    public void StartupValues_KeepDefaultsWhenOmitted()
    {
        var input = new Dictionary<string, string>
        {
            ["Host"] = "smtp.example.com",
            ["From"] = "alerts@example.com",
            ["To"] = "ops@example.com"
        }.ConvertTo<SmtpHandlerInput>();

        // The common provider setup should need no port or TLS choice at all.
        Assert.AreEqual(587, input.Port);
        Assert.IsTrue(input.UseTls);
        Assert.IsTrue(input.IsHtml);
        Assert.IsNull(input.Password);
    }
}
