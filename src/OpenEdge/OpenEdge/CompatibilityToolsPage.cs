using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using Microsoft.Win32;

namespace OpenEdge;

public partial class CompatibilityToolsPage : Page, IComponentConnector
{
	private readonly CompatibilityStateService compatibilityStateService;

	private readonly SettingsRegistry settingsRegistry;

	private readonly MediaCatalogService mediaCatalog;

	public CompatibilityToolsPage(CompatibilityStateService compatibilityStateService, SettingsRegistry settingsRegistry, MediaCatalogService mediaCatalog)
	{
		this.compatibilityStateService = compatibilityStateService;
		this.settingsRegistry = settingsRegistry;
		this.mediaCatalog = mediaCatalog;
		InitializeComponent();
		RefreshSummary();
	}

	private void RefreshSummary()
	{
		CompatibilityStateSummary summary = compatibilityStateService.GetSummary();
		SummaryText.Text = "Compatibility state file: " + RuntimePaths.CompatibilityStateFile + "\nLegacy flags directory: " + RuntimePaths.FlagsDir + "\nBackups: " + RuntimePaths.CompatibilityBackupsDir + "\nTransfer packages: " + RuntimePaths.CompatibilityTransfersDir;
		StatusText.Text = "Persistent entries tracked: " + summary.PersistentEntryCount + "\nLegacy flag files found: " + summary.LegacyFlagFileCount + "\nCompatibility state exists: " + summary.StateFileExists + "\nLast legacy import (UTC): " + (summary.LastLegacyImportUtc?.ToString("u") ?? "not yet imported");
	}

	private void ImportEverEdge_Click(object sender, RoutedEventArgs e)
	{
		OpenFolderDialog openFolderDialog = new OpenFolderDialog
		{
			Title = "Select the EverEdge folder, or its Data folder"
		};
		if (openFolderDialog.ShowDialog() == true)
		{
			try
			{
				EverEdgeImportResult importResult = compatibilityStateService.ImportEverEdgeData(openFolderDialog.FolderName, createBackup: true);
				string report = CompleteEverEdgeMediaImport(importResult);
				RefreshSummary();
				MessageBox.Show(report, "OpenEdge", MessageBoxButton.OK, MessageBoxImage.Information);
			}
			catch (Exception ex)
			{
				MessageBox.Show("EverEdge import failed:\n" + ex.Message, "OpenEdge", MessageBoxButton.OK, MessageBoxImage.Error);
			}
		}
	}

	private string CompleteEverEdgeMediaImport(EverEdgeImportResult importResult)
	{
		StringBuilder report = new StringBuilder(importResult.Report ?? "");
		int addedSourceCount = EnsureEverEdgeMediaSources(importResult);
		if (addedSourceCount > 0)
		{
			report.AppendLine("Media sources added: " + addedSourceCount);
		}
		if (File.Exists(importResult.LegacyTagsFile))
		{
			mediaCatalog.Reload();
			List<string> oldRoots = new List<string>();
			if (Directory.Exists(importResult.ImagesDirectory))
			{
				oldRoots.Add(importResult.ImagesDirectory);
			}
			if (Directory.Exists(importResult.VideosDirectory))
			{
				oldRoots.Add(importResult.VideosDirectory);
			}
			LegacyTagImportResult tagResult = mediaCatalog.ImportLegacyTags(importResult.LegacyTagsFile, oldRoots, overwriteExistingTags: true);
			report.AppendLine("Legacy tags imported into media-tag-index.json: " + tagResult.ImportedCount);
			report.AppendLine("Legacy tag import report: " + tagResult.ReportPath);
		}
		return report.ToString();
	}

	private int EnsureEverEdgeMediaSources(EverEdgeImportResult importResult)
	{
		List<MediaSourceDefinition> sources = mediaCatalog.GetSources().Select(CloneMediaSource).ToList();
		int nextSortOrder = sources.Count == 0 ? 0 : sources.Max((MediaSourceDefinition source) => source.SortOrder) + 1;
		int addedCount = 0;
		if (Directory.Exists(importResult.ImagesDirectory) && !sources.Any((MediaSourceDefinition source) => string.Equals(source.RootPath, importResult.ImagesDirectory, StringComparison.OrdinalIgnoreCase)))
		{
			sources.Add(new MediaSourceDefinition
			{
				Id = "everedge-images-" + Guid.NewGuid().ToString("N"),
				Name = "EverEdge Images",
				RootPath = importResult.ImagesDirectory,
				IsEnabled = true,
				ImagesEnabled = true,
				VideosEnabled = false,
				SortOrder = nextSortOrder++
			});
			addedCount++;
		}
		if (Directory.Exists(importResult.VideosDirectory) && !sources.Any((MediaSourceDefinition source) => string.Equals(source.RootPath, importResult.VideosDirectory, StringComparison.OrdinalIgnoreCase)))
		{
			sources.Add(new MediaSourceDefinition
			{
				Id = "everedge-videos-" + Guid.NewGuid().ToString("N"),
				Name = "EverEdge Videos",
				RootPath = importResult.VideosDirectory,
				IsEnabled = true,
				ImagesEnabled = false,
				VideosEnabled = true,
				SortOrder = nextSortOrder++
			});
			addedCount++;
		}
		if (addedCount > 0)
		{
			mediaCatalog.SaveSources(sources);
		}
		return addedCount;
	}

