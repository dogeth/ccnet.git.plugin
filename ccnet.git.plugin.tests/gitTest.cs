using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ThoughtWorks.CruiseControl.Core.Util;
using System.IO;
using NMock;
using ThoughtWorks.CruiseControl.Core.Sourcecontrol;
using ThoughtWorks.CruiseControl.UnitTests.Core;
using Exortech.NetReflector;
using ThoughtWorks.CruiseControl.Core;
using NMock.Constraints;

namespace ccnet.git.plugin.tests
{
    [TestFixture]
    public class gitTest : ProcessExecutorTestFixtureBase
    {
        const string GIT_CLONE = @"clone xyz.git c:\source\";
        const string GIT_INIT = "init";
        const string GIT_FETCH = "fetch";
        const string GIT_REMOTE_HASH = "log origin/master --date-order -1 --pretty=format:'%H'";
        const string GIT_LOCAL_HASH = "log --date-order -1 --pretty=format:'%H'";
        const string GIT_REMOTE_COMMITS = @"log origin/master --date-order --reverse --pretty=format:""<Modification><Type>Commit %H</Type><ModifiedTime>%ci</ModifiedTime><UserName>%cN</UserName><EmailAddress>%ce</EmailAddress><Comment>%s</Comment></Modification>""";
        const string GIT_CONFIG1 = @"config remote.origin.url xyz.git";
        const string GIT_CONFIG2 = "config remote.origin.fetch +refs/heads/*:refs/remotes/origin/*";
        const string GIT_CONFIG3 = "config branch.master.remote origin";
        const string GIT_CONFIG4 = "config branch.master.merge refs/heads/master";

        private class StubFileSystem:IFileSystem 
        {
            public void Copy(string sourcePath, string destPath) { }
            public void Save(string file, string content) { }
            public void AtomicSave(string file, string content) { }
            public void AtomicSave(string file, string content, Encoding encoding) { }
            public TextReader Load(string file) 
            {
                return null;
            }
            public bool FileExists(string file) 
            {
                return true;
            }
            public bool DirectoryExists(string folder) 
            {
                return true;
            }
        }
        [Test]
        public void StubFileSystemCoverage() 
        {
            StubFileSystem sf = new StubFileSystem();
            sf.Copy("asdf","asdf");
            sf.Save("asdf","Asdf");
            sf.Load("asdf");
            sf.FileExists("asdf");
            sf.DirectoryExists("asdf");
        }

        private ThoughtWorks.CruiseControl.Core.Sourcecontrol.git git;
        private IMock mockHistoryParser;
        private DateTime from;
        private DateTime to;
        private IMock mockFileSystem;

        [SetUp]
        protected void CreateGit() 
        {
            mockHistoryParser = new DynamicMock(typeof (IHistoryParser));
            mockFileSystem = new DynamicMock(typeof (IFileSystem));
            CreateProcessExecutorMock("git");
            from = new DateTime(2001, 1, 21, 20, 0, 0);
            to = from.AddDays(1);
            setupGit(new StubFileSystem());
        }

        [TearDown]
        protected void VerifyAll() 
        {
            Verify();
            mockHistoryParser.Verify();
            mockFileSystem.Verify();
        }

        [Test]
        public void gitShouldBeDefaultExecutable() 
        {
            Assert.AreEqual("git", git.Executable);
        }

        [Test]
        public void PopulateFromFullySpecifiedXml() 
        {
            string xml = @"
<git>
	<executable>git</executable>
	<repository>c:\git\ccnet\mygitrepo</repository>
	<branch>master</branch>
	<timeout>5</timeout>
	<workingDirectory>c:\git\working</workingDirectory>
	<tagOnSuccess>true</tagOnSuccess>
	<autoGetSource>true</autoGetSource>
</git>";

            git = (ThoughtWorks.CruiseControl.Core.Sourcecontrol.git)NetReflector.Read(xml);
            Assert.AreEqual(@"git", git.Executable);
            Assert.AreEqual(@"c:\git\ccnet\mygitrepo", git.Repository);
            Assert.AreEqual(@"master", git.Branch);
            Assert.AreEqual(new Timeout(5), git.Timeout);
            Assert.AreEqual(@"c:\git\working", git.WorkingDirectory);
            Assert.AreEqual(true, git.TagOnSuccess);
            Assert.AreEqual(true, git.AutoGetSource);
        }

        [Test]
        public void PopulateFromMinimallySpecifiedXml() 
        {
            string xml = @"
<git>
    <repository>c:\git\ccnet\mygitrepo</repository>
</git>";
            git = (ThoughtWorks.CruiseControl.Core.Sourcecontrol.git)NetReflector.Read(xml);
        }

        [Test]
        public void ShouldApplyLabelIfTagOnSuccessTrue() 
        {
            git.TagOnSuccess = true;

            ExpectToExecuteArguments(@"tag -a -m ""ccnet build foo"" foo");
            ExpectToExecuteArguments(@"push --tags");

            git.LabelSourceControl(IntegrationResultMother.CreateSuccessful("foo"));
        }

