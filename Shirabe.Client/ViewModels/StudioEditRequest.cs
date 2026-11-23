using Shirabe.Core.Library;
using Shirabe.Core.Metadata;

namespace Shirabe.Client.ViewModels;

/// <summary>The person asked to edit a document of a song in Studio.</summary>
public sealed record StudioEditRequest(TrackSummary Song, StoredDocument Document);
