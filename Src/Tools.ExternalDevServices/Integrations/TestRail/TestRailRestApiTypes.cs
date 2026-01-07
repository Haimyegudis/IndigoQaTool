using System.ComponentModel;
using Newtonsoft.Json;

namespace Tools.ExternalDevServices.Integrations.TestRail
{
    public class TestRailRestApiTypes
    {
        public class TestSuite
        {
            [JsonProperty("id")] public string Id { get; set; } = "";
            [JsonProperty("name")] public string Name { get; set; } = "";
            public override string ToString() => JsonConvert.SerializeObject(this);
        }

        public class TestCaseMetadata
        {
            [JsonProperty("id")] public string Id { get; set; } = "";
            [JsonProperty("title")] public string Title { get; set; } = "";
            public override string ToString() => JsonConvert.SerializeObject(this);

        }
        public class TestCase
        {
            [JsonProperty("id")] public string Id { get; set; } = "";
            [JsonProperty("title")] public string Title { get; set; } = "";
            [JsonProperty("custom_steps_separated")] public CustomStep[] CustomStepsSeparated { get; set; } = [];
            public override string ToString() => JsonConvert.SerializeObject(this);

        }

        public class CustomStep
        {
            [JsonProperty("content"), Description("Raw step content (what to do) from TestRail")] public string Content { get; set; } = "";
            [JsonProperty("expected"), Description("Raw step expected (what to check) from TestRail")] public string Expected { get; set; } = "";
            public override string ToString() => JsonConvert.SerializeObject(this);
        }

    }
}
