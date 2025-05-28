using FileSync;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;

namespace FileSync
{
    internal class Program
    {
        private static string _rulesFilePath;
        private static int _intervalSeconds;
        private static string _overallLogFilePath; // Main log file for all detailed operations

        private static List<RuleClass> _syncRules = new List<RuleClass>();
        private static Timer _timer;
        private static readonly object _logLock = new object();
        private static readonly object _syncCycleLock = new object(); // To prevent overlapping full sync cycles

        private const char RulesFileCommentChar = '#';
        private const char RulesFileSeparator = ',';

        static void Main(string[] args)
        {
            if (args.Length < 3)
            {
                Console.WriteLine("Usage: FolderSync.exe <RulesFilePath> <IntervalSeconds> <OverallLogFilePath>");
                LogToConsoleAndFile("Error: Insufficient command line arguments. Exiting.", "startup_error_log.txt");
                return;
            }

            _rulesFilePath = args[0];
            if (!int.TryParse(args[1], out _intervalSeconds) || _intervalSeconds <= 0)
            {
                LogToConsoleAndFile("Error: Invalid interval. Must be a positive integer (seconds). Exiting.", args.Length > 2 ? args[2] : "startup_error_log.txt");
                return;
            }
            _overallLogFilePath = args[2];

            try
            {
                string logDir = Path.GetDirectoryName(_overallLogFilePath);
                if (!string.IsNullOrEmpty(logDir) && !Directory.Exists(logDir))
                {
                    Directory.CreateDirectory(logDir);
                }
                LogToConsoleAndFile($"Program started. Overall Log: {_overallLogFilePath}", _overallLogFilePath);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"CRITICAL: Could not initialize overall log file '{_overallLogFilePath}'. {ex.Message}");
                return;
            }

            LogToConsoleAndFile($"Rules File Path: {_rulesFilePath}", _overallLogFilePath);
            LogToConsoleAndFile($"Sync Interval: {_intervalSeconds} seconds", _overallLogFilePath);

            if (!LoadRulesFromFile())
            {
                LogToConsoleAndFile("Error loading rules. Exiting.", _overallLogFilePath);
                return;
            }

            if (!_syncRules.Any())
            {
                LogToConsoleAndFile("No valid synchronization rules found. Exiting.", _overallLogFilePath);
                return;
            }

            _timer = new Timer(ScheduledSyncCycle, null, TimeSpan.Zero, TimeSpan.FromSeconds(_intervalSeconds));

            LogToConsoleAndFile("Synchronization service started. Press 'Q' to quit.", _overallLogFilePath);
            Console.WriteLine("Synchronization service running... Press 'Q' to quit.");

            while (Console.ReadKey(true).Key != ConsoleKey.Q) { /* Keep alive */ }

            _timer.Dispose();
            LogToConsoleAndFile("Program shutting down.", _overallLogFilePath);
        }

