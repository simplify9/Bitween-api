using System.Text;
using DotLiquid;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SW.PrimitiveTypes;

namespace SW.Bitween.NativeAdapters;

/// <summary>
/// The "Login" auth type, shared by the HTTP handler and receiver: post credentials to a login URL
/// and read a bearer token out of the reply. APIs differ in both the body they expect and where
/// they put the token, so each can be set per adapter; left blank, the old fixed shapes apply.
/// </summary>
internal static class HttpLogin
{
    public static async Task<string> GetToken(HttpClient client, string loginUrl, string? loginBody,
        string? tokenPath, string? username, string? password, object defaultBody)
    {
        var body = string.IsNullOrWhiteSpace(loginBody)
            ? JsonConvert.SerializeObject(defaultBody)
            : RenderBody(loginBody, username, password);

        var response = await client.PostAsync(new Uri(loginUrl),
            new StringContent(body, Encoding.UTF8, "application/json"));
        var responseBody = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
            throw new SWException(
                $"Login to {loginUrl} failed with {(int)response.StatusCode} {response.StatusCode}: {responseBody}");

        return ReadToken(responseBody, tokenPath);
    }

    internal static string RenderBody(string template, string? username, string? password) =>
        Template.Parse(template).Render(Hash.FromDictionary(new Dictionary<string, object>
        {
            ["username"] = JsonEscape(username),
            ["password"] = JsonEscape(password)
        }));

    // The template is JSON, so a quote or backslash in a credential would otherwise break the body.
    private static string JsonEscape(string? value) => JsonConvert.ToString(value ?? string.Empty)[1..^1];

    internal static string ReadToken(string responseBody, string? tokenPath)
    {
        JToken json;
        try
        {
            json = JToken.Parse(responseBody);
        }
        catch (JsonReaderException)
        {
            throw new SWException($"The login response is not JSON: {responseBody}");
        }

        if (string.IsNullOrWhiteSpace(tokenPath))
        {
            // Matched case-insensitively, as the old fixed deserialisation did.
            var jwt = json is JObject obj
                ? obj.GetValue("Jwt", StringComparison.OrdinalIgnoreCase)?.ToString()
                : null;
            return !string.IsNullOrEmpty(jwt)
                ? jwt
                : throw new SWException(
                    "The login response has no 'jwt' field. Set LoginTokenPath to where the token is.");
        }

        var path = tokenPath.Trim();
        return json.SelectToken(path) is JValue { Type: JTokenType.String } token && !string.IsNullOrEmpty((string?)token)
            ? (string)token!
            : throw new SWException($"The login response has no token at '{path}'.");
    }
}
