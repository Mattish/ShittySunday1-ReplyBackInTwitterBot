using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using CoreTweet;

namespace ReplyBackIn
{
    class Program
    {
        private readonly SortedList<DateTime, ReplyInfo> _tweetsToRespondSortedList;
        private readonly object _sortedListLock = new object();
        private int _running;
        private const int TwitterMentionSleep = 60000;
        private static StreamWriter _streamWriter;
        private static string screenName = ConfigurationManager.AppSettings["screenName"];
        public Program(bool ignoreCurrentMentions)
        {

            var tokens = Tokens.Create(ConfigurationManager.AppSettings["consumerToken"],
                ConfigurationManager.AppSettings["consumerSecret"],
                ConfigurationManager.AppSettings["accessToken"],
                ConfigurationManager.AppSettings["accessSecret"]
                );

            _running = 1;
            long lastTweetReadId = 0;
            if (ignoreCurrentMentions)
            {
                Log("Starting by ignoring the current mentions and getting the last Id");
                if (GetStatusesMentionRateLimitsRemaining(tokens) > 0)
                {
                    var tweets = tokens.Statuses.MentionsTimeline();
                    if (tweets != null)
                    {
                        foreach (var mention in tweets.OrderBy(tweet => tweet.Id))
                        {
                            lastTweetReadId = mention.Id;
                        }
                    }
                }
            }
            Thread.Sleep(1000);
            var autoResetEvent = new AutoResetEvent(false);
            var taskForReplying = new Task(() => TaskForReplying(autoResetEvent, tokens));
            taskForReplying.Start();
            _tweetsToRespondSortedList = new SortedList<DateTime, ReplyInfo>();
            var taskForGettingTweets = new Task(() => TaskForCheckingMentions(autoResetEvent, tokens, lastTweetReadId));
            taskForGettingTweets.Start();
            Log("Started listening!");
            Console.ReadKey();
            Log("Ending...");
            Interlocked.Exchange(ref _running, 0);
            autoResetEvent.Set();
        }

        static void Main(string[] args)
        {
            _streamWriter = File.AppendText("Log.txt");
            bool skipCurrentMentions = (args.Length > 0);
            var p = new Program(skipCurrentMentions);
            _streamWriter.Flush();
            _streamWriter.Close();
        }

        public class ReplyInfo
        {
            public string Text;
            public long Id;
            public string UserName;
        }

        private int GetStatusesMentionRateLimitsRemaining(Tokens tokens)
        {
            try
            {
                var rateLimits = tokens.Application.RateLimitStatus();
                Dictionary<string, RateLimit> statuses;
                RateLimit statusesMentionRateLimits;
                rateLimits.TryGetValue("statuses", out statuses);
                statuses.TryGetValue("/statuses/mentions_timeline", out statusesMentionRateLimits);
                if (statusesMentionRateLimits != null)
                    return statusesMentionRateLimits.Remaining;
                return 0;
            }
            catch (Exception e)
            {
                Log("3: Cap'n, We have a serious problem...We can't get any rate limits...");
            }
            return 0;
        }

