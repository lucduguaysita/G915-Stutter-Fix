using System;
using System.Net;
using Newtonsoft.Json.Linq;

namespace KeyboardRepeatFilter
{
    /// <summary>
    /// Best-effort check for a newer GitHub release. Every failure mode (no
    /// network, rate limit, timeout, malformed JSON, unparseable tag) is treated
    /// the same as "no update available" and never surfaces to the user: the app
    /// is fully usable offline, so a missed check must be silent.
    /// </summary>
    internal static class UpdateChecker
    {
        // The "latest release" endpoint returns the most recent non-prerelease,
        // non-draft release for the repository.
        private const string LatestReleaseApi =
            "https://api.github.com/repos/lucduguaysita/G915-Stutter-Fix/releases/latest";

        // Human-facing page used as a fallback link and from the About box.
        public const string ReleasesPageUrl =
            "https://github.com/lucduguaysita/G915-Stutter-Fix/releases/latest";

        // Hard cap on the whole request. WebClient's default is ~100s; a check that
        // cannot answer in a few seconds is abandoned so the background thread never
        // lingers. A timeout is just another silent "no update".
        private const int RequestTimeoutMs = 8000;

        // Outcome of a check. Failed covers every error path (offline, timeout, rate
        // limit, malformed response): the network answer is simply unknown.
        public enum Status { Failed, UpToDate, UpdateAvailable }

        public sealed class Result
        {
            public Status Status { get; set; }
            public Version Latest { get; set; } // normalised Major.Minor.Build (when known)
            public string Tag { get; set; }     // raw tag, e.g. "v3.2.0" (when known)
            public string Url { get; set; }     // release page to open (when newer)
        }

        /// <summary>
        /// Queries the latest GitHub release and reports whether it is newer than
        /// <paramref name="current"/>. Always returns a Result; its Status is Failed
        /// when the check could not be completed. Blocking; call from a background
        /// thread.
        /// </summary>
        public static Result CheckForNewer(Version current)
        {
            if (current == null)
            {
                return new Result { Status = Status.Failed };
            }

            try
            {
                // GitHub's API is HTTPS-only and rejects older TLS; make sure 1.2 is
                // permitted without disturbing whatever the OS already enabled.
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;

                string json;
                using (var client = new TimedWebClient(RequestTimeoutMs))
                {
                    // GitHub returns 403 to requests without a User-Agent.
                    client.Headers[HttpRequestHeader.UserAgent] = "KeyboardRepeatFilter-update-check";
                    client.Headers[HttpRequestHeader.Accept] = "application/vnd.github+json";
                    json = client.DownloadString(LatestReleaseApi);
                }

                var obj = JObject.Parse(json);
                string tag = (string)obj["tag_name"];
                if (!TryParseVersion(tag, out Version latest))
                {
                    return new Result { Status = Status.Failed };
                }

                if (latest > Normalize(current))
                {
                    string url = (string)obj["html_url"];
                    return new Result
                    {
                        Status = Status.UpdateAvailable,
                        Latest = latest,
                        Tag = tag,
                        Url = string.IsNullOrWhiteSpace(url) ? ReleasesPageUrl : url
                    };
                }

                return new Result { Status = Status.UpToDate, Latest = latest, Tag = tag };
            }
            catch
            {
                return new Result { Status = Status.Failed }; // best-effort: unknown
            }
        }

        // Parses a release tag such as "v3.2.0" or "3.2" into a normalised version.
        private static bool TryParseVersion(string tag, out Version version)
        {
            version = null;
            if (string.IsNullOrWhiteSpace(tag))
            {
                return false;
            }

            string s = tag.Trim();
            if (s.StartsWith("v", StringComparison.OrdinalIgnoreCase))
            {
                s = s.Substring(1);
            }

            if (!Version.TryParse(s, out Version parsed))
            {
                return false;
            }

            version = Normalize(parsed);
            return true;
        }

        // Collapses a version to Major.Minor.Build with absent components treated as
        // 0, so the 3-part release tags ("3.1.0") compare cleanly against the 4-part
        // assembly version ("3.1.0.0") without the unspecified-component (-1) skew.
        private static Version Normalize(Version v) =>
            new Version(Math.Max(0, v.Major), Math.Max(0, v.Minor), Math.Max(0, v.Build));

        // WebClient does not expose a timeout, so set one on the underlying request.
        // Bounds the whole check; on expiry DownloadString throws and the caller's
        // catch turns it into a silent "no update".
        private sealed class TimedWebClient : WebClient
        {
            private readonly int _timeoutMs;

            public TimedWebClient(int timeoutMs)
            {
                _timeoutMs = timeoutMs;
            }

            protected override WebRequest GetWebRequest(Uri address)
            {
                var request = base.GetWebRequest(address);
                if (request != null)
                {
                    request.Timeout = _timeoutMs;
                    if (request is HttpWebRequest http)
                    {
                        http.ReadWriteTimeout = _timeoutMs;
                    }
                }

                return request;
            }
        }
    }
}
