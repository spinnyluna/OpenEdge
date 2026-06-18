using System;
using System.Collections.Generic;

namespace OpenEdge;

public sealed class CompatibilityStateStore
{
	public int SchemaVersion { get; set; } = 1;

	public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

	public DateTime LastLegacyImportUtc { get; set; } = DateTime.UtcNow;

	public Dictionary<string, string> PersistentEntries { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
}

public sealed class CompatibilityTransferPackage
{
	public int SchemaVersion { get; set; } = 1;

	public DateTime ExportedAtUtc { get; set; } = DateTime.UtcNow;

	public Dictionary<string, string> PersistentEntries { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

	public string OptionsContent { get; set; } = "";

	public string[] TaskLines { get; set; } = Array.Empty<string>();

	public string[] LegacyTagLines { get; set; } = Array.Empty<string>();

	public List<MediaSourceDefinition> MediaSources { get; set; } = new List<MediaSourceDefinition>();

	public MediaIdentityStore MediaIdentityStore { get; set; } = new MediaIdentityStore();

	public List<string> ExportWarnings { get; set; } = new List<string>();
}

public sealed class EverEdgeImportResult
{
	public string Report { get; set; } = "";

	public string DataDirectory { get; set; } = "";

	public string LegacyTagsFile { get; set; } = "";

	public string ImagesDirectory { get; set; } = "";

	public string VideosDirectory { get; set; } = "";
}

public sealed class CompatibilityStateSummary
{
	public int PersistentEntryCount { get; init; }

	public int LegacyFlagFileCount { get; init; }

	public bool StateFileExists { get; init; }

	public DateTime? LastLegacyImportUtc { get; init; }
}
