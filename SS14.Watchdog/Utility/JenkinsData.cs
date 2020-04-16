using System;
using System.Text.Json.Serialization;

namespace SS14.Watchdog.Utility
{
#pragma warning disable 649
    [Serializable]
    internal sealed class JenkinsJobInfo
    {
        [JsonPropertyName("lastSuccessfulBuild")]
        public JenkinsBuildRef? LastSuccessfulBuild { get; set; }
    }

    [Serializable]
    internal sealed class JenkinsBuildRef
    {
        [JsonPropertyName("number")]
        public int Number { get; set; }
        [JsonPropertyName("url")]
        public string? Url { get; set; }
    }
#pragma warning restore 649
}