using Newtonsoft.Json;

namespace Tools.ExternalDevServices.Integrations.Confluence;

public class ConfluenceDocumentBodyView
{
    [JsonProperty("value")] public string Value { get; set; } = string.Empty;
}

public class ConfluenceDocumentBody
{
    [JsonProperty("view")] public ConfluenceDocumentBodyView View { get; set; } = null!;
}

public class Links
{
    [JsonProperty("webui")] public string WebUI { get; set; } = "";
    [JsonProperty("tinyui")] public string TinyUI { get; set; } = "";
}

public class Label
{
    [JsonProperty("id")]
    public string Id { get; set; } = string.Empty;

    [JsonProperty("prefix")]
    public string Prefix { get; set; } = "";

    [JsonProperty("name")]
    public string Name { get; set; } = "";
}

public class LabelResults
{
    [JsonProperty("results")]
    public Label[] Labels { get; set; } = null!;
}

public class ConfluenceDocumentAttributes
{
    [JsonProperty("labels")]
    public LabelResults? Results { get; set; }
}

public class ConfluenceDocumentMetadata
{
    [JsonProperty("id")]
    public string Id { get; set; } = string.Empty;

    [JsonProperty("space")]
    public string Space { get; set; } = string.Empty;

    [JsonProperty("title")]
    public string Title { get; set; } = string.Empty;

    [JsonProperty("metadata")]
    public ConfluenceDocumentAttributes? Attributes { get; set; }

    [JsonProperty("_links")]
    public Links Links { get; set; } = null!;
}

public class ConfluenceDocumentMetadataResponse
{
    [JsonProperty("results")]
    public ConfluenceDocumentMetadata[]? Results { get; set; }
}

public class ConfluenceDocumentResult
{
    [JsonProperty("id")] public string Id { get; set; } = string.Empty;

    [JsonProperty("title")] public string Title { get; set; } = string.Empty;

    [JsonProperty("type")] public string Type { get; set; } = string.Empty;

    [JsonProperty("status")] public string Status { get; set; } = string.Empty;

    [JsonProperty("metadata")] public ConfluenceDocumentAttributes? Attributes { get; set; }

    [JsonProperty("body")] public ConfluenceDocumentBody Body { get; set; } = null!;
}

public class ConfluenceDocumentResponse
{
    [JsonProperty("results")]
    public ConfluenceDocumentResult[] Results { get; set; } = null!;
}