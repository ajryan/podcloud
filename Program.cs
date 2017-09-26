﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using System.Xml.Linq;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json.Linq;
using RedditSharp;
using RedditSharp.Things;

namespace podcloud
{
  internal class Program
  {
    static void Main(string[] args)
    {
      if (!File.Exists("podcloud.json"))
      {
        Console.WriteLine("Please provide config file podcloud.json. See sampleconfig.json.");
        Environment.Exit(1);
      }

      var builder = new ConfigurationBuilder()
        .SetBasePath(Directory.GetCurrentDirectory())
        .AddJsonFile("podcloud.json");

      var options = new Options();

      builder.Build().Bind(options);

      IEnumerable<XElement> allItems = XDocument.Parse(GetFeed(options.FeedUrl).Result).Descendants("item");
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
        DateTime.TryParse(item.Element("pubDate").Value, out var pubDate);
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
                        Description = item.Element("description")?.Value?.Replace("<p>", "")?.Replace("</p>", ""),
                        Tags        = item.Elements("category").Select(e => e.Value).ToArray()
                      };

        Console.WriteLine($"Item title {podcast.Title}, link {podcast.EpisodeUrl}, released {podcast.ReleaseDate}, enclosure {podcast.Mp3Url}, art {podcast.ArtUrl}, description {podcast.Description.Left(20)}... ");

        if (options.PostToSoundCloud)
        {
          podcast.DownloadFiles();
          PostToSoundCloud(podcast, options.SoundCloudUsername, options.SoundCloudPassword);
        }

        if (options.UpdateReddit)
          UpdateReddit(podcast, options.SubRedditName, options.RedditUsername, options.RedditPassword);
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

    private static void UpdateReddit(Podcast podcast, string subreddit, string username, string password)
    {
      Console.WriteLine("Updating Reddit wiki");

      var reddit = new Reddit(username, password);
      var sub = reddit.GetSubreddit(subreddit);
      var wikiPageContent = sub.Wiki.GetPage("index").MarkdownContent;

      var contentLines = wikiPageContent.Split(Environment.NewLine).ToList();
      var tableSeparatorIndex = contentLines.IndexOf(":--|:-:|:--|:--");
      var newEpisodeLine = $"[{podcast.Title}]({podcast.EpisodeUrl})|{podcast.ReleaseDate:d}|{podcast.Description.Replace("\r", null).Replace("\n", null)}|[Mp3]({podcast.Mp3Url})";
      contentLines.Insert(tableSeparatorIndex + 1, newEpisodeLine);

      Console.WriteLine("New wiki line: " + newEpisodeLine);

      sub.Wiki.EditPage("index", String.Join(Environment.NewLine, contentLines), reason: podcast.Title.Left(256));

      var post = sub.SubmitPost(podcast.Title, podcast.EpisodeUrl);
      var comment = post.Comment($"Official description:\r\n>{podcast.Description}");
      comment.Distinguish(VotableThing.DistinguishType.Special);
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
