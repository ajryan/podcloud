using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using System.Xml.Linq;
using Microsoft.Extensions.Configuration;
using RedditSharp;
using RedditSharp.Things;

namespace podcloud
{
  internal class Program
  {
    static async Task Main(string[] args)
    {
      var configPath = args.Length > 0 ? args[0] : "podcloud.json";

      if (!File.Exists(configPath))
      {
        Console.WriteLine($"{configPath} does not exist. See sampleconfig.json.");
        Environment.Exit(1);
      }

      var builder = new ConfigurationBuilder()
        .SetBasePath(Directory.GetCurrentDirectory())
        .AddJsonFile(configPath);

      var options = new Options();

      builder.Build().Bind(options);

      string feed = await GetFeed(options.FeedUrl);
      IEnumerable<XElement> allItems = XDocument.Parse(feed).Descendants("item");
      IEnumerable<XElement> itemsToProcess;

      if (options.UploadLatest)
      {
        itemsToProcess = new[] { allItems.First() };
      }
      else
      {
        int skipCount  = options.EpisodeSkipCount ?? 0;
        itemsToProcess = allItems.Reverse().Skip(skipCount);
      }

      int itemNumber = 0;

      XNamespace itunes = "http://www.itunes.com/dtds/podcast-1.0.dtd";

      foreach (XElement item in itemsToProcess)
      {
        itemNumber++;

        string title = item.Element("title")?.Value;
        string link = item.Element("link")?.Value;
        DateTime.TryParse(item.Element("pubDate")?.Value, out var pubDate);
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
                        EpisodeUrl  = link,
                        ReleaseDate = pubDate,
                        Mp3Url      = mp3Url,
                        ArtUrl      = item.Descendants(itunes + "image").FirstOrDefault()?.FirstAttribute?.Value,
                        Description = item.Element("description")?.Value.Replace("<p>", "")?.Replace("</p>", ""),
                        Tags        = item.Elements("category").Select(e => e.Value).ToArray()
                      };

        Console.WriteLine($"Item title {podcast.Title}, link {podcast.EpisodeUrl}, released {podcast.ReleaseDate}, enclosure {podcast.Mp3Url}, art {podcast.ArtUrl}, description {podcast.Description.Left(20)}... ");

        if (options.UploadToSoundCloud)
        {
          podcast.DownloadFiles();
          await PostToSoundCloud(podcast, options.SoundCloudAccessToken);
        }

        if (options.UpdateReddit)
          await UpdateReddit(podcast, options.SubRedditName, options.RedditUsername, options.RedditPassword);
      }

      Console.WriteLine("done");
    }

    private static Task<string> GetFeed(string feedUrl)
    {
      return new HttpClient().GetStringAsync(feedUrl);
    }

    private static async Task PostToSoundCloud(Podcast podcast, string accessToken)
    {
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
      var httpClient = new HttpClient { Timeout = TimeSpan.FromHours(2d) };
      var response = await httpClient.SendAsync(uploadRequest);
      Console.WriteLine("Response " + response);
    }

    private static async Task UpdateReddit(Podcast podcast, string subreddit, string username, string password)
    {
      Console.WriteLine("Updating Reddit wiki");

      var botAgent = new BotWebAgent(
        username, password, "Y4oVLWwzPuymNQ", "_I9Y3z_wIoLWSYT9Y0piHA2-hKI", "https://github.com/ajryan/podcloud");

      var reddit = new Reddit(botAgent, false);
      var sub = await reddit.GetSubredditAsync(subreddit);
      var wikiPageContent = (await sub.GetWiki.GetPageAsync("index")).MarkdownContent;

      var contentLines = wikiPageContent.Split(Environment.NewLine).ToList();
      var tableSeparatorIndex = contentLines.IndexOf(":--|:-:|:--|:--");
      var newEpisodeLine = $"[{podcast.Title.Replace("’", "'")}]({podcast.EpisodeUrl})|{podcast.ReleaseDate:d}|{podcast.Description.Replace("\r", null).Replace("\n", null)}|[Mp3]({podcast.Mp3Url})";
      contentLines.Insert(tableSeparatorIndex + 1, newEpisodeLine);

      Console.WriteLine("New wiki line: " + newEpisodeLine);

      try
      {
        sub.GetWiki.EditPageAsync("index", String.Join(Environment.NewLine, contentLines), reason: podcast.Title.Left(256)).Wait();
      }
      catch (Exception exception)
      {
        Console.WriteLine("Wiki edit failed: " + exception);
      }

      var post = sub.SubmitPostAsync(podcast.Title, podcast.EpisodeUrl).Result;
      var comment = post.CommentAsync($"Official description:\r\n>{podcast.Description}").Result;
      comment.DistinguishAsync(ModeratableThing.DistinguishType.Moderator).Wait();
    }

    private static StringContent GetStringContent(string content)
    {
      StringContent stringContent = new StringContent(content);
      stringContent.Headers.ContentType = null;
      return stringContent;
    }
  }

  internal static class StringExtensions
  {
    public static string Left(this string value, int length) => value.Length <= length ? value : value.Substring(0, length);
  }
}
