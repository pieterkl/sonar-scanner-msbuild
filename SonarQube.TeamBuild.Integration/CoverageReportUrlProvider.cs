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
using Microsoft.TeamFoundation.TestManagement.Client;
using SonarQube.Common;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;

namespace SonarQube.TeamBuild.Integration
{
    internal class CoverageReportUrlProvider : ICoverageUrlProvider
    {
        /// <summary>
        /// Length of time to spend trying to locate code coverage reports in TFS
        /// </summary>
        private const int TimeoutInMs = 20000;

        /// <summary>
        /// The time to wait between retry attempts
        /// </summary>
        private const int RetryPeriodInMs = 2000;

        /// <summary>
        /// Builds and returns the download URLs for all code coverage reports for the specified build
        /// </summary>
        public IEnumerable<string> GetCodeCoverageReportUrls(string tfsUri, string buildUri, ILogger logger)
        {
            if (string.IsNullOrWhiteSpace(tfsUri))
            {
                throw new ArgumentNullException("tfsUri");
            }
            if (string.IsNullOrWhiteSpace(buildUri))
            {
                throw new ArgumentNullException("buildUri");
            }
            if (logger == null)
            {
                throw new ArgumentNullException("logger");
            }

            List<string> urls = new List<string>();

            logger.LogDebug(Resources.URL_DIAG_ConnectingToTfs);
            using (TfsTeamProjectCollection collection = TfsTeamProjectCollectionFactory.GetTeamProjectCollection(new Uri(tfsUri)))
            {
                IBuildServer buildServer = collection.GetService<IBuildServer>();

                logger.LogDebug(Resources.URL_DIAG_FetchingBuildInfo);
                IBuildDetail build = buildServer.GetMinimalBuildDetails(new Uri(buildUri));
                string projectName = build.TeamProject;

                logger.LogDebug(Resources.URL_DIAG_FetchingCoverageReportInfo);
                ITestManagementService tcm = collection.GetService<ITestManagementService>();
                ITestManagementTeamProject testProject = tcm.GetTeamProject(projectName);

                // TODO: investigate further. It looks as if we might be requesting the coverage reports
                // before the service is able to provide them.
                // For the time being, we're retrying with a time out.
                IBuildCoverage[] coverages = null;
                Utilities.Retry(TimeoutInMs, RetryPeriodInMs, logger, () => { return TryGetCoverageInfo(testProject, buildUri, out coverages); });

                foreach (IBuildCoverage coverage in coverages)
                {
                    logger.LogDebug(Resources.URL_DIAG_CoverageReportInfo, coverage.Configuration.Id, coverage.Configuration.BuildPlatform, coverage.Configuration.BuildPlatform);

                    string coverageFileUrl = CoverageReportUrlProvider.GetCoverageUri(build, coverage);
                    Debug.WriteLine(coverageFileUrl);
                    urls.Add(coverageFileUrl);
                }
            }

            logger.LogDebug(Resources.URL_DIAG_Finished);
            return urls;
        }

        private static bool TryGetCoverageInfo(ITestManagementTeamProject testProject, string buildUri, out IBuildCoverage[] coverageInfo)
        {
            coverageInfo = testProject.CoverageAnalysisManager.QueryBuildCoverage(buildUri, CoverageQueryFlags.Modules);

            return coverageInfo != null && coverageInfo.Length > 0;
        }

        private static string GetCoverageUri(IBuildDetail buildDetail, IBuildCoverage buildCoverage)
        {
            string serverPath = string.Format(CultureInfo.InvariantCulture, "/BuildCoverage/{0}.{1}.{2}.{3}.coverage",
                                    buildDetail.BuildNumber,
                                    buildCoverage.Configuration.BuildFlavor,
                                    buildCoverage.Configuration.BuildPlatform,
                                    buildCoverage.Configuration.Id);

            string coverageFileUrl = String.Format(CultureInfo.InvariantCulture, "{0}/{1}/_api/_build/ItemContent?buildUri={2}&path={3}",
                                           buildDetail.BuildServer.TeamProjectCollection.Uri.AbsoluteUri,
                                           Uri.EscapeDataString(buildDetail.TeamProject),
                                           Uri.EscapeDataString(buildDetail.Uri.AbsoluteUri),
                                           Uri.EscapeDataString(serverPath));

            return coverageFileUrl;
        }

    }
}