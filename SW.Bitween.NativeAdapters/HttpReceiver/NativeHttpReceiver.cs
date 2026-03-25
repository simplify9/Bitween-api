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
      if (_options.AuthType == "ApiKey")
        client.DefaultRequestHeaders.Add("ApiKey", _options.ApiKey);
      else if (_options.AuthType == "Basic")
      {
        string credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes(_options.LoginUsername + ":" + _options.LoginPassword));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
      }
      else if (_options.AuthType == "Bearer")
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _options.LoginPassword);
      else if (_options.AuthType == "Login")
      {
        string loginJson = JsonConvert.SerializeObject(new ReceiverUserLoginModel()
        {
          UserName = _options.LoginUsername,
          Password = _options.LoginPassword
        });
        HttpResponseMessage loginResponse = await client.PostAsync(new Uri(_options.LoginUrl),
          new StringContent(loginJson, Encoding.UTF8, "application/json"));
        loginResponse.EnsureSuccessStatusCode();
        if (loginResponse.StatusCode != HttpStatusCode.OK)
          throw new Exception(loginResponse.StatusCode.ToString());
        string rs = await loginResponse.Content.ReadAsStringAsync();
        LoginResponse rsDeserialized = JsonConvert.DeserializeObject<LoginResponse>(rs);
        client.DefaultRequestHeaders.Authorization =
          new AuthenticationHeaderValue("Bearer", rsDeserialized.Jwt);
      }
      else if (_options.AuthType == "OAuth2")
      {
        var oathRequest = new HttpRequestMessage(HttpMethod.Post, _options.LoginUrl);
        var oauthContentDictionary = new List<KeyValuePair<string, string>>();
        oauthContentDictionary.Add(new KeyValuePair<string, string>("client_id", _options.ClientId));
        oauthContentDictionary.Add(new KeyValuePair<string, string>("client_secret", _options.ClientSecret));
        oauthContentDictionary.Add(new KeyValuePair<string, string>("grant_type", "client_credentials"));
        var oauthContent = new FormUrlEncodedContent(oauthContentDictionary);
        oathRequest.Content = oauthContent;
        var oauthResponse = await client.SendAsync(oathRequest);
        var res = await oauthResponse.Content.ReadAsStringAsync();
        var resDeserialized = JsonConvert.DeserializeObject<OAuth2Response>(res);
        client.DefaultRequestHeaders.Authorization =
          new AuthenticationHeaderValue("Bearer", resDeserialized.access_token);
      }
      
      HttpContent content = null;
      if (!string.IsNullOrEmpty(_options.DefaultRequest ?? string.Empty)) 
      {
        string requestBody = _options.DefaultRequest ?? string.Empty;
        string str = _options.ContentType.ToLower();
        switch (str)
        {
          case "application/x-www-form-urlencoded":
            content =  new FormUrlEncodedContent( JsonConvert.DeserializeObject<Dictionary<string, string>>(requestBody));
            break;
          case "application/json":
            content =  new StringContent(requestBody, Encoding.UTF8, "application/json");
            break;
          default:
            content =  new StringContent(requestBody, Encoding.UTF8, _options.ContentType);
            break;
        }
      }

      Uri uri = new Uri(_options.Url);
      HttpRequestMessage request = new HttpRequestMessage()
      {
        RequestUri = uri,
        Method = HttpMethodFromString(_options.Verb),
        Content = content
      };
      string headers1 = _options.Headers;
      IEnumerable<KeyValuePair<string, string>> headers = headers1?.Split(',').Select((Func<string, KeyValuePair<string, string>>) (h =>
      {
        string[] strArray = h.Split(':');
        return new KeyValuePair<string, string>(strArray[0], strArray[1]);
      }));
      if (headers != null)
      {
        foreach (KeyValuePair<string, string> keyValuePair1 in headers)
        {
          KeyValuePair<string, string> keyValuePair = keyValuePair1;
          request.Headers.Add(keyValuePair.Key, keyValuePair.Value);
        }
      }
      HttpResponseMessage response = await client.SendAsync(request);
      
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