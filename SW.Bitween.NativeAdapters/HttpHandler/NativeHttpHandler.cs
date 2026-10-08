using System.Net;
using System.Net.Http.Headers;
using System.Text;
using DotLiquid;
using Newtonsoft.Json;
using SW.PrimitiveTypes;

namespace SW.Bitween.NativeAdapters;

public class NativeHttpHandler(IDynamicHttpProxy httpProxy) : INativeInfolinkHandler
{
    private HttpMethod HttpMethodFromString(string method)
    {
        switch (method.ToLower())
        {
            case "get":
                return HttpMethod.Get;
            case "delete":
                return HttpMethod.Delete;
            case "put":
                return HttpMethod.Put;
            default:
                return HttpMethod.Post;
        }
    }

    private HttpHandlerInput _options = new();



    public async Task<XchangeFile> Handle(XchangeFile xchangeFile)
    {
        HttpClient client = httpProxy.GetClient(_options.Url);

        // Credentials go on this request, never on the client: the proxy shares one client per
        // origin across every subscription that calls it, so a default header set here was sent on
        // the next subscription's call too — with its credentials, or with none of its own.
        string? apiKey = null;
        AuthenticationHeaderValue? authorization = null;
        if (_options.AuthType == "ApiKey")
            apiKey = _options.ApiKey;
        else if (_options.AuthType == "Bearer")
            authorization = new AuthenticationHeaderValue("Bearer", _options.LoginPassword);
        else if (_options.AuthType == "Basic")
        {
            string credentials =
                Convert.ToBase64String(
                    Encoding.ASCII.GetBytes(_options.LoginUsername + ":" + _options.LoginPassword));
            authorization = new AuthenticationHeaderValue("Basic", credentials);
        }
        else if (_options.AuthType == "Login")
        {
            string token = await HttpLogin.GetToken(client, _options.LoginUrl!, _options.LoginBody,
                _options.LoginTokenPath, _options.LoginUsername, _options.LoginPassword,
                new UserLoginModel()
                {
                    Email = _options.LoginUsername,
                    Password = _options.LoginPassword
                });
            authorization = new AuthenticationHeaderValue("Bearer", token);
        }
        else if (_options.AuthType == "OAuth2")
        {
            var oathRequest = new HttpRequestMessage(HttpMethod.Post, _options.LoginUrl);
            var oauthContentDictionary = new List<KeyValuePair<string, string>>();
            oauthContentDictionary.Add(new("client_id", _options.ClientId!));
            oauthContentDictionary.Add(new("client_secret", _options.ClientSecret!));
            oauthContentDictionary.Add(new("grant_type", "client_credentials"));
            var oauthContent = new FormUrlEncodedContent(oauthContentDictionary);
            oathRequest.Content = oauthContent;
            var oauthResponse = await client.SendAsync(oathRequest);
            var res = await oauthResponse.Content.ReadAsStringAsync();
            var resDeserialized = JsonConvert.DeserializeObject<OAuth2Response>(res);
            authorization = new AuthenticationHeaderValue("Bearer", resDeserialized?.access_token);
        }

        string requestBody = xchangeFile.Data;
        if (string.IsNullOrEmpty(requestBody))
            requestBody = _options.DefaultRequest ?? string.Empty;
        string str = _options.ContentType.ToLower();
        HttpContent content;
        MultipartFormDataContent multipartTmp;
        byte[] fileContent;
        switch (str)
        {
            case "application/x-www-form-urlencoded":
                content = new FormUrlEncodedContent(
                    JsonConvert.DeserializeObject<Dictionary<string, string>>(requestBody)
                    ?? new Dictionary<string, string>());
                break;
            case "multipart/form-data":
                multipartTmp = new MultipartFormDataContent();
                fileContent = Encoding.UTF8.GetBytes(requestBody);
                multipartTmp.Add(new ByteArrayContent(fileContent), "file", xchangeFile.Filename ?? "file");
                content = multipartTmp;
                break;
            case "application/json":
                content = new StringContent(requestBody, Encoding.UTF8, "application/json");
                break;
            default:
                content = new StringContent(requestBody, Encoding.UTF8, _options.ContentType);
                break;
        }

        Uri uri;
        if (!string.IsNullOrEmpty(xchangeFile.Data) && _options.Url.Contains("{{"))
        {
            Template parsedTemplate = Template.Parse(_options.Url);
            IDictionary<string, object> obj =
                JsonConvert.DeserializeObject<IDictionary<string, object>>(xchangeFile.Data,
                    new DictionaryConverter()) ?? new Dictionary<string, object>();
            Hash jsonHash = Hash.FromDictionary(obj);
            uri = new Uri(parsedTemplate.Render(jsonHash));
            EnsureSameOrigin(_options.Url, uri);
        }
        else
            uri = new Uri(_options.Url);

        var httpMethod = HttpMethodFromString(_options.Verb);
        HttpRequestMessage request = new HttpRequestMessage()
        {
            RequestUri = uri,
            Method = httpMethod,
            Content = httpMethod == HttpMethod.Get ? null : content
        };
        string? headers1 = _options.Headers;
        IEnumerable<KeyValuePair<string, string>>? headers = headers1 != null
            ? (headers1.Split(',')).Select((Func<string, KeyValuePair<string, string>>)(h =>
            {
                string[] strArray = h.Split(':');
                return new KeyValuePair<string, string>(strArray[0], strArray[1]);
            }))
            : null;
        if (headers != null)
        {
            foreach (KeyValuePair<string, string> keyValuePair1 in headers)
            {
                KeyValuePair<string, string> keyValuePair = keyValuePair1;
                request.Headers.Add(keyValuePair.Key, keyValuePair.Value);
            }
        }

        if (authorization is not null)
            request.Headers.Authorization = authorization;
        if (apiKey is not null)
            request.Headers.Add("ApiKey", apiKey);

        if (!string.IsNullOrEmpty(_options.CorrelationId))
            request.Headers.Add("request-context-correlation-id", _options.CorrelationId);
        HttpResponseMessage response = await client.SendAsync(request);
        string resp = await response.Content.ReadAsStringAsync();
        if (response.StatusCode < HttpStatusCode.OK || response.StatusCode >= HttpStatusCode.InternalServerError)
            throw new Exception($"{response.StatusCode}: {resp}");
        XchangeFile xchangeFile1 = response.StatusCode < HttpStatusCode.BadRequest
            ? new XchangeFile(resp)
            : new XchangeFile(resp, badData: true);
        return xchangeFile1;
    }


