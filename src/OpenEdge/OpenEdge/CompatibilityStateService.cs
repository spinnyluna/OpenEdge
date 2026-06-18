using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace OpenEdge;

public sealed class CompatibilityStateService
{
	private readonly object gate = new object();

	private readonly JsonSerializerOptions jsonOptions = new JsonSerializerOptions
	{
		WriteIndented = true
	};

	private CompatibilityStateStore state;

	private readonly string stateFilePath = RuntimePaths.CompatibilityStateFile;

	private readonly string sourcesFilePath = Path.Combine(RuntimePaths.RuntimeRoot, "media-sources.json");

	private readonly string identityFilePath = Path.Combine(RuntimePaths.RuntimeRoot, "media-tag-index.json");

	public void EnsureInitialized()
	{
		lock (gate)
		{
			Directory.CreateDirectory(RuntimePaths.FlagsDir);
			Directory.CreateDirectory(RuntimePaths.TempFlagsDir);
			Directory.CreateDirectory(RuntimePaths.CompatibilityBackupsDir);
			Directory.CreateDirectory(RuntimePaths.CompatibilityTransfersDir);
			state = LoadStateFromDisk();
			if (state == null)
			{
				state = BuildStateFromLegacyFiles();
				SaveStateToDisk();
			}
			else
			{
				SyncMissingLegacyEntriesIntoState();
			}
		}
	}

	public CompatibilityStateSummary GetSummary()
	{
		lock (gate)
		{
			EnsureInitialized();
			return new CompatibilityStateSummary
			{
				PersistentEntryCount = state.PersistentEntries.Count,
				LegacyFlagFileCount = GetLegacyFlagFilePaths().Count,
				StateFileExists = File.Exists(stateFilePath),
				LastLegacyImportUtc = state.LastLegacyImportUtc
			};
		}
	}

	public void MigrateCurrentRuntimeState(bool createBackup)
	{
		lock (gate)
		{
			EnsureInitialized();
			if (createBackup)
			{
				CreateBackupSnapshot();
			}
			state = BuildStateFromLegacyFiles();
			SaveStateToDisk();
		}
	}

	public EverEdgeImportResult ImportEverEdgeData(string selectedPath, bool createBackup)
	{
		lock (gate)
		{
			EnsureInitialized();
			string dataDirectory = ResolveEverEdgeDataDirectory(selectedPath);
			if (string.IsNullOrWhiteSpace(dataDirectory))
			{
				throw new InvalidOperationException("Select an EverEdge folder that contains a Data folder, or select the Data folder itself.");
			}
			if (createBackup)
			{
				CreateBackupSnapshot();
			}
			Directory.CreateDirectory(RuntimePaths.FlagsDir);
			List<string> copiedFiles = new List<string>();
			string legacyTagsFile = Path.Combine(dataDirectory, "tags.txt");
			foreach (string sourceFile in Directory.GetFiles(dataDirectory, "*.txt", SearchOption.TopDirectoryOnly))
			{
				if (string.Equals(Path.GetFileName(sourceFile), "tags.txt", StringComparison.OrdinalIgnoreCase))
				{
					continue;
				}
				string destinationPath = Path.Combine(RuntimePaths.RuntimeRoot, Path.GetFileName(sourceFile));
				File.Copy(sourceFile, destinationPath, overwrite: true);
				copiedFiles.Add(Path.GetFileName(sourceFile));
			}
			string sourceFlagsDirectory = Path.Combine(dataDirectory, "flags");
			int copiedFlagCount = 0;
			if (Directory.Exists(sourceFlagsDirectory))
			{
				foreach (string sourceFile in Directory.GetFiles(sourceFlagsDirectory, "*.txt", SearchOption.TopDirectoryOnly))
				{
					File.Copy(sourceFile, Path.Combine(RuntimePaths.FlagsDir, Path.GetFileName(sourceFile)), overwrite: true);
					copiedFlagCount++;
				}
			}
			state = BuildStateFromLegacyFiles();
			SaveStateToDisk();
			StringBuilder report = new StringBuilder();
			report.AppendLine("EverEdge data imported successfully.");
			report.AppendLine();
			report.AppendLine("Source: " + dataDirectory);
			report.AppendLine("Text files imported: " + copiedFiles.Count);
			foreach (string copiedFile in copiedFiles.OrderBy((string item) => item, StringComparer.OrdinalIgnoreCase))
			{
				report.AppendLine("- " + copiedFile);
			}
			report.AppendLine("Flag files imported: " + copiedFlagCount);
			report.AppendLine("Compatibility entries rebuilt: " + state.PersistentEntries.Count);
			if (File.Exists(legacyTagsFile))
			{
				report.AppendLine("Legacy tags queued for canonical media import: " + legacyTagsFile);
			}
			return new EverEdgeImportResult
			{
				Report = report.ToString(),
				DataDirectory = dataDirectory,
				LegacyTagsFile = legacyTagsFile,
				ImagesDirectory = Path.Combine(dataDirectory, "images"),
				VideosDirectory = Path.Combine(dataDirectory, "videos")
			};
		}
	}