        private static bool LoadRulesFromFile()
        {
            LogToConsoleAndFile($"Attempting to load rules from: {_rulesFilePath}", _overallLogFilePath);
            if (!File.Exists(_rulesFilePath))
            {
                LogToConsoleAndFile($"Error: Rules file not found at '{_rulesFilePath}'.", _overallLogFilePath);
                return false;
            }

            _syncRules.Clear();
            int ruleCounter = 0;
            try
            {
                string[] lines = File.ReadAllLines(_rulesFilePath);
                foreach (string line in lines)
                {
                    if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith(RulesFileCommentChar))
                        continue;

                    string[] parts = line.Split(RulesFileSeparator);
                    if (parts.Length >= 2)
                    {
                        ruleCounter++;
                        var rule = new RuleClass
                        {
                            RuleNum = ruleCounter,
                            SourcePath = parts[0].Trim(),
                            ReplicaPath = parts[1].Trim()
                        };

                        if (!Directory.Exists(rule.SourcePath))
                        {
                            LogToConsoleAndFile($"Warning for Rule {rule.RuleNum}: Source path '{rule.SourcePath}' does not exist. Rule will be skipped until path is valid.", _overallLogFilePath);
                            rule.ReplicaAccessible = false; // Mark as inaccessible for now
                        }
                        else
                        {
                            CheckReplicaAccessible(rule); // Checks and attempts to create replica base
                        }
                        _syncRules.Add(rule);
                        LogToConsoleAndFile($"Loaded Rule {rule.RuleNum}: Source='{rule.SourcePath}', Replica='{rule.ReplicaPath}', ReplicaAccessible={rule.ReplicaAccessible}", _overallLogFilePath);
                    }
                    else
                    {
                        LogToConsoleAndFile($"Warning: Skipping invalid rule line: '{line}'", _overallLogFilePath);
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                LogToConsoleAndFile($"Error reading rules file: {ex.Message}", _overallLogFilePath);
                return false;
            }
        }

        private static void CheckReplicaAccessible(RuleClass rule)
        {
            try
            {
                if (!Directory.Exists(rule.ReplicaPath))
                {
                    Directory.CreateDirectory(rule.ReplicaPath);
                    LogOperation(rule, $"CREATED Base Replica Directory: '{rule.ReplicaPath}' (for Rule {rule.RuleNum})");
                }
                rule.ReplicaAccessible = true;
            }
            catch (Exception ex)
            {
                LogToConsoleAndFile($"Error for Rule {rule.RuleNum}: Replica path '{rule.ReplicaPath}' is not accessible or couldn't be created. {ex.Message}", _overallLogFilePath);
                rule.AddException($"Replica path '{rule.ReplicaPath}' is not accessible or couldn't be created. {ex.Message}");
                rule.ReplicaAccessible = false;
            }
        }

        private static void ScheduledSyncCycle(object state)
        {
            if (!Monitor.TryEnter(_syncCycleLock))
            {
                LogToConsoleAndFile("Synchronization cycle skipped: Previous full cycle still in progress.", _overallLogFilePath);
                return;
            }

            try
            {
                LogToConsoleAndFile("Starting new synchronization cycle for all rules...", _overallLogFilePath);
                foreach (RuleClass rule in _syncRules)
                {
                    rule.ResetStats(); // Reset stats for the current cycle

                    // Re-check source path validity in case it was unavailable before
                    if (!Directory.Exists(rule.SourcePath))
                    {
                        string msg = $"Source path '{rule.SourcePath}' for Rule {rule.RuleNum} is not accessible. Skipping this rule for the current cycle.";
                        LogToConsoleAndFile(msg, _overallLogFilePath);
                        rule.AddLogMessage(msg);
                        rule.AddException(msg);
                        SaveRuleCycleSummary(rule); // Save summary even if skipped
                        continue;
                    }
                    // Re-check/ensure replica path accessibility
                    CheckReplicaAccessible(rule);
                    if (!rule.ReplicaAccessible)
                    {
                        string msg = $"Replica path '{rule.ReplicaPath}' for Rule {rule.RuleNum} is not accessible. Skipping this rule for the current cycle.";
                        LogToConsoleAndFile(msg, _overallLogFilePath);
                        // Exception already added by CheckReplicaAccessible
                        rule.AddLogMessage(msg);
                        SaveRuleCycleSummary(rule); // Save summary even if skipped
                        continue;
                    }


                    LogToConsoleAndFile($"Processing Rule {rule.RuleNum}: '{rule.SourcePath}' -> '{rule.ReplicaPath}'", _overallLogFilePath);
                    Stopwatch stopwatch = Stopwatch.StartNew();

                    try
                    {
                        ProcessRuleDirectoryRecursively(rule, new DirectoryInfo(rule.SourcePath), rule.ReplicaPath);
                    }
                    catch (Exception ex)
                    {
                        string errorMsg = $"CRITICAL ERROR during sync for Rule {rule.RuleNum}: {ex.Message}";
                        LogToConsoleAndFile(errorMsg, _overallLogFilePath);
                        rule.AddException(errorMsg + Environment.NewLine + ex.StackTrace);
                    }

                    stopwatch.Stop();
                    rule.ElapsedTime = stopwatch.Elapsed;
                    SaveRuleCycleSummary(rule);
                }
                LogToConsoleAndFile("All rules processed for this synchronization cycle.", _overallLogFilePath);
            }
            catch (Exception ex) // Catchall for unexpected errors in the cycle scheduler
            {
                LogToConsoleAndFile($"FATAL ERROR in ScheduledSyncCycle: {ex.Message}{Environment.NewLine}{ex.StackTrace}", _overallLogFilePath);
            }
            finally
            {
                Monitor.Exit(_syncCycleLock);
            }
        }

        private static void ProcessRuleDirectoryRecursively(RuleClass rule, DirectoryInfo sourceDir, string currentReplicaDirPath)
        {
            // 1. Ensure current replica directory exists (already done by CheckReplicaAccessible for base, this handles subdirs)
            if (!Directory.Exists(currentReplicaDirPath))
            {
                try
                {
                    Directory.CreateDirectory(currentReplicaDirPath);
                    LogOperation(rule, $"CREATED Directory: '{currentReplicaDirPath}'");
                    rule.NumNewFolders++;
                }
                catch (Exception ex)
                {
                    string msg = $"ERROR creating directory '{currentReplicaDirPath}': {ex.Message}";
                    LogOperation(rule, msg); rule.AddException(msg); return;
                }
            }

            // 2. Synchronize files: Source to Replica (Create/Update)
            try
            {
                foreach (FileInfo sourceFile in sourceDir.EnumerateFiles())
                {
                    string replicaFilePath = Path.Combine(currentReplicaDirPath, sourceFile.Name);
                    try
                    {
                        bool needsCopy = false;
                        if (!File.Exists(replicaFilePath))
                        {
                            needsCopy = true;
                        }
                        else
                        {
                            FileInfo replicaFileInfo = new FileInfo(replicaFilePath);
                            if (sourceFile.LastWriteTimeUtc > replicaFileInfo.LastWriteTimeUtc || sourceFile.Length != replicaFileInfo.Length)
                            {
                                needsCopy = true;
                            }
                        }

                        if (needsCopy)
                        {
                            File.Copy(sourceFile.FullName, replicaFilePath, true);
                            LogOperation(rule, $"COPIED/UPDATED File: '{sourceFile.FullName}' to '{replicaFilePath}'");
                            rule.NumCopiedFiles++;
                            rule.NumMBCopied += sourceFile.Length / (1024.0 * 1024.0);
                        }
                    }
                    catch (Exception ex)
                    {
                        string msg = $"ERROR copying/updating file '{sourceFile.FullName}' to '{replicaFilePath}': {ex.Message}";
                        LogOperation(rule, msg); rule.AddException(msg);
                    }
                }
            }
            catch (Exception ex)
            {
                string msg = $"ERROR enumerating files in source directory '{sourceDir.FullName}': {ex.Message}";
                LogOperation(rule, msg); rule.AddException(msg);
            }

            // 3. Recursively synchronize subdirectories
            try
            {
                foreach (DirectoryInfo sourceSubDir in sourceDir.EnumerateDirectories())
                {
                    string replicaSubDirPath = Path.Combine(currentReplicaDirPath, sourceSubDir.Name);
                    ProcessRuleDirectoryRecursively(rule, sourceSubDir, replicaSubDirPath);
                }
            }
            catch (Exception ex)
            {
                string msg = $"ERROR enumerating subdirectories in source directory '{sourceDir.FullName}': {ex.Message}";
                LogOperation(rule, msg); rule.AddException(msg);
            }


            // 4. Delete extraneous files in the current replica directory
            try
            {
                DirectoryInfo replicaDirInfo = new DirectoryInfo(currentReplicaDirPath);
                foreach (FileInfo replicaFile in replicaDirInfo.EnumerateFiles())
                {
                    string correspondingSourceFilePath = Path.Combine(sourceDir.FullName, replicaFile.Name);
                    if (!File.Exists(correspondingSourceFilePath))
                    {
                        try
                        {
                            replicaFile.Delete();
                            LogOperation(rule, $"DELETED File: '{replicaFile.FullName}'");
                            rule.NumDeletedFiles++;
                        }
                        catch (Exception ex)
                        {
                            string msg = $"ERROR deleting file '{replicaFile.FullName}': {ex.Message}";
                            LogOperation(rule, msg); rule.AddException(msg);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                string msg = $"ERROR enumerating files in replica directory '{currentReplicaDirPath}' for deletion: {ex.Message}";
                LogOperation(rule, msg); rule.AddException(msg);
            }

            // 5. Delete extraneous subdirectories in the current replica directory
            try
            {
                DirectoryInfo replicaDirInfo = new DirectoryInfo(currentReplicaDirPath);
                foreach (DirectoryInfo replicaSubDir in replicaDirInfo.EnumerateDirectories())
                {
                    string correspondingSourceSubDirPath = Path.Combine(sourceDir.FullName, replicaSubDir.Name);
                    if (!Directory.Exists(correspondingSourceSubDirPath))
                    {
                        try
                        {
                            Directory.Delete(replicaSubDir.FullName, true); // Recursive delete
                            LogOperation(rule, $"DELETED Directory (and contents): '{replicaSubDir.FullName}'");
                            rule.NumDeletedFolders++; // Note: this counts only the top-level deleted folder
                        }
                        catch (Exception ex)
                        {
                            string msg = $"ERROR deleting directory '{replicaSubDir.FullName}': {ex.Message}";
                            LogOperation(rule, msg); rule.AddException(msg);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                string msg = $"ERROR enumerating subdirectories in replica directory '{currentReplicaDirPath}' for deletion: {ex.Message}";
                LogOperation(rule, msg); rule.AddException(msg);
            }
        }

        // Logs an individual operation for a specific rule to the console and the overall log file.
        // Also adds it to the rule's LastCycleLogMessages for its own summary.
        private static void LogOperation(RuleClass rule, string message)
        {
            string fullMessage = $"Rule {rule.RuleNum}: {message}";
            LogToConsoleAndFile(fullMessage, _overallLogFilePath);
            rule.AddLogMessage(message); // Add original message without "Rule X" prefix
        }

        // Appends to the overall log file and writes to console.
        public static void LogToConsoleAndFile(string message, string filePath)
        {
            lock (_logLock)
            {
                try
                {
                    string timestampedMessage = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} - {message}";
                    Console.WriteLine(timestampedMessage);
                    if (!string.IsNullOrEmpty(filePath)) // Allows for early logging before _overallLogFilePath is set
                    {
                        File.AppendAllText(filePath, timestampedMessage + Environment.NewLine);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} - LOGGING_ERROR to '{filePath}': {ex.Message} (Original: {message})");
                }
            }
        }

        // Saves a summary of a single rule's last cycle to the overall log file.
        private static void SaveRuleCycleSummary(RuleClass rule)
        {
            // This method will append a summary of the rule's activity to the main log file.
            // You could also direct this to a separate summary file if desired.
            var summary = new System.Text.StringBuilder();
            summary.AppendLine($"--- Rule {rule.RuleNum} Sync Cycle Summary ---");
            summary.AppendLine($"  Source: {rule.SourcePath}");
            summary.AppendLine($"  Replica: {rule.ReplicaPath}");
            summary.AppendLine($"  Duration: {rule.ElapsedTime.TotalSeconds:F2} seconds");
            summary.AppendLine($"  Folders Created: {rule.NumNewFolders}");
            summary.AppendLine($"  Files Copied/Updated: {rule.NumCopiedFiles}");
            summary.AppendLine($"  Data Copied: {rule.NumMBCopied:F2} MB");
            summary.AppendLine($"  Files Deleted in Replica: {rule.NumDeletedFiles}");
            summary.AppendLine($"  Folders Deleted in Replica: {rule.NumDeletedFolders}");

            if (rule.ProcessingExceptions.Any())
            {
                summary.AppendLine($"  Processing Exceptions ({rule.ProcessingExceptions.Count}):");
                foreach (string exMsg in rule.ProcessingExceptions.Take(5)) // Log first 5 exceptions
                {
                    summary.AppendLine($"    - {exMsg.Split(new[] { Environment.NewLine }, StringSplitOptions.None)[0]}"); // First line of ex
                }
                if (rule.ProcessingExceptions.Count > 5) summary.AppendLine("    - ... (more exceptions not listed in summary)");
            }
            else
            {
                summary.AppendLine("  Status: Completed successfully.");
            }
            summary.AppendLine($"--- End Rule {rule.RuleNum} Summary ---");

            LogToConsoleAndFile(summary.ToString(), _overallLogFilePath);
        }
    }
}