namespace SW.Bitween.Domain.Accounts;

/// <summary>Who an account is: a person who signs in, or a program that holds service tokens.</summary>
public enum AccountKind
{
    Person = 0,

    /// <summary>
    /// Signs in only by trading one of its <see cref="ServiceToken"/>s; never with a password,
    /// Microsoft or the CLI's browser sign-in, and never into the admin UI.
    /// </summary>
    Service = 1
}