	public void ExportTransferPackage(string filePath, bool createBackup)
	{
		lock (gate)
		{
			EnsureInitialized();
			if (createBackup)
			{
				CreateBackupSnapshot();
			}
			List<string> exportWarnings = new List<string>();
			CompatibilityTransferPackage compatibilityTransferPackage = new CompatibilityTransferPackage
			{
				PersistentEntries = CopyPersistentEntriesIgnoreCase(state.PersistentEntries),
				OptionsContent = ReadOptionalText(RuntimePaths.OptionsFile, exportWarnings),
				TaskLines = ReadOptionalLines(RuntimePaths.TasksFile, exportWarnings),
				LegacyTagLines = ReadOptionalLines(RuntimePaths.TagsFile, exportWarnings),
				MediaSources = ReadOptionalJson<List<MediaSourceDefinition>>(sourcesFilePath, exportWarnings) ?? new List<MediaSourceDefinition>(),
				MediaIdentityStore = ReadOptionalJson<MediaIdentityStore>(identityFilePath, exportWarnings) ?? new MediaIdentityStore(),
				ExportWarnings = exportWarnings
			};
			string directoryName = Path.GetDirectoryName(filePath);
			if (!string.IsNullOrWhiteSpace(directoryName))
			{
				Directory.CreateDirectory(directoryName);
			}
			File.WriteAllText(filePath, JsonSerializer.Serialize(compatibilityTransferPackage, jsonOptions));
		}
	}

	public void ImportTransferPackage(string filePath, bool createBackup)
	{
		lock (gate)
		{
			EnsureInitialized();
			if (createBackup)
			{
				CreateBackupSnapshot();
			}
			CompatibilityTransferPackage compatibilityTransferPackage = JsonSerializer.Deserialize<CompatibilityTransferPackage>(File.ReadAllText(filePath));
			if (compatibilityTransferPackage == null)
			{
				throw new InvalidOperationException("Migration package was empty or invalid.");
			}
			state = new CompatibilityStateStore
			{
				SchemaVersion = compatibilityTransferPackage.SchemaVersion,
				CreatedAtUtc = DateTime.UtcNow,
				LastLegacyImportUtc = DateTime.UtcNow,
				PersistentEntries = CopyPersistentEntriesIgnoreCase(compatibilityTransferPackage.PersistentEntries)
			};
			SaveStateToDisk();
			MirrorPersistentEntriesToLegacyFiles();
			WriteOptionalText(RuntimePaths.OptionsFile, compatibilityTransferPackage.OptionsContent);
			WriteOptionalLines(RuntimePaths.TasksFile, compatibilityTransferPackage.TaskLines);
			WriteOptionalJson(sourcesFilePath, compatibilityTransferPackage.MediaSources);
			WriteOptionalJson(identityFilePath, compatibilityTransferPackage.MediaIdentityStore);
		}
	}

	private static string ResolveEverEdgeDataDirectory(string selectedPath)
	{
		if (string.IsNullOrWhiteSpace(selectedPath) || !Directory.Exists(selectedPath))
		{
			return null;
		}
		if (Directory.Exists(Path.Combine(selectedPath, "flags")) || File.Exists(Path.Combine(selectedPath, "options.txt")) || File.Exists(Path.Combine(selectedPath, "tasks.txt")) || File.Exists(Path.Combine(selectedPath, "tags.txt")))
		{
			return selectedPath;
		}
		string childDataDirectory = Path.Combine(selectedPath, "Data");
		if (Directory.Exists(childDataDirectory))
		{
			return childDataDirectory;
		}
		return null;
	}

