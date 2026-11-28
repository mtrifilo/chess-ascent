using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace ChessAscent.Api.ApiDocument;

// LEARN: A schema transformer is called once for each schema the OpenAPI
// generator builds, with the System.Text.Json type and, for a property, the
// property it describes, and may edit the schema before it is written. It
// is the per-schema counterpart of the document transformer in
// HealthApiDescription, like a springdoc PropertyCustomizer next to an
// OpenApiCustomizer.
/// <summary>Puts <c>null</c> in the document only where the API sends or accepts it.</summary>
internal static class NullSchemas
{
    // The metadata entry the generator reads, after every transformer has
    // run, to wrap a property's schema in oneOf [null, the type]. It is not a
    // public constant of Microsoft.AspNetCore.OpenApi, so ApiDocumentTests
    // checks the result: a changed name shows there as a [Required] field
    // that may be null again.
    private const string NullablePropertyMetadata = "x-is-nullable-property";

    public static OpenApiOptions KeepNullWhereItBelongs(this OpenApiOptions options) =>
        options.AddSchemaTransformer((schema, context, _) =>
        {
            if (schema is not OpenApiSchema concrete)
            {
                return Task.CompletedTask;
            }

            // A nullable enum property (GameResult?) is written as a
            // reference to the enum's shared schema, wrapped in oneOf with
            // null, so the property says it may be null. Left alone, the
            // generator also puts null among the members of the shared
            // schema itself, and every other use of the enum would then
            // allow null too.
            if (concrete.Enum is { } members)
            {
                for (var index = members.Count - 1; index >= 0; index--)
                {
                    if (members[index] is null)
                    {
                        members.RemoveAt(index);
                    }
                }
            }

            // A request property can be nullable in C# only so that
            // validation can tell a missing value from a present one (see
            // GameContracts). [Required] refuses null, so the document says
            // the property is never null: neither through the oneOf that
            // wraps a reference, nor through the type list of a plain value
            // such as an int?.
            if (context.JsonPropertyInfo?.AttributeProvider?.IsDefined(typeof(RequiredAttribute), inherit: false) == true)
            {
                concrete.Metadata?.Remove(NullablePropertyMetadata);
                if (concrete.Type is { } types)
                {
                    concrete.Type = types & ~JsonSchemaType.Null;
                }
            }

            return Task.CompletedTask;
        });
}
