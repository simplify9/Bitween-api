using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SW.PrimitiveTypes;

namespace SW.Bitween.NativeAdapters.HttpReceiver;

public class NativeHttpReceiver(IDynamicHttpProxy httpProxy) : INativeInfolinkReceiver
{
  IDictionary<string, string> elementDictionary = new Dictionary<string, string>();
  
    private HttpReceiverInput _options = new();
    /// <summary>
    /// A required adapter setting, or a message naming it. These all used to flow into the HTTP
    /// stack as null and come back as a NullReferenceException that named nothing, so a blank
    /// field in a subscription's configuration was diagnosed by guesswork.
    /// </summary>
    private static string Require(string? value, string setting) =>
        !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new SWException($"The HTTP receiver needs '{setting}' to be set.");

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
            case "patch":
                return HttpMethod.Patch;
            default:
                return HttpMethod.Post;
        }
    }
    
    public async Task Initialize()
    {
        var data = await Task.FromResult(new{  });
    }
    
    public async Task Finalize()
    {
        var data = await Task.FromResult(new{  });
    }
    
    public async Task<IEnumerable<string>> ListFiles()
    {
      HttpClient client = httpProxy.GetClient(_options.Url);

      // On the request, never on the client — see NativeHttpHandler: the client is shared by
      // every subscription calling the same origin.
      string? apiKey = null;
      AuthenticationHeaderValue? authorization = null;
      if (_options.AuthType == "ApiKey")
        apiKey = _options.ApiKey;
      else if (_options.AuthType == "Basic")
      {
        string credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes(_options.LoginUsername + ":" + _options.LoginPassword));
        authorization = new AuthenticationHeaderValue("Basic", credentials);
      }
      else if (_options.AuthType == "Bearer")
        authorization = new AuthenticationHeaderValue("Bearer", _options.LoginPassword);
      else if (_options.AuthType == "Login")
      {
        string token = await HttpLogin.GetToken(client, Require(_options.LoginUrl, "LoginUrl"), _options.LoginBody,
          _options.LoginTokenPath, _options.LoginUsername, _options.LoginPassword,
          new ReceiverUserLoginModel()
          {
            UserName = _options.LoginUsername,
            Password = _options.LoginPassword
          });
        authorization = new AuthenticationHeaderValue("Bearer", token);
      }
      else if (_options.AuthType == "OAuth2")
      {
        authorization = new AuthenticationHeaderValue("Bearer",
          await OAuthTokens.GetAsync(client, _options.LoginUrl, _options.ClientId, _options.ClientSecret));
      }
      
      HttpContent? content = null;
      if (!string.IsNullOrEmpty(_options.DefaultRequest ?? string.Empty)) 
      {
        string requestBody = _options.DefaultRequest ?? string.Empty;
        string str = Require(_options.ContentType, "ContentType").ToLower();
        switch (str)
        {
          case "application/x-www-form-urlencoded":
            content = new FormUrlEncodedContent(
              JsonConvert.DeserializeObject<Dictionary<string, string>>(requestBody)
              ?? throw new SWException("DefaultRequest is not a JSON object of form fields."));
            break;
          case "application/json":
            content =  new StringContent(requestBody, Encoding.UTF8, "application/json");
            break;
          default:
            content = new StringContent(requestBody, Encoding.UTF8, Require(_options.ContentType, "ContentType"));
            break;
        }
      }

      Uri uri = new Uri(Require(_options.Url, "Url"));
      HttpRequestMessage request = new HttpRequestMessage()
      {
        RequestUri = uri,
        Method = HttpMethodFromString(Require(_options.Verb, "Verb")),
        Content = content
      };
      foreach (var (name, value) in HeaderList.Parse(_options.Headers))
        request.Headers.Add(name, value);
      if (authorization is not null)
        request.Headers.Authorization = authorization;
      if (apiKey is not null)
        request.Headers.Add("ApiKey", apiKey);

      using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(Math.Max(1, _options.TimeoutSeconds)));
      HttpResponseMessage response;
      try
      {
        response = await client.SendAsync(request, timeout.Token);
      }
      catch (OperationCanceledException) when (timeout.IsCancellationRequested)
      {
        throw new TimeoutException($"{uri.GetLeftPart(UriPartial.Authority)} did not answer within {_options.TimeoutSeconds} seconds.");
      }
      
      if (response.StatusCode < HttpStatusCode.OK || response.StatusCode >= HttpStatusCode.InternalServerError)
        throw new Exception(response.StatusCode.ToString());
      string resp = await response.Content.ReadAsStringAsync();
      
      if (response.StatusCode >= HttpStatusCode.BadRequest)
        throw new Exception($"Request failed with status {response.StatusCode}: {resp}");
      // XchangeFile file = response.StatusCode < HttpStatusCode.BadRequest ? new XchangeFile(resp) : new XchangeFile(resp, badData: true);
      
      var jsonResponse = JToken.Parse(resp);
      
      JArray items;
      if (!string.IsNullOrEmpty(_options.ArrayPath))
      {
        var token = jsonResponse.SelectToken(_options.ArrayPath);
        if (token is JArray arr)
          items = arr;
        else
          throw new Exception($"The path '{_options.ArrayPath}' did not resolve to any token in the response.");
      }
      else
      {
        items = jsonResponse is JArray rootArray
          ? rootArray
          : new JArray(jsonResponse);
      }
      
      for (int i = 0; i < items.Count; i++)
      {
        elementDictionary.TryAdd(i.ToString(), items[i].ToString());
      }

      return elementDictionary.Keys;
    }
    
    public async Task<XchangeFile> GetFile(string fileId)
    {
      return new XchangeFile(elementDictionary[fileId]);
    }

    public async Task DeleteFile(string fileId)
    {
      var data = await Task.FromResult(new{  });
    }

    public string Name => "NativeHttpReceiver";

    public void InitializeStartupValues(IDictionary<string, string> settings)
    {
      _options = settings.ConvertTo<HttpReceiverInput>();
    }

    public Type StartupValuesType => typeof(HttpReceiverInput);
}