	private static MediaSourceDefinition CloneMediaSource(MediaSourceDefinition source)
	{
		return new MediaSourceDefinition
		{
			Id = source.Id,
			Name = source.Name,
			RootPath = source.RootPath,
			IsEnabled = source.IsEnabled,
			ImagesEnabled = source.ImagesEnabled,
			VideosEnabled = source.VideosEnabled,
			IsLegacy = source.IsLegacy,
			SortOrder = source.SortOrder,
			FolderRules = source.FolderRules.Select(delegate(MediaFolderRule rule)
			{
				return new MediaFolderRule { RelativeFolderPath = rule.RelativeFolderPath, IsIncluded = rule.IsIncluded };
			}).ToList()
		};
	}

	private void Export_Click(object sender, RoutedEventArgs e)
	{
		SaveFileDialog saveFileDialog = new SaveFileDialog
		{
			Filter = "OpenEdge user data (*.json)|*.json",
			InitialDirectory = RuntimePaths.CompatibilityTransfersDir,
			FileName = "openedge-user-data-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".json"
		};
		if (saveFileDialog.ShowDialog() == true)
		{
			compatibilityStateService.ExportTransferPackage(saveFileDialog.FileName, createBackup: false);
			RefreshSummary();
			MessageBox.Show("User data exported successfully.", "OpenEdge", MessageBoxButton.OK, MessageBoxImage.Information);
		}
	}

	private void ExportDiagnostics_Click(object sender, RoutedEventArgs e)
	{
		Directory.CreateDirectory(RuntimePaths.DebugDir);
		string timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
		string filePath = Path.Combine(RuntimePaths.DebugDir, "openedge-diagnostics-" + timestamp + ".zip");
		CreateDiagnosticsZip(filePath);
		MessageBox.Show("Diagnostics bundle exported to:\n" + filePath, "OpenEdge", MessageBoxButton.OK, MessageBoxImage.Information);
	}

	private string BuildDiagnosticsReport()
	{
		CompatibilityStateSummary summary = compatibilityStateService.GetSummary();
		StringBuilder builder = new StringBuilder();
		builder.AppendLine("# OpenEdge Compatibility Diagnostics");
		builder.AppendLine();
		builder.AppendLine("Generated: " + DateTime.Now.ToString("u"));
		builder.AppendLine();
		builder.AppendLine("## Compatibility state");
		builder.AppendLine();
		builder.AppendLine("- State file: `" + RuntimePaths.CompatibilityStateFile + "`");
		builder.AppendLine("- Persistent entries: " + summary.PersistentEntryCount);
		builder.AppendLine("- Legacy flag files: " + summary.LegacyFlagFileCount);
		builder.AppendLine("- State file exists: " + summary.StateFileExists);
		builder.AppendLine("- Last legacy import UTC: " + (summary.LastLegacyImportUtc?.ToString("u") ?? "not yet imported"));
		builder.AppendLine();
		builder.AppendLine("## Canonical setting aliases");
		builder.AppendLine();
		foreach (SettingDefinition definition in settingsRegistry.GetDefinitions().Where(HasAlias).OrderBy((SettingDefinition item) => item.Key, StringComparer.OrdinalIgnoreCase))
		{
			builder.AppendLine("- `" + definition.Key + "`: " + string.Join(", ", BuildAliasParts(definition)));
		}
		builder.AppendLine();
		builder.AppendLine("## Media tag stores");
		builder.AppendLine();
		builder.AppendLine("- Primary identity index: `" + Path.Combine(RuntimePaths.RuntimeRoot, "media-tag-index.json") + "` exists=" + File.Exists(Path.Combine(RuntimePaths.RuntimeRoot, "media-tag-index.json")));
		builder.AppendLine("- Legacy tags input: `" + RuntimePaths.TagsFile + "` exists=" + File.Exists(RuntimePaths.TagsFile) + " lines=" + (File.Exists(RuntimePaths.TagsFile) ? File.ReadAllLines(RuntimePaths.TagsFile).Length : 0));
		builder.AppendLine();
		builder.AppendLine("## Script migration diagnostics");
		builder.AppendLine();
		builder.AppendLine("Run `powershell -ExecutionPolicy Bypass -File docs/recovery/audit-legacy-state.ps1` from the repository root for exact remaining legacy command locations.");
		return builder.ToString();
	}