    /// <summary>
    /// Refuses a rendered URL that went somewhere the template does not allow. The values come
    /// from the message, so without this a payload such as <c>"path": ".attacker.example/x"</c>
    /// could send the request — and its credentials — to a host of its choosing.
    /// </summary>
    /// <remarks>
    /// The scheme must be the template's. A placeholder inside the host may fill in one DNS label
    /// and no more — no dots, no slashes — so <c>https://{{tenant}}.api.example</c> still reaches
    /// any tenant of api.example and nothing else. The port must be the template's unless the
    /// template leaves it to a placeholder, and the message may not add a user name.
    /// </remarks>
    internal static void EnsureSameOrigin(string template, Uri rendered)
    {
        var schemeEnd = template.IndexOf("://", StringComparison.Ordinal);
        if (schemeEnd < 0 || template[..schemeEnd].Contains("{{")) return;

        var authority = AuthorityOf(template, schemeEnd + 3);
        if (!authority.Contains("{{"))
        {
            // Nothing in the message can reach the host when the template spells it out.
            return;
        }

        var at = LastIndexOutsidePlaceholders(authority, '@');
        var hostAndPort = at < 0 ? authority : authority[(at + 1)..];

        string hostTemplate = hostAndPort, portTemplate = null;
        var colon = LastIndexOutsidePlaceholders(hostAndPort, ':');
        if (colon >= 0 && !hostAndPort.StartsWith('['))
        {
            hostTemplate = hostAndPort[..colon];
            portTemplate = hostAndPort[(colon + 1)..];
        }

        var pattern = "^" + string.Join("[a-z0-9-]*",
            System.Text.RegularExpressions.Regex.Split(hostTemplate.ToLowerInvariant(), @"\{\{.*?\}\}")
                .Select(System.Text.RegularExpressions.Regex.Escape)) + "$";

        var allowed =
            string.Equals(template[..schemeEnd], rendered.Scheme, StringComparison.OrdinalIgnoreCase) &&
            System.Text.RegularExpressions.Regex.IsMatch(rendered.IdnHost.ToLowerInvariant(), pattern) &&
            (portTemplate is null
                ? rendered.IsDefaultPort
                : portTemplate.Contains("{{") || portTemplate == rendered.Port.ToString()) &&
            (at >= 0 || string.IsNullOrEmpty(rendered.UserInfo));

        if (!allowed)
            throw new SWException(
                $"The message's values changed where this request goes: the URL template allows {authority} " +
                $"but rendered to {rendered.GetLeftPart(UriPartial.Authority)}.");
    }

    /// <summary>The authority of a URL template, read past placeholders so a '/' inside one doesn't end it.</summary>
    private static string AuthorityOf(string template, int start)
    {
        var i = start;
        while (i < template.Length)
        {
            if (template.AsSpan(i).StartsWith("{{"))
            {
                var close = template.IndexOf("}}", i + 2, StringComparison.Ordinal);
                i = close < 0 ? template.Length : close + 2;
                continue;
            }
            if (template[i] is '/' or '?' or '#') break;
            i++;
        }
        return template[start..i];
    }

    private static int LastIndexOutsidePlaceholders(string value, char c)
    {
        var found = -1;
        var depth = false;
        for (var i = 0; i < value.Length; i++)
        {
            if (!depth && value.AsSpan(i).StartsWith("{{")) { depth = true; i++; continue; }
            if (depth && value.AsSpan(i).StartsWith("}}")) { depth = false; i++; continue; }
            if (!depth && value[i] == c) found = i;
        }
        return found;
    }

    public string Name => "NativeHttpHandler";
    public void InitializeStartupValues(IDictionary<string, string> settings)
    {
        _options = settings.ConvertTo<HttpHandlerInput>();
    }

    public Type StartupValuesType => typeof(HttpHandlerInput);
}