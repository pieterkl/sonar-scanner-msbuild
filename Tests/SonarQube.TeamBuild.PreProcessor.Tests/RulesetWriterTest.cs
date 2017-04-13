﻿/*
 * SonarQube Scanner for MSBuild
 * Copyright (C) 2016-2017 SonarSource SA
 * mailto:info AT sonarsource DOT com
 *
 * This program is free software; you can redistribute it and/or
 * modify it under the terms of the GNU Lesser General Public
 * License as published by the Free Software Foundation; either
 * version 3 of the License, or (at your option) any later version.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the GNU
 * Lesser General Public License for more details.
 *
 * You should have received a copy of the GNU Lesser General Public License
 * along with this program; if not, write to the Free Software Foundation,
 * Inc., 51 Franklin Street, Fifth Floor, Boston, MA  02110-1301, USA.
 */
 
using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Text;

namespace SonarQube.TeamBuild.PreProcessor.UnitTests
{
    [TestClass]
    public class RulesetWriterTest
    {
        [TestMethod]
        public void RulesetWriterShouldFailToTwiceSeveralTimesIdenticalCheckId()
        {
            List<string> ids = new List<string>();
            ids.Add("CA1000");
            ids.Add("CA1000");
            ids.Add("CA1001");
            ids.Add("CA1002");
            ids.Add("CA1002");
            ids.Add("CA1002");

            try
            {
                RulesetWriter.ToString(ids);
            }
            catch (ArgumentException e)
            {
                if ("The following CheckId should not appear multiple times: CA1000, CA1002".Equals(e.Message))
                {
                    return;
                }
            }

            Assert.Fail();
        }

        [TestMethod]
        public void RulesetWriterToString()
        {
            List<string> ids = new List<string>();
            ids.Add("CA1000");
            ids.Add("MyCustomCheckId");

            string actual = RulesetWriter.ToString(ids);

            StringBuilder expected = new StringBuilder();
            expected.AppendLine("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
            expected.AppendLine("<RuleSet Name=\"SonarQube\" Description=\"Rule set generated by SonarQube\" ToolsVersion=\"12.0\">");
            expected.AppendLine("  <Rules AnalyzerId=\"Microsoft.Analyzers.ManagedCodeAnalysis\" RuleNamespace=\"Microsoft.Rules.Managed\">");
            expected.AppendLine("    <Rule Id=\"CA1000\" Action=\"Warning\" />");
            expected.AppendLine("    <Rule Id=\"MyCustomCheckId\" Action=\"Warning\" />");
            expected.AppendLine("  </Rules>");
            expected.AppendLine("</RuleSet>");

            Assert.AreEqual(expected.ToString(), actual);
        }
    }
}
