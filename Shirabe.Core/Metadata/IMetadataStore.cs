using System.Text.Json;
using Shirabe.Metadata;

namespace Shirabe.Core.Metadata;

/// <param name="Value">One JSON value of the definition's type, or an array of them when it is <see cref="FieldDefinition.Multiple"/>.</param>
public sealed record FieldValue(FieldDefinition Definition, JsonElement Value);

/// <summary>
/// The user's own custom fields (the personal layer of the Shirabe Metadata Format) and their definitions. Fields
/// belong to a recording by its stable id, so rescans and file changes never touch them.
/// Failures: <see cref="ArgumentException"/> for a malformed key or value, <see cref="InvalidOperationException"/>
/// when the request contradicts what is stored (the message says how).
/// </summary>
public interface IMetadataStore
{
    Task<IReadOnlyList<FieldDefinition>> GetDefinitionsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates or updates a definition. Only <see cref="FieldKey.IsExtension"/> keys can be defined. While any song has a
    /// value, its type and <see cref="FieldDefinition.Multiple"/> cannot change, because the values would no longer fit.
    /// </summary>
    Task SaveDefinitionAsync(FieldDefinition definition, CancellationToken cancellationToken = default);

    /// <summary>Removes a definition nobody uses. Refused while any song has a value for it: that is the user's data.</summary>
    Task RemoveDefinitionAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>The custom fields of one recording that have a value, ordered by name.</summary>
    Task<IReadOnlyList<FieldValue>> GetFieldsAsync(Guid recordingId, CancellationToken cancellationToken = default);

    /// <summary>Sets or replaces one field. The field needs a definition and the value must fit it.</summary>
    Task SetFieldAsync(Guid recordingId, string key, JsonElement value, CancellationToken cancellationToken = default);

    /// <summary>Removes a field's value from a recording; does nothing when it has none.</summary>
    Task RemoveFieldAsync(Guid recordingId, string key, CancellationToken cancellationToken = default);
}
