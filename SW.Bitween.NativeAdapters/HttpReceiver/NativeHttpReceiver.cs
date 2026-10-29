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

      // On the request, never on the client - see NativeHttpHandler: the client is shared by
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
      
      // Pages are followed only on the endpoint's own origin: a next link elsewhere would be handed
      // the same credentials.
      var first = new Uri(Require(_options.Url, "Url"));
      var paging = !string.IsNullOrWhiteSpace(_options.NextLinkPath) || !string.IsNullOrWhiteSpace(_options.PageParameter);
      var maxPages = paging ? Math.Max(1, _options.MaxPages) : 1;
      var page = _options.PageStart;
      var uri = string.IsNullOrWhiteSpace(_options.PageParameter) ? first : WithQuery(first, _options.PageParameter!, page);

      for (var pages = 1; pages <= maxPages; pages++)
      {
        var items = Items(await Fetch(client, uri, authorization, apiKey), out var reply);
        foreach (var item in items)
          elementDictionary.TryAdd(elementDictionary.Count.ToString(), item.ToString());

        if (!string.IsNullOrWhiteSpace(_options.NextLinkPath))
        {
          var next = reply.SelectToken(_options.NextLinkPath!)?.ToString();
          if (string.IsNullOrWhiteSpace(next)) break;
          uri = new Uri(uri, next);
          if (uri.GetLeftPart(UriPartial.Authority) != first.GetLeftPart(UriPartial.Authority))
            throw new SWException($"The next page is on {uri.GetLeftPart(UriPartial.Authority)}, not on {first.GetLeftPart(UriPartial.Authority)}; it isn't followed.");
        }
        else if (!string.IsNullOrWhiteSpace(_options.PageParameter))
        {
          if (items.Count == 0) break;
          uri = WithQuery(first, _options.PageParameter!, ++page);
        }
        else break;
      }

      return elementDictionary.Keys;
    }
    
    /// <summary>A request built from the settings, sent and answered as JSON.</summary>
    private async Task<string> Fetch(HttpClient client, Uri uri, AuthenticationHeaderValue? authorization, string? apiKey)
    {
      HttpRequestMessage request = new HttpRequestMessage()
      {
        RequestUri = uri,
        Method = HttpMethodFromString(Require(_options.Verb, "Verb")),
        Content = Body()
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
      return resp;
    }

    /// <summary>A new body for each request: content can't be sent twice.</summary>
    private HttpContent? Body()
    {
      if (string.IsNullOrEmpty(_options.DefaultRequest ?? string.Empty)) return null;
      string requestBody = _options.DefaultRequest ?? string.Empty;
      string str = Require(_options.ContentType, "ContentType").ToLower();
      switch (str)
      {
        case "application/x-www-form-urlencoded":
          return new FormUrlEncodedContent(
            JsonConvert.DeserializeObject<Dictionary<string, string>>(requestBody)
            ?? throw new SWException("DefaultRequest is not a JSON object of form fields."));
        case "application/json":
          return new StringContent(requestBody, Encoding.UTF8, "application/json");
        default:
          return new StringContent(requestBody, Encoding.UTF8, Require(_options.ContentType, "ContentType"));
      }
    }

    /// <summary>The items of one reply: the array at ArrayPath, a root array, or the reply as one item.</summary>
    private JArray Items(string resp, out JToken reply)
    {
      reply = JToken.Parse(resp);
      if (!string.IsNullOrEmpty(_options.ArrayPath))
      {
        var token = reply.SelectToken(_options.ArrayPath);
        if (token is JArray arr)
          return arr;
        throw new Exception($"The path '{_options.ArrayPath}' did not resolve to any token in the response.");
      }

      return reply is JArray rootArray ? rootArray : new JArray(reply);
    }

    private static Uri WithQuery(Uri uri, string name, int value)
    {
      var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
      query[name] = value.ToString(System.Globalization.CultureInfo.InvariantCulture);
      return new UriBuilder(uri) { Query = query.ToString() }.Uri;
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