using System.Net;
using System.Net.Http.Headers;
using System.Text;
using DotLiquid;
using Newtonsoft.Json;
using SW.PrimitiveTypes;

namespace SW.Bitween.NativeAdapters;

public class HttpHandler : IInfolinkHandler
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

    private readonly HttpHandlerInput _options;
    
    public HttpHandler(HttpHandlerInput options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public async Task<XchangeFile> Handle(XchangeFile xchangeFile)
    {
        HttpClient client = new HttpClient();
        if (_options.AuthType == "ApiKey")
            client.DefaultRequestHeaders.Add("ApiKey", _options.ApiKey);
        else if (_options.AuthType == "Bearer")
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", _options.LoginPassword);
        else if (_options.AuthType == "Basic")
        {
            string credentials =
                Convert.ToBase64String(
                    Encoding.ASCII.GetBytes(_options.LoginUsername + ":" + _options.LoginPassword));
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        }
        else if (_options.AuthType == "Login")
        {
            string loginJson = JsonConvert.SerializeObject(new UserLoginModel()
            {
                Email = _options.LoginUsername,
                Password = _options.LoginPassword
            });
            HttpResponseMessage loginResponse = await client.PostAsync(new Uri(_options.LoginUrl!),
                new StringContent(loginJson, Encoding.UTF8, "application/json"));
            loginResponse.EnsureSuccessStatusCode();
            if (loginResponse.StatusCode != HttpStatusCode.OK)
                throw new Exception(loginResponse.StatusCode.ToString());
            string rs = await loginResponse.Content.ReadAsStringAsync();
            LoginResponse? rsDeserialized = JsonConvert.DeserializeObject<LoginResponse>(rs);
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", rsDeserialized?.Jwt);
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
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", resDeserialized?.access_token);
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

        if (!string.IsNullOrEmpty(_options.CorrelationId))
            request.Headers.Add("request-context-correlation-id", _options.CorrelationId);
        HttpResponseMessage response = await client.SendAsync(request);
        if (response.StatusCode < HttpStatusCode.OK || response.StatusCode >= HttpStatusCode.InternalServerError)
            throw new Exception(response.StatusCode.ToString());
        string resp = await response.Content.ReadAsStringAsync();
        XchangeFile xchangeFile1 = response.StatusCode < HttpStatusCode.BadRequest
            ? new XchangeFile(resp)
            : new XchangeFile(resp, badData: true);
        return xchangeFile1;
    }


}