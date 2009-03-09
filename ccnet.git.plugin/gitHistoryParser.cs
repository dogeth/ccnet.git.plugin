using System;
using System.IO;
using Exortech.NetReflector;
using System.Collections;
using System.Xml.Serialization;

namespace ThoughtWorks.CruiseControl.Core.Sourcecontrol
{
    public class gitHistoryParser : IHistoryParser
    {
        /// <summary>
        /// Parse and filter the supplied modifications.  The position of each modification in the list is used as the ChangeNumber.
        /// </summary>
        /// <param name="history"></param>
        /// <param name="from"></param>
        /// <param name="to"></param>
        /// <returns></returns>
        public Modification[] Parse(TextReader history, DateTime from, DateTime to)
        {
            StringReader sr = new StringReader(string.Format(@"<ArrayOfModification xmlns:xsi=""http://www.w3.org/2001/XMLSchema-instance"" xmlns:xsd=""http://www.w3.org/2001/XMLSchema"">{0}</ArrayOfModification>"
                , history.ReadToEnd()));

            XmlSerializer serializer = new XmlSerializer(typeof(Modification[]));
            Modification[] mods;
            try
            {
                mods = (Modification[])serializer.Deserialize(sr);
            }
            catch (Exception ex)
            {
                throw new CruiseControlException("History Parsing Failed", ex);
            }

            ArrayList results = new ArrayList();
            int change = 0;
            foreach (Modification mod in mods)
            {
                change++;
                mod.ChangeNumber = change;
                if ((mod.ModifiedTime >= from) & (mod.ModifiedTime <= to))
                {
                    results.Add(mod);
                }
            }
            return (Modification[])results.ToArray(typeof(Modification));
        }
    }
}
