namespace podcloud
{
    public class Options
    {
        public string FeedUrl            { get; set; }
        public bool   PostToSoundCloud   { get; set; }
        public string SoundCloudUsername { get; set; }
        public string SoundCloudPassword { get; set; }
        public int?   EpisodeSkipCount   { get; set; }
        public bool   UploadLatest       { get; set; }
        public bool   UpdateReddit   { get; set; }
        public string SubRedditName      { get; set; }
        public string RedditUsername     { get; set; }
        public string RedditPassword     { get; set; }
    }
}