	public bool PersistentEntryExists(string name)
	{
		lock (gate)
		{
			EnsureInitialized();
			if (state.PersistentEntries.ContainsKey(name))
			{
				return true;
			}
			if (TryReadLegacyFlagValue(name, out string value))
			{
				state.PersistentEntries[name] = value;
				SaveStateToDisk();
				return true;
			}
			return false;
		}
	}

	public string GetPersistentValue(string name)
	{
		lock (gate)
		{
			EnsureInitialized();
			if (state.PersistentEntries.TryGetValue(name, out string value))
			{
				return value;
			}
			if (TryReadLegacyFlagValue(name, out value))
			{
				state.PersistentEntries[name] = value;
				SaveStateToDisk();
				return value;
			}
			return null;
		}
	}

	public void SetPersistentValue(string name, string value)
	{
		lock (gate)
		{
			EnsureInitialized();
			state.PersistentEntries[name] = value ?? "";
			SaveStateToDisk();
			File.WriteAllText(RuntimePaths.Flag(name), value ?? "");
		}
	}

	public void DeletePersistentValue(string name)
	{
		lock (gate)
		{
			EnsureInitialized();
			state.PersistentEntries.Remove(name);
			SaveStateToDisk();
			string text = RuntimePaths.Flag(name);
			if (File.Exists(text))
			{
				File.Delete(text);
			}
		}
	}

	private CompatibilityStateStore LoadStateFromDisk()
	{
		if (!File.Exists(stateFilePath))
		{
			return null;
		}
		CompatibilityStateStore loadedState = JsonSerializer.Deserialize<CompatibilityStateStore>(File.ReadAllText(stateFilePath));
		if (loadedState != null)
		{
			loadedState.PersistentEntries = CopyPersistentEntriesIgnoreCase(loadedState.PersistentEntries);
		}
		return loadedState;
	}

	private CompatibilityStateStore BuildStateFromLegacyFiles()
	{
		Dictionary<string, string> dictionary = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		foreach (string legacyFlagFilePath in GetLegacyFlagFilePaths())
		{
			dictionary[Path.GetFileNameWithoutExtension(legacyFlagFilePath)] = File.ReadAllText(legacyFlagFilePath);
		}
		return new CompatibilityStateStore
		{
			PersistentEntries = dictionary,
			LastLegacyImportUtc = DateTime.UtcNow
		};
	}

	private void SyncMissingLegacyEntriesIntoState()
	{
		bool flag = false;
		foreach (string legacyFlagFilePath in GetLegacyFlagFilePaths())
		{
			string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(legacyFlagFilePath);
			if (!state.PersistentEntries.ContainsKey(fileNameWithoutExtension))
			{
				state.PersistentEntries[fileNameWithoutExtension] = File.ReadAllText(legacyFlagFilePath);
				flag = true;
			}
		}
		if (flag)
		{
			state.LastLegacyImportUtc = DateTime.UtcNow;
			SaveStateToDisk();
		}
	}

	private void SaveStateToDisk()
	{
		state.PersistentEntries = CopyPersistentEntriesIgnoreCase(state.PersistentEntries);
		File.WriteAllText(stateFilePath, JsonSerializer.Serialize(state, jsonOptions));
	}

	private static Dictionary<string, string> CopyPersistentEntriesIgnoreCase(IDictionary<string, string> source)
	{
		Dictionary<string, string> result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		if (source == null)
		{
			return result;
		}
		foreach (KeyValuePair<string, string> entry in source)
		{
			result[entry.Key] = entry.Value ?? "";
		}
		return result;
	}

	private void CreateBackupSnapshot()
	{
		string text = Path.Combine(RuntimePaths.CompatibilityBackupsDir, DateTime.Now.ToString("yyyyMMdd-HHmmss"));
		Directory.CreateDirectory(text);
		CopyOptionalFile(RuntimePaths.OptionsFile, text);
		CopyOptionalFile(RuntimePaths.TasksFile, text);
		CopyOptionalFile(RuntimePaths.TagsFile, text);
		CopyOptionalFile(RuntimePaths.CompatibilityStateFile, text);
		CopyOptionalFile(sourcesFilePath, text);
		CopyOptionalFile(identityFilePath, text);
		CopyDirectory(RuntimePaths.FlagsDir, Path.Combine(text, "flags"));
	}

