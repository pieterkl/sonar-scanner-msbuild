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
 
using Microsoft.TeamFoundation.Build.Client;
using Microsoft.TeamFoundation.Client;
using System;

namespace SonarQube.TeamBuild.Integration
{
    /// <summary>
    /// Wrapper to help write custom build summary messages
    /// </summary>
    /// <remarks>The class will connect to TFS when the first message is written, and
    /// save all of the written messages to the server when the class is disposed</remarks>
    public class BuildSummaryLogger : IDisposable
    {
        /// <summary>
        /// The priority specifies where this summary section appears in the list of summary sections.
        /// </summary>
        private const int SectionPriority = 200;

        /// <summary>
        /// Unique id for the section
        /// </summary>
        private const string SectionName = "SonarTeamBuildSummary";

        bool disposed;

        private readonly string tfsUri;
        private readonly string buildUri;

        private TfsTeamProjectCollection teamProjectCollection;
        private IBuildDetail build;

        #region Public methods

        public BuildSummaryLogger(string tfsUri, string buildUri)
        {
            if (string.IsNullOrWhiteSpace(tfsUri))
            {
                throw new ArgumentNullException("tfsUri");
            }
            if (string.IsNullOrWhiteSpace(buildUri))
            {
                throw new ArgumentNullException("buildUri");
            }

            this.tfsUri = tfsUri;
            this.buildUri = buildUri;
        }

        /// <summary>
        /// Writes the custom build summary message
        /// </summary>
        public void WriteMessage(string message, params object[] args)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                throw new ArgumentNullException("message");
            }

            string finalMessage = message;
            if (args != null && args.Length > 0)
            {
                finalMessage = string.Format(System.Globalization.CultureInfo.CurrentCulture, message, args);
            }

            this.EnsureConnected();
            this.build.Information.AddCustomSummaryInformation(finalMessage, SectionName, Resources.SonarQubeSummarySectionHeader, SectionPriority).Save();
        }

        #endregion

        #region IDisposable interface

        public void Dispose()
        {
            this.Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!disposed && disposing && this.teamProjectCollection != null)
            {
                this.build.Save();

                this.teamProjectCollection.Dispose();
                this.teamProjectCollection = null;
                this.build = null;
            }

            this.disposed = true;
        }

        #endregion

        private void EnsureConnected()
        {
            if (this.teamProjectCollection == null)
            {
                this.teamProjectCollection = TfsTeamProjectCollectionFactory.GetTeamProjectCollection(new Uri(this.tfsUri));
                this.build = teamProjectCollection.GetService<IBuildServer>().GetBuild(new Uri(this.buildUri));
            }

        }
    }
}
