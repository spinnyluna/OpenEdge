using System;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace OpenEdge;

public static class SessionTraceLogger
{
	private const int MaxArchivedTraceLogs = 10;

	private static readonly object LogLock = new object();

	public static string LogFile => Path.Combine(RuntimePaths.DebugDir, "session-trace.log");

	public static void Reset(string reason)
	{
		try
		{
			Directory.CreateDirectory(RuntimePaths.DebugDir);
			lock (LogLock)
			{
				ArchiveCurrentLog();
				File.WriteAllText(LogFile, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " [INFO] logger - reset: " + reason + Environment.NewLine);
				PruneArchivedLogs();
			}
		}
		catch
		{
		}
	}

	public static void Info(string category, string message)
	{
		Write("INFO", category, message, null);
	}

	public static void Error(string category, string message, Exception exception = null)
	{
		Write("ERROR", category, message, exception);
	}

	public static void Memory(string category, string message = "")
	{
		long managedBytes = GC.GetTotalMemory(forceFullCollection: false);
		long privateBytes = 0;
		try
		{
			privateBytes = Process.GetCurrentProcess().PrivateMemorySize64;
		}
		catch
		{
		}
		Write("MEMORY", category, message + " managed=" + FormatBytes(managedBytes) + " private=" + FormatBytes(privateBytes), null);
	}

	private static void ArchiveCurrentLog()
	{
		if (!File.Exists(LogFile) || new FileInfo(LogFile).Length == 0)
		{
			return;
		}
		DateTime timestamp = File.GetLastWriteTime(LogFile);
		string archivePath = Path.Combine(RuntimePaths.DebugDir, "session-trace-" + timestamp.ToString("yyyyMMdd-HHmmss") + ".log");
		int suffix = 1;
		while (File.Exists(archivePath))
		{
			archivePath = Path.Combine(RuntimePaths.DebugDir, "session-trace-" + timestamp.ToString("yyyyMMdd-HHmmss") + "-" + suffix + ".log");
			suffix++;
		}
		File.Copy(LogFile, archivePath);
	}

	private static void PruneArchivedLogs()
	{
		if (!Directory.Exists(RuntimePaths.DebugDir))
		{
			return;
		}
		foreach (string archivePath in Directory.GetFiles(RuntimePaths.DebugDir, "session-trace-*.log", SearchOption.TopDirectoryOnly)
			.OrderByDescending(File.GetLastWriteTimeUtc)
			.Skip(MaxArchivedTraceLogs))
		{
			File.Delete(archivePath);
		}
	}

	private static void Write(string level, string category, string message, Exception exception)
	{
		try
		{
			Directory.CreateDirectory(RuntimePaths.DebugDir);
			string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " [" + level + "] " + category + " - " + message;
			if (exception != null)
			{
				line += " | " + exception.GetType().Name + ": " + exception.Message;
			}
			lock (LogLock)
			{
				File.AppendAllText(LogFile, line + Environment.NewLine);
			}
		}
		catch
		{
		}
	}

	private static string FormatBytes(long bytes)
	{
		if (bytes <= 0)
		{
			return "0 MB";
		}
		return Math.Round(bytes / 1024.0 / 1024.0, 1) + " MB";
	}
}
