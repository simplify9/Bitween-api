namespace SW.Bitween.NativeAdapters;

public class UserLoginModel
{
    public string? Email { get; set; }
    public string? Password { get; set; }
}

public class LoginResponse
{
    public string? Jwt { get; set; }
    public string? Refresh { get; set; }
}

public class OAuth2Response
{
    public string? access_token { get; set; }
}
