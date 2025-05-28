using System;
using System.Collections.Generic;

namespace FileSync
{
    public class RuleClass
    {
        public int RuleNum { get; set; } // To identify the rule
        public string SourcePath { get; set; }
        public string ReplicaPath { get; set; } // Renamed from targetPath for clarity with task
        public bool ReplicaAccessible { get; set; }
        public TimeSpan ElapsedTime { get; set; }
        public int NumNewFolders { get; set; }
        public int NumCopiedFiles { get; set; } // New or updated
        public int NumDeletedFiles { get; set; }
        public int NumDeletedFolders { get; set; }
        public double NumMBCopied { get; set; } // Simple sum of copied file sizes
        public List<string> LastCycleLogMessages { get; private set; } // Stores messages for this rule's last cycle for summary
        public List<string> ProcessingExceptions { get; private set; }

        public RuleClass()
        {
            LastCycleLogMessages = new List<string>();
            ProcessingExceptions = new List<string>();
            ResetStats();
        }

        public void ResetStats()
        {
            NumNewFolders = 0;
            NumCopiedFiles = 0;
            NumDeletedFiles = 0;
            NumDeletedFolders = 0;
            NumMBCopied = 0;
            ElapsedTime = TimeSpan.Zero;
            LastCycleLogMessages.Clear();
            ProcessingExceptions.Clear();
        }

        public void AddLogMessage(string message)
        {
            LastCycleLogMessages.Add(message);
        }
        public void AddException(string exceptionMessage)
        {
            ProcessingExceptions.Add(exceptionMessage);
        }
    }
}