        void TaskForCheckingMentions(AutoResetEvent autoResetEvent, Tokens tokens, long lastTweetReadIdInput)
        {
            long lastTweetReadId = lastTweetReadIdInput;

            while (_running == 1)
            {
                try
                {
                    int remainingRateLimit = GetStatusesMentionRateLimitsRemaining(tokens);
                    if (remainingRateLimit > 0)
                    {
                        var tweets = tokens.Statuses.MentionsTimeline(count => 100);
                        if (tweets != null)
                        {
                            foreach (var mention in tweets.OrderBy(tweet => tweet.Id))
                            {
                                if (mention.Id > lastTweetReadId)
                                {
                                    Log("1: Id: {0} User: {1}: {2}", mention.Id, mention.User, mention.Text);
                                    lastTweetReadId = mention.Id;
                                    string mentionText = mention.Text;
                                    Match timeMatch = Regex.Match(mentionText, @"(\d{1,2}[mhd]){1,3}");

                                    if (timeMatch.Success)
                                    {
                                        var replyInfo = new ReplyInfo();
                                        replyInfo.Text = mentionText.Remove(timeMatch.Index, timeMatch.Length);
                                        Match replybackinMatch = Regex.Match(replyInfo.Text, "@" + screenName, RegexOptions.IgnoreCase);
                                        replyInfo.Text = replyInfo.Text.Remove(replybackinMatch.Index, replybackinMatch.Length);
                                        replyInfo.Id = mention.Id;
                                        replyInfo.UserName = mention.User.ScreenName;

                                        lock (_sortedListLock)
                                        {
                                            DateTime timeInFuture;
                                            if (TimeInFutureFromTextFormat(mention.CreatedAt.LocalDateTime,
                                                timeMatch.Value,
                                                out timeInFuture))
                                            {
                                                _tweetsToRespondSortedList.Add(timeInFuture, replyInfo);
                                                Log("1: Added {0} - {1}", "@" + replyInfo.UserName + replyInfo.Text,
                                                    timeInFuture);
                                                autoResetEvent.Set();
                                            }
                                            else
                                            {
                                                Log("Error trying to parse for time {0}", mentionText);
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
                catch (Exception e)
                {
                    Log(e.Message + e.StackTrace);
                }
                Thread.Sleep(TwitterMentionSleep);
            }

        }

        static bool TimeInFutureFromTextFormat(DateTime startTime, string input, out DateTime result)
        {
            try
            {
                TimeSpan timeSpan = TimeSpan.Zero;
                Match days = Regex.Match(input, @"\d{1,2}d");
                Match hours = Regex.Match(input, @"\d{1,2}h");
                Match minutes = Regex.Match(input, @"\d{1,2}m");
                if (minutes.Success)
                    timeSpan = timeSpan.Add(TimeSpan.FromMinutes(double.Parse(minutes.Value.Remove(minutes.Value.Length - 1))));
                if (hours.Success)
                    timeSpan = timeSpan.Add(TimeSpan.FromHours(double.Parse(hours.Value.Remove(hours.Value.Length - 1))));
                if (days.Success)
                    timeSpan = timeSpan.Add(TimeSpan.FromDays(double.Parse(days.Value.Remove(days.Value.Length - 1))));
                result = startTime + timeSpan;
                return true;
            }
            catch (Exception e)
            {
                result = DateTime.Now;
                return false;
            }
        }

        void TaskForReplying(AutoResetEvent autoResetEvent, Tokens tokens)
        {
            while (_running == 1)
            {
                TimeSpan timeToWait = TimeSpan.FromHours(1);
                lock (_sortedListLock)
                {
                    if (_tweetsToRespondSortedList.Count > 0)
                    {
                        timeToWait = (_tweetsToRespondSortedList.First().Key - DateTime.Now);
                        if (timeToWait < TimeSpan.Zero)
                            timeToWait = TimeSpan.Zero;
                    }
                }

                if (!autoResetEvent.WaitOne(timeToWait))
                {
                    lock (_sortedListLock)
                    {
                        if (_tweetsToRespondSortedList.Count > 0)
                        {
                            ReplyInfo replyInfo = _tweetsToRespondSortedList.First().Value;
                            try
                            {
                                string statusText = "@" + replyInfo.UserName + replyInfo.Text;
                                while (statusText.Contains("  "))
                                {
                                    statusText = statusText.Replace("  ", " ");
                                }
                                if (
                                    tokens.Statuses.Update(status => statusText,
                                        inReplyToStatusId => replyInfo.Id) != null)
                                {
                                    Log("2: Reminding {0}", "@" + replyInfo.UserName + " " + replyInfo.Text);
                                }
                            }
                            catch (Exception e)
                            {
                                Log("2: {0} - {1}", e.Message, replyInfo.UserName + " " + replyInfo.Text);
                            }
                            _tweetsToRespondSortedList.RemoveAt(0);
                        }
                    }
                }
                else
                {
                    Log("2: Timeout in TaskForReplying Loop!");
                }
            }
        }

        public static void Log(string input, params object[] args)
        {
            _streamWriter.Write(input + "\n");
            Console.WriteLine(input, args);
        }
    }
}
