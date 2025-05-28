using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FileSync
{
    public class RuleClass
    {
        public int ruleNum {  get; set; }
        public string sourcePath { get; set; }
        public string targetPath { get; set; }
        public bool targetAccesible { get; set; }
        public TimeSpan elapsedTime { get; set; }
        public int numNewFolders { get; set; }
        public int numNewFiles { get; set; }
        public int numOverwrittenFiles { get; set; }
        public double numMBCopied { get; set; }
        public int targetFilesNewer { get; set; }
        public List<string> exceptions { get; set; }
        public List<string> newFolderPaths { get; set; }
        public List<string> newFilePaths { get; set; }

        public RuleClass()
        {
            numNewFolders = 0;
            numNewFiles = 0;
            numOverwrittenFiles = 0;

            exceptions = new List<string>();
            newFolderPaths = new List<string>();
            newFilePaths = new List<string>();
        }

    }
}