        [Test]
        public void ShouldApplyLabelWithCustomMessageIfTagOnSuccessTrueAndACustomMessageIsSpecified() 
        {
            git.TagOnSuccess = true;
            git.TagCommitMessage = "a---- {0} ----a";

            ExpectToExecuteArguments(@"tag -a -m ""a---- foo ----a"" foo");
            ExpectToExecuteArguments(@"push --tags");

            git.LabelSourceControl(IntegrationResultMother.CreateSuccessful("foo"));
        }

        [Test]
        public void ShouldCloneIfDirectoryDoesntExist() 
        {
            setupGit((IFileSystem)mockFileSystem.MockInstance);

            mockFileSystem.ExpectAndReturn("DirectoryExists", false, @"c:\source\");

            ExpectToExecuteArguments(GIT_CLONE);

            ExpectToExecuteArguments(GIT_REMOTE_COMMITS);

            git.GetModifications(IntegrationResult(from), IntegrationResult(to));
        }

        [Test]
        public void ShouldInitIfGitDirectoryDoesntExist()
        {
            setupGit((IFileSystem)mockFileSystem.MockInstance);

            mockFileSystem.ExpectAndReturn("DirectoryExists", true, @"c:\source\");
            mockFileSystem.ExpectAndReturn("DirectoryExists", false, @"c:\source\.git");

            ExpectToExecuteArguments(GIT_INIT);
            ExpectToExecuteArguments(GIT_CONFIG1);
            ExpectToExecuteArguments(GIT_CONFIG2);
            ExpectToExecuteArguments(GIT_CONFIG3);
            ExpectToExecuteArguments(GIT_CONFIG4);

            ExpectToExecuteArguments(GIT_FETCH);

            ExpectToExecuteArguments(GIT_REMOTE_COMMITS);

            git.GetModifications(IntegrationResult(from), IntegrationResult(to));
        }

        [Test]
        public void ShouldNotGetModificationsWhenHashsMatch()
        {
            ExpectToExecuteArguments(GIT_FETCH);

            ExpectToExecuteWithArgumentsAndReturn(GIT_REMOTE_HASH , new ProcessResult("abcdef", "", 0, false));
            ExpectToExecuteWithArgumentsAndReturn(GIT_LOCAL_HASH , new ProcessResult("abcdef", "", 0, false));

            Modification[] mods = git.GetModifications(IntegrationResult(from), IntegrationResult(to));

            Assert.AreEqual(0, mods.Length);
        }
        
        private void ExpectToExecuteWithArgumentsAndReturn(string args, ProcessResult returnValue)
        {
            mockProcessExecutor.ExpectAndReturn("Execute", returnValue, NewProcessInfo(args));
        }

        [Test]
        public void ShouldGetSourceIfModificationsFound() 
        {
            git.AutoGetSource = true;

            ExpectToExecuteArguments("clean -d -f -x");
            ExpectToExecuteWithArgumentsAndReturn(GIT_LOCAL_HASH, new ProcessResult("abcdef", "", 0, false));
            ExpectToExecuteArguments("reset HEAD --hard");
            ExpectToExecuteArguments("merge origin/master");

           git.GetSource(IntegrationResult());
        }

        [Test]
        public void ShouldNotApplyLabelIfIntegrationFailed() 
        {
            git.TagOnSuccess = true;

            ExpectThatExecuteWillNotBeCalled();

           git.LabelSourceControl(IntegrationResultMother.CreateFailed());
        }

        [Test]
        public void ShouldNotApplyLabelIfTagOnSuccessFalse() 
        {
            git.TagOnSuccess = false;

            ExpectThatExecuteWillNotBeCalled();

            git.LabelSourceControl(IntegrationResultMother.CreateSuccessful());
        }

        [Test]
        public void ShouldNotGetSourceIfAutoGetSourceFalse() 
        {
            git.AutoGetSource = false;

            ExpectThatExecuteWillNotBeCalled();

           git.GetSource(IntegrationResult());
        }

        [Test]
        public void ShouldReturnModificationsWhenHashsDifferent() 
        {
            Modification[] modifications = new Modification[2] { new Modification(), new Modification() };

            ExpectToExecuteArguments(GIT_FETCH);

            ExpectToExecuteWithArgumentsAndReturn(GIT_REMOTE_HASH, new ProcessResult("abcdef", "", 0, false));
            ExpectToExecuteWithArgumentsAndReturn(GIT_LOCAL_HASH, new ProcessResult("ghijkl", "", 0, false));

            ExpectToExecuteArguments(GIT_REMOTE_COMMITS);

            mockHistoryParser.ExpectAndReturn("Parse", modifications, new IsAnything(), from, new IsAnything());

            Modification[] result = git.GetModifications(IntegrationResult(from), IntegrationResult(to));
            Assert.AreEqual(modifications, result);
        }

        private void setupGit(IFileSystem filesystem) 
        {
            git = new ThoughtWorks.CruiseControl.Core.Sourcecontrol.git((IHistoryParser)mockHistoryParser.MockInstance, (ProcessExecutor)mockProcessExecutor.MockInstance, filesystem);
            git.Repository = @"xyz.git";
            git.WorkingDirectory = DefaultWorkingDirectory;
        }

    }
}
