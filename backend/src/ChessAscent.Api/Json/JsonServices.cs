using System.Text.Json;
using System.Text.Json.Serialization;

namespace ChessAscent.Api.Json;

public static class JsonServices
{
    /// <summary>
    /// Sets how every request and response body is read and written: a
    /// number only as a JSON number, and an enumeration as the camel-case
    /// name of its member.
    /// </summary>
    public static IServiceCollection AddJsonConventions(this IServiceCollection services) =>
        // LEARN: ConfigureHttpJsonOptions changes the System.Text.Json
        // settings that minimal API endpoints read bodies with and write
        // results with, and that the OpenAPI generator describes those bodies
        // from, much as a Jackson2ObjectMapperBuilderCustomizer changes the
        // one ObjectMapper Spring MVC uses. The settings start from
        // JsonSerializerDefaults.Web: camel-case property names, property
        // names matched without regard to case, and numbers accepted when
        // written as strings.
        services.ConfigureHttpJsonOptions(options =>
        {
            // The Web defaults read "5" as 5, so the document described every
            // integer as an integer or a string of digits, and the front
            // end's generated types as number | string. Strict reads and
            // writes a number only as a JSON number.
            options.SerializerOptions.NumberHandling = JsonNumberHandling.Strict;

            // LEARN: System.Text.Json writes an enum as its number unless a
            // converter says otherwise, where Jackson writes its name. A C#
            // enum is a named integer (see GameStore), so a number would also
            // be read into a value that names no member. This converter
            // writes "white" for Side.White and reads only the names:
            // allowIntegerValues: false refuses 0 as well as 7. The document
            // lists the names as each enum's allowed values.
            options.SerializerOptions.Converters.Add(
                new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false));
        });
}
