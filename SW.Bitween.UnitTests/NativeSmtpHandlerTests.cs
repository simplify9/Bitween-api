using System.Collections.Generic;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
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

    // ─── Server certificate acceptance ──────────────────────────────────────────

    [TestMethod]
    public void Certificate_WithNothingWrong_IsAccepted()
    {
        Assert.IsTrue(NativeSmtpHandler.IsCertificateAcceptable(
            SslPolicyErrors.None, new[] { X509ChainStatusFlags.NoError }));
    }

    [TestMethod]
    public void Certificate_WhoseRevocationCouldNotBeChecked_IsAccepted()
    {
        // The whole point of the soft-fail: the CA's OCSP or CRL server was unreachable, which says
        // nothing bad about the certificate itself.
        Assert.IsTrue(NativeSmtpHandler.IsCertificateAcceptable(
            SslPolicyErrors.RemoteCertificateChainErrors,
            new[] { X509ChainStatusFlags.RevocationStatusUnknown }));

        Assert.IsTrue(NativeSmtpHandler.IsCertificateAcceptable(
            SslPolicyErrors.RemoteCertificateChainErrors,
            new[] { X509ChainStatusFlags.OfflineRevocation }));

        Assert.IsTrue(NativeSmtpHandler.IsCertificateAcceptable(
            SslPolicyErrors.RemoteCertificateChainErrors,
            new[] { X509ChainStatusFlags.RevocationStatusUnknown | X509ChainStatusFlags.OfflineRevocation }));
    }

    [TestMethod]
    public void Certificate_ThatWasRevoked_IsRefused()
    {
        Assert.IsFalse(NativeSmtpHandler.IsCertificateAcceptable(
            SslPolicyErrors.RemoteCertificateChainErrors,
            new[] { X509ChainStatusFlags.Revoked }));
    }

    [TestMethod]
    public void Certificate_RevokedAlongsideAnUncheckableStatus_IsRefused()
    {
        // One chain entry can carry several flags at once, so the tolerated ones have to be masked
        // out rather than compared — otherwise a revoked certificate rides in on the same entry.
        Assert.IsFalse(NativeSmtpHandler.IsCertificateAcceptable(
            SslPolicyErrors.RemoteCertificateChainErrors,
            new[] { X509ChainStatusFlags.Revoked | X509ChainStatusFlags.RevocationStatusUnknown }));

        Assert.IsFalse(NativeSmtpHandler.IsCertificateAcceptable(
            SslPolicyErrors.RemoteCertificateChainErrors,
            new[] { X509ChainStatusFlags.RevocationStatusUnknown, X509ChainStatusFlags.Revoked }));
    }

    [TestMethod]
    public void Certificate_WithAnyOtherDefect_IsRefused()
    {
        Assert.IsFalse(NativeSmtpHandler.IsCertificateAcceptable(
            SslPolicyErrors.RemoteCertificateChainErrors,
            new[] { X509ChainStatusFlags.UntrustedRoot }));

        Assert.IsFalse(NativeSmtpHandler.IsCertificateAcceptable(
            SslPolicyErrors.RemoteCertificateChainErrors,
            new[] { X509ChainStatusFlags.NotTimeValid }));

        // A wrong hostname or no certificate at all is not a chain question, so the chain flags must
        // not be allowed to excuse it.
        Assert.IsFalse(NativeSmtpHandler.IsCertificateAcceptable(
            SslPolicyErrors.RemoteCertificateNameMismatch,
            new[] { X509ChainStatusFlags.NoError }));

        Assert.IsFalse(NativeSmtpHandler.IsCertificateAcceptable(
            SslPolicyErrors.RemoteCertificateNotAvailable,
            new[] { X509ChainStatusFlags.NoError }));

        Assert.IsFalse(NativeSmtpHandler.IsCertificateAcceptable(
            SslPolicyErrors.RemoteCertificateChainErrors | SslPolicyErrors.RemoteCertificateNameMismatch,
            new[] { X509ChainStatusFlags.RevocationStatusUnknown }));
    }
}
