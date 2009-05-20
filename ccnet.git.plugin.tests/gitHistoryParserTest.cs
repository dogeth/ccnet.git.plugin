using System;
using System.IO;
using NUnit.Framework;
using ThoughtWorks.CruiseControl.Core;
using ThoughtWorks.CruiseControl.Core.Sourcecontrol;

namespace ccnet.git.plugin.tests
{
    [TestFixture]
    public class gitHistoryParserTest
    {
        string emptyLogXml = string.Empty;

        string oneEntryLogXml = @"<Modification>
            <ModifiedTime>2009-01-01T10:00:00+10:00</ModifiedTime>
            <UserName>Fred</UserName>
            <EmailAddress>abc@abc.com</EmailAddress>
            <Comment>Commit Message 1</Comment>
        </Modification>";

        string multipleEntriesLogXml =
        @"<Modification>
            <ModifiedTime>2000-01-01T10:00:00+10:00</ModifiedTime>
            <UserName>Fred</UserName>
            <EmailAddress>abc@abc.com</EmailAddress>
            <Comment>Commit Message 1</Comment>
        </Modification>
        <Modification>
            <ModifiedTime>2001-01-01T10:00:00+10:00</ModifiedTime>
            <UserName>Fred</UserName>
            <EmailAddress>abc@abc.com</EmailAddress>
            <Comment>Commit Message 2</Comment>
        </Modification>
        <Modification>
            <ModifiedTime>2009-01-01T10:00:00+10:00</ModifiedTime>
            <UserName>Fred</UserName>
            <EmailAddress>abc@abc.com</EmailAddress>
            <Comment>Commit Message 3</Comment>
        </Modification>
        <Modification>
            <ModifiedTime>2009-01-02T10:00:00+10:00</ModifiedTime>
            <UserName>Fred</UserName>
            <EmailAddress>abc@abc.com</EmailAddress>
            <Comment>Commit Message 4</Comment>
        </Modification>";

        string oneEntryLogWithCDataCommentXml = @"<Modification>
            <ModifiedTime>2009-01-01T10:00:00+10:00</ModifiedTime>
            <UserName>Fred</UserName>
            <EmailAddress>abc@abc.com</EmailAddress>
            <Comment><![CDATA[Commit Message 1]]></Comment>
        </Modification>";

        private readonly gitHistoryParser git = new gitHistoryParser();

        [Test]
        public void ParsingEmptyLogProducesNoModifications()
        {
            Modification[] modifications = git.Parse(new StringReader(emptyLogXml), DateTime.Now, DateTime.Now);
            Assert.AreEqual(0, modifications.Length);
        }

        [Test]
        public void ParsingSingleLogMessageProducesOneModification()
        {
            Modification[] modifications = git.Parse(new StringReader(oneEntryLogXml), new DateTime(2009, 01, 01, 10, 00, 00), DateTime.Now);
            Assert.AreEqual(1, modifications.Length);
        }

        [Test]
        public void ParsingLotsOfEntriesOneModification()
        {
            Modification[] modifications = git.Parse(new StringReader(multipleEntriesLogXml), new DateTime(2009, 01, 01, 10, 00, 00), DateTime.Now);
            Assert.AreEqual(2, modifications.Length);
            Assert.AreEqual("Commit Message 3", modifications[0].Comment);
            Assert.AreEqual(3, modifications[0].ChangeNumber);
            Assert.AreEqual("Commit Message 4", modifications[1].Comment);
            Assert.AreEqual(4, modifications[1].ChangeNumber);

        }

        [Test, ExpectedException(typeof(CruiseControlException))]
        public void HandleInvalidXml()
        {
            git.Parse(new StringReader("><"), DateTime.Now, DateTime.Now);
        }

        [Test]
        public void ParsingSingleLogMessageWithCDataCommentProducesCorrectComment()
        {
            Modification[] modifications = git.Parse(new StringReader(oneEntryLogWithCDataCommentXml), new DateTime(2009, 01, 01, 10, 00, 00), DateTime.Now);

            Assert.AreEqual("Commit Message 1", modifications[0].Comment);
        }
    }
}
