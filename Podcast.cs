using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace podcloud
{
  public class Podcast
  {
    public string Title { get; set; }

    public string Description { get; set; }

    public string[] Tags { get; set; }

    public string Mp3Url { get; set; }

    public string ArtUrl { get; set; }

    public string Mp3Path { get; private set; }

    public string ArtPath { get; private set; }

    public string GetTagList()
    {
      return String.Join(" ", Tags.Select(t => !t.Contains(" ") ? t : string.Format("\"{0}\"", t)));
    }

    public void DownloadFiles()
    {
      var downloadTasks = new List<Task<string>> { DownloadFile(Mp3Url) };

      if (!String.IsNullOrEmpty(ArtUrl))
        downloadTasks.Add(DownloadFile(ArtUrl));

      Task.WaitAll(downloadTasks.Cast<Task>().ToArray());

      Mp3Path = downloadTasks[0].Result;
      ArtPath = downloadTasks.Count > 1 ? downloadTasks[1].Result : null;
    }

    private async Task<string> DownloadFile(string url)
    {
      string filePath = Path.Combine(Path.GetTempPath(), Path.GetFileName(new Uri(url).LocalPath));

      if (File.Exists(filePath))
      {
        Console.WriteLine($"{filePath} already exists.");
        return filePath;
      }

      Console.WriteLine($"Downloading {url}");

      var client = new HttpClient { Timeout = TimeSpan.FromHours(2d) };

      byte[] byteArrayAsync = await client.GetByteArrayAsync(url);
      File.WriteAllBytes(filePath, byteArrayAsync);

      Console.WriteLine($"Downloaded to {filePath}");

      return filePath;
    }
  }
}