	private void MirrorPersistentEntriesToLegacyFiles()
	{
		foreach (string legacyFlagFilePath in GetLegacyFlagFilePaths())
		{
			File.Delete(legacyFlagFilePath);
		}
		foreach (KeyValuePair<string, string> persistentEntry in state.PersistentEntries)
		{
			File.WriteAllText(RuntimePaths.Flag(persistentEntry.Key), persistentEntry.Value ?? "");
		}
	}

	private static List<string> GetLegacyFlagFilePaths()
	{
		if (!Directory.Exists(RuntimePaths.FlagsDir))
		{
			return new List<string>();
		}
		return Directory.GetFiles(RuntimePaths.FlagsDir, "*.txt", SearchOption.TopDirectoryOnly).ToList();
	}

	private static bool TryReadLegacyFlagValue(string name, out string value)
	{
		string text = RuntimePaths.Flag(name);
		if (File.Exists(text))
		{
			value = File.ReadAllText(text);
			return true;
		}
		value = null;
		return false;
	}

	private static string ReadOptionalText(string path, List<string> warnings = null)
	{
		if (!File.Exists(path))
		{
			return "";
		}
		try
		{
			return ReadAllTextShared(path);
		}
		catch (Exception ex)
		{
			warnings?.Add("Skipped " + Path.GetFileName(path) + ": " + ex.Message);
			return "";
		}
	}

	private static string[] ReadOptionalLines(string path, List<string> warnings = null)
	{
		if (!File.Exists(path))
		{
			return Array.Empty<string>();
		}
		try
		{
			return ReadAllTextShared(path).Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
		}
		catch (Exception ex)
		{
			warnings?.Add("Skipped " + Path.GetFileName(path) + ": " + ex.Message);
			return Array.Empty<string>();
		}
	}

	private T ReadOptionalJson<T>(string path, List<string> warnings = null) where T : class
	{
		if (!File.Exists(path))
		{
			return null;
		}
		try
		{
			return JsonSerializer.Deserialize<T>(ReadAllTextShared(path));
		}
		catch (Exception ex)
		{
			warnings?.Add("Skipped " + Path.GetFileName(path) + ": " + ex.Message);
			return null;
		}
	}

	private static string ReadAllTextShared(string path)
	{
		using FileStream fileStream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
		using StreamReader reader = new StreamReader(fileStream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
		return reader.ReadToEnd();
	}

	private void WriteOptionalJson<T>(string path, T value) where T : class
	{
		if (value == null)
		{
			return;
		}
		File.WriteAllText(path, JsonSerializer.Serialize(value, jsonOptions));
	}

	private static void WriteOptionalText(string path, string value)
	{
		File.WriteAllText(path, value ?? "");
	}

	private static void WriteOptionalLines(string path, IReadOnlyList<string> lines)
	{
		if (lines == null)
		{
			return;
		}
		File.WriteAllLines(path, lines);
	}

	private static void CopyOptionalFile(string sourcePath, string destinationDirectory)
	{
		if (!File.Exists(sourcePath))
		{
			return;
		}
		File.Copy(sourcePath, Path.Combine(destinationDirectory, Path.GetFileName(sourcePath)), overwrite: true);
	}

	private static void CopyDirectory(string sourceDirectory, string destinationDirectory)
	{
		if (!Directory.Exists(sourceDirectory))
		{
			return;
		}
		Directory.CreateDirectory(destinationDirectory);
		foreach (string file in Directory.GetFiles(sourceDirectory, "*", SearchOption.AllDirectories))
		{
			string relativePath = Path.GetRelativePath(sourceDirectory, file);
			string directoryName = Path.GetDirectoryName(Path.Combine(destinationDirectory, relativePath));
			if (!string.IsNullOrWhiteSpace(directoryName))
			{
				Directory.CreateDirectory(directoryName);
			}
			File.Copy(file, Path.Combine(destinationDirectory, relativePath), overwrite: true);
		}
	}
}
