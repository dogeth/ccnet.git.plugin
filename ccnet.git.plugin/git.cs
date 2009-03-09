using System;
using System.Collections.Generic;
using System.Collections;
using System.IO;
using System.Net;

using Exortech.NetReflector;
using ThoughtWorks.CruiseControl.Core.Util;
using System.Text;
using System.Text.RegularExpressions;

namespace ThoughtWorks.CruiseControl.Core.Sourcecontrol
{
    /// <summary>
    ///   Source Control Plugin for CruiseControl.NET that talks to git.
    /// </summary>
    [ReflectorType("git")]
    public class git : ProcessSourceControl
    {
        public const string HistoryFormat = @"""<Modification><Type>Commit %H</Type><ModifiedTime>%ci</ModifiedTime><UserName>%cN</UserName><EmailAddress>%ce</EmailAddress><Comment>%s</Comment></Modification>""";

        private readonly IFileSystem _fileSystem;

        [ReflectorProperty("autoGetSource", Required = false)]
        public bool AutoGetSource = true;

        [ReflectorProperty("executable", Required = false)]
        public string Executable = "git";

        [ReflectorProperty("repository", Required = true)]
        public string Repository;

        [ReflectorProperty("branch", Required = false)]
        public string Branch = "master";

        [ReflectorProperty("tagCommitMessage", Required = false)]
        public string TagCommitMessage = "ccnet build {0}";

        [ReflectorProperty("tagOnSuccess", Required = false)]
        public bool TagOnSuccess = false;

        [ReflectorProperty("workingDirectory", Required = false)]
        public string WorkingDirectory;

        public git() : this(new gitHistoryParser(), new ProcessExecutor(), new SystemIoFileSystem()) { }

        public git(IHistoryParser historyParser, ProcessExecutor executor, IFileSystem fileSystem)
            : base(historyParser, executor)
        {
            _fileSystem = fileSystem;
        }

        public override Modification[] GetModifications(IIntegrationResult from, IIntegrationResult to)
        {
            if (!Fetch(to) && git_log_origin_hash(to) == git_log_local_hash(to)) return new Modification[0];

            return ParseModifications(git_log_history(to), from.StartTime, to.StartTime);
        }

        public override void GetSource(IIntegrationResult result)
        {
            if (!AutoGetSource) return;
            git_clean(result);
            if (git_log_local_hash(result) != null) git_reset(result);
            git_merge(result);
        }

        public override void LabelSourceControl(IIntegrationResult result)
        {
            if (!TagOnSuccess || result.Failed) return;
            git_tag(result);
            git_push_tags(result);
        }

        private string BaseWorkingDirectory(IIntegrationResult result)
        {
            return result.BaseFromWorkingDirectory(WorkingDirectory);
        }

        /// <summary>
        /// Fetches a git repository.  
        /// 
        /// If the working directory doesn't exist then a 'git clone' is issued to 
        /// initialize the local repo and fetch changes from the remote repo.
        /// 
        /// Else if the .git directory doesn't exist then 'git init' initializes 
        /// the working directory, 'git config' sets up the required configuration 
        /// properties, and a 'git fetch' is issued to fetch changes from the remote 
        /// repo.
        /// 
        /// Else if the working directory is already a git repository then a 'git fetch'
        /// is issued to fetch changes from the remote repo.
        /// </summary>
        /// <returns>
        /// Returns true if we needed to create the local repository
        /// </returns>
        private bool Fetch(IIntegrationResult result)
        {
            string wd = BaseWorkingDirectory(result);
            bool first = false;

            if (_fileSystem.DirectoryExists(wd))
            {
                if (!_fileSystem.DirectoryExists(Path.Combine(wd, ".git")))
                {
                    // Initialise the existing directory 
                    git_init(result);

                    // Set config options 
                    git_config("remote.origin.url", Repository, result);
                    git_config("remote.origin.fetch", "+refs/heads/*:refs/remotes/origin/*", result);
                    git_config(string.Format("branch.{0}.remote", Branch), "origin", result);
                    git_config(string.Format("branch.{0}.merge", Branch), string.Format("refs/heads/{0}", Branch), result);

                    first = true;
                }

                // Fetch changes from the remote repository
                git_fetch(result);
            }
            else
            {
                // Cloning will setup the working directory as a git repository and do a fetch for us
                git_clone(result);
                first = true;
            }
            return first;
        }

        private ProcessInfo NewProcessInfo(string args, string dir)
        {
            Log.Info("Calling git " + args);
            ProcessInfo processInfo = new ProcessInfo(Executable, args, dir);
            processInfo.StreamEncoding = Encoding.UTF8;
            return processInfo;
        }

        #region "git commands"

        /// <summary>
        /// Get the hash of the latest commit in the remote repository
        /// </summary>
        private string git_log_origin_hash(IIntegrationResult result)
        {
            ProcessArgumentBuilder buffer = new ProcessArgumentBuilder();
            buffer.AddArgument("log");
            buffer.AddArgument("origin/master");
            buffer.AddArgument("--date-order");
            buffer.AddArgument("-1");
            buffer.AddArgument("--pretty=format:'%H'");
            return Execute(NewProcessInfo(buffer.ToString(), BaseWorkingDirectory(result))).StandardOutput.Trim();
        }

