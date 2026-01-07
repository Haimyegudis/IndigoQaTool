using System.Text.Json;
using System.Text.Json.Serialization;
using NJsonSchema.Generation;
using JsonSchemaGenerator = NJsonSchema.Generation.JsonSchemaGenerator;

namespace Tools.ExternalDevServices.AI.Orchestration.Utils;

public static class JsonUtils
{
    public static string GetSchema<T>()
    {
        var schemaOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        schemaOptions.Converters.Add(new JsonStringEnumConverter());
        var schemaSettings = new SystemTextJsonSchemaGeneratorSettings
        {
            // Honor your STJ config (camelCase, converters, etc.)
            SerializerOptions = schemaOptions
        };
        return new JsonSchemaGenerator(schemaSettings).Generate(typeof(T)).ToJson();
    }
}