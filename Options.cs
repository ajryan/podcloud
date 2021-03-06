namespace podcloud
{
    public class Options
    {
        public string FeedUrl            { get; set; }
        public bool   UploadToSoundCloud { get; set; }
        public string SoundCloudAccessToken { get; set; }
        public int?   EpisodeSkipCount   { get; set; }
        public bool   UploadLatest       { get; set; }
        public bool   UpdateReddit       { get; set; }
        public string SubRedditName      { get; set; }
        public string RedditUsername     { get; set; }
        public string RedditPassword     { get; set; }
    }
}