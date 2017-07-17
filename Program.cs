using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace podcloud
{
  internal class Program
  {
    static void Main(string[] args)
    {
      if (args.Length < 3)
      {
        Console.WriteLine("Usage: podcloud.exe feedurl soundcloudusername soundcloudpassword [# eps to skip|'latest']");
        return;
      }

      string feedUrl            = args[0];
      string soundCloudUsername = args[1];
      string soundCloudPassword = args[2];
      string episodeArg         = args.Length > 3 ? args[3] : null;

      IEnumerable<XElement> allItems = XDocument.Parse(GetFeed(feedUrl).Result).Descendants("item");
      IEnumerable<XElement> itemsToProcess;

      if (episodeArg == "latest")
      {
        itemsToProcess = new[] { allItems.First() };
      }
      else
      {
        int skipCount  = episodeArg != null ? Int32.Parse(episodeArg) : 0;
        itemsToProcess = allItems.Reverse().Skip(skipCount);
      }

      int itemNumber = 0;

      XNamespace itunes = "http://www.itunes.com/dtds/podcast-1.0.dtd";

      foreach (XElement item in itemsToProcess)
      {
        itemNumber++;

        string title = item.Element("title")?.Value;
        string mp3Url = item.Element("enclosure")?.Attribute("url")?.Value;

        if (String.IsNullOrEmpty(title) || String.IsNullOrEmpty(mp3Url))
        {
          Console.WriteLine($"Item {itemNumber} has no title or mp3 url");
          continue;
        }

        if (title.Length > 100)
          title = title.Substring(0, 100);

        var podcast = new Podcast
                      {
                        Title       = title,
                        Mp3Url      = mp3Url,
                        ArtUrl      = item.Descendants(itunes + "image").FirstOrDefault()?.FirstAttribute?.Value,
                        Description = item.Element("description")?.Value?.Replace("<p>", "")?.Replace("</p>", ""),
                        Tags        = item.Elements("category").Select(e => e.Value).ToArray()
                      };

        Console.WriteLine($"Item title {podcast.Title}, enclosure {podcast.Mp3Url}, description {podcast.Description} art {podcast.ArtUrl}");
        podcast.DownloadFiles();
        PostToSoundCloud(podcast, soundCloudUsername, soundCloudPassword);
      }

      Console.WriteLine("done");
    }

    private static Task<string> GetFeed(string feedUrl)
    {
      return new HttpClient().GetStringAsync(feedUrl);
    }

    private static void PostToSoundCloud(Podcast podcast, string userName, string password)
    {
      const string CLIENT_ID = "ffc80dc8b5bd435a15f9808724f73c40";
      const string CLIENT_SECRET = "b299b6681e00dfd9f5015639c7f5fe29";

      var httpClient = new HttpClient { Timeout = TimeSpan.FromHours(2d) };
      var requestUrl = $"https://api.soundcloud.com/oauth2/token?client_id={CLIENT_ID}&client_secret={CLIENT_SECRET}&grant_type=password&username={userName}&password={password}";

      var tokenRequest = new HttpRequestMessage(
        HttpMethod.Post,
        requestUrl);

      string accessToken = httpClient.SendAsync(tokenRequest).ContinueWith(responseTask =>
      {
        var tokenObject = JObject.Parse(responseTask.Result.Content.ReadAsStringAsync().Result);
        return (string)((JValue)tokenObject["access_token"]).Value;
      }).Result;

      var mp3Content = new ByteArrayContent(File.ReadAllBytes(podcast.Mp3Path));
      mp3Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

      var formContent = new MultipartFormDataContent(Guid.NewGuid().ToString())
      {
        { GetStringContent(accessToken),          "[oauth_token]" },
        { GetStringContent(podcast.Title),        "track[title]" },
        { GetStringContent(podcast.Description),  "track[description]" },
        { GetStringContent(podcast.GetTagList()), "track[tag_list]" },
        { GetStringContent("Storytelling"),       "track[genre]" },
        { GetStringContent("public"),             "track[sharing]" },
        { GetStringContent("podcast"),            "track[track_type]" },
        { mp3Content,                             "track[asset_data]", Path.GetFileName(podcast.Mp3Path)}
      };

      if (podcast.ArtPath != null)
      {
        ByteArrayContent artworkContent = new ByteArrayContent(File.ReadAllBytes(podcast.ArtPath));
        artworkContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

        formContent.Add(artworkContent, "track[artwork_data]", Path.GetFileName(podcast.ArtPath));
      }

      var uploadRequest = new HttpRequestMessage(HttpMethod.Post, $"https://api.soundcloud.com/tracks?oauth_token={accessToken}")
      {
        Content = formContent
      };

      Console.WriteLine("Uploading...");
      httpClient.SendAsync(uploadRequest).ContinueWith(responseTask => Console.WriteLine("Response " + responseTask.Result.ToString())).Wait();
    }

    private static StringContent GetStringContent(string content)
    {
      StringContent stringContent = new StringContent(content);
      stringContent.Headers.ContentType = null;
      return stringContent;
    }
  }
}
