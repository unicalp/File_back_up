using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
namespace FileSync
{
    internal class Program

    {
        static int numRules = 0;
        static string rulesPath = //@
        static string logPath = //
        public static List<RuleClass> rules = new List<RuleClass>();

        public static bool testRun = true;
        public static List<string> exceptions = new List<string>();

        #endregion

        static void Main(string[] args)
        {
            if (File.Exists(LogPath))
            {
                File.Delete(logPath)
            }
            if (File.Exists(summaryPath)
            {
                File.Delete(summaPath);
            }
            GetRulesFromFile();

            foreach (RuleClass rule in rules)
            {
                var srcFolder = new DirectoryInfo(rule.sourcePath);
                
                Stopwatch stopwatch = new Stopwatch();
                stopwatch.Restart();
                //
                if (rule.targetAccesible)
                {
                    UpdateTargetDirs(rule, srcFolder);
                    UpdateTargetFiles(rule, srcFolder);
                }
                stopwatch.Stop();
                rule.elapsedTime = stopwatch.Elapsed;
                SaveLogToFile(rule);
            }
            SaveSummaryToFile();

        }

    }
}