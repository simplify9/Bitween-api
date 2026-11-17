using System;

namespace SW.Bitween.Domain.Accounts;

/// <summary>
/// A sign-in code that has been redeemed. Codes are signed rather than stored, so this is what makes
/// each one work once: a second redemption finds its nonce here and is refused. Kept only as long
/// as the code could still be redeemed.
/// </summary>
public class SignInCodeUse
{
    public SignInCodeUse(string nonce, DateTime expiresOn)
    {
        Nonce = nonce;
        ExpiresOn = expiresOn;
    }

    public string Nonce { get; private set; }

    public DateTime ExpiresOn { get; private set; }
}