	private void CreateDiagnosticsZip(string zipPath)
	{
		if (File.Exists(zipPath))
		{
			File.Delete(zipPath);
		}
		using ZipArchive zipArchive = ZipFile.Open(zipPath, ZipArchiveMode.Create);
		AddTextEntry(zipArchive, "diagnostics/compatibility-diagnostics.md", BuildDiagnosticsReport());
		AddTextEntry(zipArchive, "diagnostics/manifest.txt", BuildDiagnosticsManifest());
		AddFileIfExists(zipArchive, RuntimePaths.CompatibilityStateFile, "state/compatibility-state.json");
		AddFileIfExists(zipArchive, Path.Combine(RuntimePaths.RuntimeRoot, "media-sources.json"), "media/media-sources.json");
		AddFileIfExists(zipArchive, Path.Combine(RuntimePaths.RuntimeRoot, "media-sources.json.backup"), "media/media-sources.json.backup");
		AddFileIfExists(zipArchive, Path.Combine(RuntimePaths.RuntimeRoot, "media-tag-index.json"), "media/media-tag-index.json");
		AddFileIfExists(zipArchive, Path.Combine(RuntimePaths.RuntimeRoot, "media-tag-index.json.backup"), "media/media-tag-index.json.backup");
		AddFileIfExists(zipArchive, RuntimePaths.OptionsFile, "state/options.txt");
		AddFileIfExists(zipArchive, RuntimePaths.TasksFile, "state/tasks.txt");
		AddFileIfExists(zipArchive, RuntimePaths.TagsFile, "state/tags.txt");
		AddFileIfExists(zipArchive, RuntimePaths.TagGroupsFile, "state/tagGroups.txt");
		AddDirectoryFiles(zipArchive, RuntimePaths.FlagsDir, "flags");
		AddSessionTraceFiles(zipArchive);
		AddRecentDebugFiles(zipArchive, zipPath);
	}

	private string BuildDiagnosticsManifest()
	{
		StringBuilder builder = new StringBuilder();
		builder.AppendLine("OpenEdge diagnostics bundle");
		builder.AppendLine("Generated: " + DateTime.Now.ToString("u"));
		builder.AppendLine("Runtime root: " + RuntimePaths.RuntimeRoot);
		builder.AppendLine();
		builder.AppendLine("Included when present:");
		builder.AppendLine("- diagnostics/compatibility-diagnostics.md");
		builder.AppendLine("- debug/session-trace.log plus retained debug/session-trace-*.log archives");
		builder.AppendLine("- recent debug reports/logs");
		builder.AppendLine("- media/media-sources.json");
		builder.AppendLine("- media/media-tag-index.json");
		builder.AppendLine("- state/options.txt, tasks.txt, tags.txt, tagGroups.txt, compatibility-state.json");
		builder.AppendLine("- flags/*.txt and flags/temp/*.txt");
		builder.AppendLine();
		builder.AppendLine("Privacy note: media source/index files can contain local folder and file paths. Redact paths before sharing if needed.");
		return builder.ToString();
	}

	private static void AddSessionTraceFiles(ZipArchive zipArchive)
	{
		if (!Directory.Exists(RuntimePaths.DebugDir))
		{
			return;
		}
		AddFileIfExists(zipArchive, Path.Combine(RuntimePaths.DebugDir, "session-trace.log"), "debug/session-trace.log");
		foreach (string file in Directory.GetFiles(RuntimePaths.DebugDir, "session-trace-*.log", SearchOption.TopDirectoryOnly)
			.OrderByDescending(File.GetLastWriteTimeUtc)
			.Take(10))
		{
			AddFileIfExists(zipArchive, file, "debug/trace-archive/" + Path.GetFileName(file));
		}
	}