        /// <summary>
        /// Get the hash of the latest commit in the local repository
        /// </summary>
        private string git_log_local_hash(IIntegrationResult result)
        {
            ProcessArgumentBuilder buffer = new ProcessArgumentBuilder();
            buffer.AddArgument("log");
            buffer.AddArgument("--date-order");
            buffer.AddArgument("-1");
            buffer.AddArgument("--pretty=format:'%H'");

            string hash = null;
            try
            {
                hash = Execute(NewProcessInfo(buffer.ToString(), BaseWorkingDirectory(result))).StandardOutput.Trim();
            }
            catch (CruiseControlException ex)
            {
                if (!ex.Message.Contains("fatal: bad default revision 'HEAD'")) throw;
            }
            return hash;
        }

        /// <summary>
        /// Get a list of all commits in date order.  The position of each commit in the list is used as the ChangeNumber.
        /// </summary>
        private ProcessResult git_log_history(IIntegrationResult result)
        {
            ProcessArgumentBuilder buffer = new ProcessArgumentBuilder();
            buffer.AddArgument("log");
            buffer.AddArgument("origin/master");
            buffer.AddArgument("--date-order");
            buffer.AddArgument("--reverse");
            buffer.AddArgument(string.Format("--pretty=format:{0}", HistoryFormat));

            ProcessResult pr = Execute(NewProcessInfo(buffer.ToString(), BaseWorkingDirectory(result)));

            //Need to change dates to be valid xml dates.
            //<ModifiedTime>2009-03-02 16:10:39 +1000</ModifiedTime>
            //to
            //<ModifiedTime>2009-03-02T16:10:39+10:00</ModifiedTime>
            string parsedStandardOutput = Regex.Replace(pr.StandardOutput, @"<ModifiedTime>(\d{4}-\d\d-\d\d)\s(\d\d:\d\d:\d\d)\s(\+|-)(\d\d)(\d\d)</ModifiedTime>", "<ModifiedTime>$1T$2$3$4:$5</ModifiedTime>");

            return new ProcessResult(parsedStandardOutput, pr.StandardError, pr.ExitCode, pr.TimedOut, pr.Failed);

        }

        private void git_clone(IIntegrationResult result)
        {
            ProcessArgumentBuilder buffer = new ProcessArgumentBuilder();
            buffer.AddArgument("clone");
            buffer.AddArgument(Repository);
            buffer.AddArgument(BaseWorkingDirectory(result));
            Execute(NewProcessInfo(buffer.ToString(), BaseWorkingDirectory(result)));
        }

        private void git_init(IIntegrationResult result)
        {
            ProcessArgumentBuilder buffer = new ProcessArgumentBuilder();
            buffer.AddArgument("init");
            Execute(NewProcessInfo(buffer.ToString(), BaseWorkingDirectory(result)));
        }

        private void git_config(string name, string value, IIntegrationResult result)
        {
            ProcessArgumentBuilder buffer = new ProcessArgumentBuilder();
            buffer.AddArgument("config");
            buffer.AddArgument(name);
            buffer.AddArgument(value);
            Execute(NewProcessInfo(buffer.ToString(), BaseWorkingDirectory(result)));
        }

        private void git_fetch(IIntegrationResult result)
        {
            ProcessArgumentBuilder buffer = new ProcessArgumentBuilder();
            buffer.AddArgument("fetch");
            Execute(NewProcessInfo(buffer.ToString(), BaseWorkingDirectory(result)));
        }

        private void git_tag(IIntegrationResult result)
        {
            ProcessArgumentBuilder buffer = new ProcessArgumentBuilder();
            buffer.AddArgument("tag");
            buffer.AddArgument("-a");
            buffer.AddArgument("-m", string.Format(TagCommitMessage, result.Label));
            buffer.AddArgument(result.Label);
            Execute(NewProcessInfo(buffer.ToString(), BaseWorkingDirectory(result)));
        }

        private void git_clean(IIntegrationResult result)
        {
            ProcessArgumentBuilder buffer = new ProcessArgumentBuilder();
            buffer.AddArgument("clean");
            buffer.AddArgument("-d");
            buffer.AddArgument("-f");
            buffer.AddArgument("-x");
            Execute(NewProcessInfo(buffer.ToString(), BaseWorkingDirectory(result)));
        }

        private void git_reset(IIntegrationResult result)
        {
            ProcessArgumentBuilder buffer = new ProcessArgumentBuilder();
            buffer.AddArgument("reset");
            buffer.AddArgument("HEAD");
            buffer.AddArgument("--hard");
            Execute(NewProcessInfo(buffer.ToString(), BaseWorkingDirectory(result)));
        }

        private void git_merge(IIntegrationResult result)
        {
            ProcessArgumentBuilder buffer = new ProcessArgumentBuilder();
            buffer.AddArgument("merge");
            buffer.AddArgument(string.Format("origin/{0}", Branch));
            Execute(NewProcessInfo(buffer.ToString(), BaseWorkingDirectory(result)));
        }

        private void git_push_tags(IIntegrationResult result)
        {
            ProcessArgumentBuilder buffer = new ProcessArgumentBuilder();
            buffer.AddArgument("push");
            buffer.AddArgument("--tags");
            Execute(NewProcessInfo(buffer.ToString(), BaseWorkingDirectory(result)));
        }
        #endregion

    }

}
