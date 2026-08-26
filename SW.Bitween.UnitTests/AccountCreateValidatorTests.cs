using System.Linq;
using System.Reflection;
using FluentValidation;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SW.Bitween.Model;

namespace SW.Bitween.UnitTests;

/// <summary>
/// The rules that decide whether a request to add a team member is accepted at all.
/// </summary>
/// <remarks>
/// These validators are nested private classes discovered by the request pipeline, so a test that
/// calls the handler directly never runs them — which is exactly how the handler and its validator
/// came to disagree. The handler was updated to take explicit role ids and treat the coarse
/// <c>Role</c> as legacy, but the validator went on demanding <c>Role</c> unconditionally, so the
/// payload the UI actually sends was rejected and adding a member failed outright.
/// </remarks>
[TestClass]
public class AccountCreateValidatorTests
{
    /// <summary>
    /// Builds the handler's nested validator. Reflection because it is private by design — the
    /// pipeline finds it by scanning, and nothing else is meant to construct one.
    /// </summary>
    private static IValidator<CreateAccountModel> Validator(bool disableEmailPasswordLogin = false)
    {
        var type = typeof(Resources.Accounts.Create)
            .GetNestedType("Validate", BindingFlags.NonPublic)!;
        return (IValidator<CreateAccountModel>)System.Activator.CreateInstance(
            type, new BitweenOptions { DisableEmailPasswordLogin = disableEmailPasswordLogin })!;
    }

    private static CreateAccountModel Valid() => new()
    {
        Name = "New Member",
        Email = "new-member@test.local",
        Password = "A-Strong-Passw0rd!",
    };

    private static string[] FailedFields(CreateAccountModel model) =>
        Validator().Validate(model).Errors.Select(e => e.PropertyName).ToArray();

    [TestMethod]
    public void Explicit_role_ids_alone_are_accepted()
    {
        var model = Valid();
        model.RoleIds = [Domain.Accounts.Role.MemberId];

        // Exactly what the UI posts. Demanding the legacy Role here is what broke adding a member.
        Assert.IsTrue(Validator().Validate(model).IsValid);
    }

    [TestMethod]
    public void The_legacy_coarse_role_alone_is_still_accepted()
    {
        var model = Valid();
        model.Role = (int)Domain.Accounts.AccountRole.Member;

        // Older callers send only this, and they must keep working.
        Assert.IsTrue(Validator().Validate(model).IsValid);
    }

    [TestMethod]
    public void Naming_no_role_at_all_is_refused()
    {
        // The rule the original NotNull was really protecting. When Role was a plain int, omitting
        // it sent 0 — which is Admin — so a member added with nothing ticked got the run of the
        // instance. Neither field given has to stay a refusal.
        CollectionAssert.Contains(FailedFields(Valid()), nameof(CreateAccountModel.Role));
    }

    [TestMethod]
    public void An_empty_role_id_list_counts_as_naming_no_role()
    {
        var model = Valid();
        model.RoleIds = [];

        // An empty list is not an answer — it is the absence of one, and must not slip past the
        // guard just because the property was present.
        CollectionAssert.Contains(FailedFields(model), nameof(CreateAccountModel.Role));
    }

    [TestMethod]
    public void A_member_still_needs_a_name_an_email_and_a_password()
    {
        var failed = FailedFields(new CreateAccountModel { RoleIds = [Domain.Accounts.Role.MemberId] });

        CollectionAssert.Contains(failed, nameof(CreateAccountModel.Name));
        CollectionAssert.Contains(failed, nameof(CreateAccountModel.Email));
        CollectionAssert.Contains(failed, nameof(CreateAccountModel.Password));
    }

    [TestMethod]
    public void No_password_is_required_when_the_instance_signs_in_through_Microsoft_only()
    {
        var model = Valid();
        model.Password = null;
        model.RoleIds = [Domain.Accounts.Role.MemberId];

        // The account exists purely to be matched by email, so there is no password to demand.
        Assert.IsTrue(Validator(disableEmailPasswordLogin: true).Validate(model).IsValid);
    }
}