	private static void AddRecentDebugFiles(ZipArchive zipArchive, string zipPath)
	{
		if (!Directory.Exists(RuntimePaths.DebugDir))
		{
			return;
		}
		string fullZipPath = Path.GetFullPath(zipPath);
		IEnumerable<string> files = Directory.GetFiles(RuntimePaths.DebugDir, "*.*", SearchOption.TopDirectoryOnly)
			.Where(delegate(string path)
			{
				string extension = Path.GetExtension(path).ToLowerInvariant();
				string fileName = Path.GetFileName(path);
				return Path.GetFullPath(path) != fullZipPath && !fileName.StartsWith("session-trace", StringComparison.OrdinalIgnoreCase) && (extension == ".log" || extension == ".txt" || extension == ".md" || extension == ".json");
			})
			.OrderByDescending(File.GetLastWriteTimeUtc)
			.Take(25);
		foreach (string file in files)
		{
			AddFileIfExists(zipArchive, file, "debug/" + Path.GetFileName(file));
		}
	}

	private static void AddDirectoryFiles(ZipArchive zipArchive, string directoryPath, string entryRoot)
	{
		if (!Directory.Exists(directoryPath))
		{
			return;
		}
		foreach (string file in Directory.GetFiles(directoryPath, "*.*", SearchOption.AllDirectories))
		{
			string relativePath = Path.GetRelativePath(directoryPath, file).Replace(Path.DirectorySeparatorChar, '/');
			AddFileIfExists(zipArchive, file, entryRoot.TrimEnd('/') + "/" + relativePath);
		}
	}

	private static void AddFileIfExists(ZipArchive zipArchive, string filePath, string entryName)
	{
		try
		{
			if (!File.Exists(filePath))
			{
				return;
			}
			ZipArchiveEntry entry = zipArchive.CreateEntry(entryName.Replace('\\', '/'), CompressionLevel.Optimal);
			using Stream entryStream = entry.Open();
			using FileStream fileStream = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
			fileStream.CopyTo(entryStream);
		}
		catch (Exception ex)
		{
			AddTextEntry(zipArchive, "errors/" + SanitizeEntryName(entryName) + ".txt", "Failed to add `" + filePath + "`: " + ex.Message);
		}
	}

	private static void AddTextEntry(ZipArchive zipArchive, string entryName, string content)
	{
		ZipArchiveEntry entry = zipArchive.CreateEntry(entryName.Replace('\\', '/'), CompressionLevel.Optimal);
		using StreamWriter writer = new StreamWriter(entry.Open(), Encoding.UTF8);
		writer.Write(content);
	}

	private static string SanitizeEntryName(string entryName)
	{
		foreach (char invalidFileNameChar in Path.GetInvalidFileNameChars())
		{
			entryName = entryName.Replace(invalidFileNameChar, '_');
		}
		return entryName.Replace('/', '_').Replace('\\', '_');
	}

	private static bool HasAlias(SettingDefinition definition)
	{
		return !string.IsNullOrWhiteSpace(definition.LegacyEnabledFlag) || !string.IsNullOrWhiteSpace(definition.LegacyDisabledFlag) || !string.IsNullOrWhiteSpace(definition.LegacyValueKey) || definition.RelatedLegacyKeys.Count > 0;
	}

	private static IEnumerable<string> BuildAliasParts(SettingDefinition definition)
	{
		if (!string.IsNullOrWhiteSpace(definition.LegacyEnabledFlag)) yield return "enabled=" + definition.LegacyEnabledFlag;
		if (!string.IsNullOrWhiteSpace(definition.LegacyDisabledFlag)) yield return "disabled=" + definition.LegacyDisabledFlag;
		if (!string.IsNullOrWhiteSpace(definition.LegacyValueKey)) yield return "value=" + definition.LegacyValueKey;
		foreach (string relatedLegacyKey in definition.RelatedLegacyKeys) yield return "related=" + relatedLegacyKey;
	}

	private void Import_Click(object sender, RoutedEventArgs e)
	{
		OpenFileDialog openFileDialog = new OpenFileDialog
		{
			Filter = "OpenEdge user data (*.json)|*.json",
			InitialDirectory = RuntimePaths.CompatibilityTransfersDir
		};
		if (openFileDialog.ShowDialog() == true)
		{
			compatibilityStateService.ImportTransferPackage(openFileDialog.FileName, createBackup: true);
			RefreshSummary();
			MessageBox.Show("User data imported successfully.", "OpenEdge", MessageBoxButton.OK, MessageBoxImage.Information);
		}
	}

	private void Refresh_Click(object sender, RoutedEventArgs e)
	{
		RefreshSummary();
	}

	private void Back_Click(object sender, RoutedEventArgs e)
	{
		NavigationService?.GoBack();
	}
}
