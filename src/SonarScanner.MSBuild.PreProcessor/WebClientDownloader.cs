/*
 * SonarScanner for MSBuild
 * Copyright (C) 2016-2019 SonarSource SA
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
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using SonarScanner.MSBuild.Common;

namespace SonarScanner.MSBuild.PreProcessor
{
    public class WebClientDownloader : IDownloader
    {
        private readonly ILogger logger;
        private readonly PersistentUserAgentHttpClient client;

        // WebClient resets certain headers after each request: Accept, Connection, Content-Type, Expect, Referer, User-Agent.
        // This class keeps the User Agent across requests.
        // See https://github.com/SonarSource/sonar-scanner-msbuild/issues/459
        private class PersistentUserAgentHttpClient : HttpClient
        {
            public string UserAgent { get; private set; }

            public PersistentUserAgentHttpClient(string userAgent)
            {
                UserAgent = userAgent;
                this.DefaultRequestHeaders.Add(HttpRequestHeader.UserAgent.ToString(), userAgent);
            }
        }

        public WebClientDownloader(string userName, string password, ILogger logger)
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls;

            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));

            if (password == null)
            {
                password = "";
            }

            this.client = new PersistentUserAgentHttpClient($"ScannerMSBuild/{Utilities.ScannerVersion}");

            if (userName != null)
            {
                if (userName.Contains(':'))
                {
                    throw new ArgumentException(Resources.WCD_UserNameCannotContainColon);
                }
                if (!IsAscii(userName) || !IsAscii(password))
                {
                    throw new ArgumentException(Resources.WCD_UserNameMustBeAscii);
                }

                var credentials = string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0}:{1}", userName, password);
                credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes(credentials));
                this.client.DefaultRequestHeaders.Add(HttpRequestHeader.Authorization.ToString(), "Basic " + credentials);
            }
        }

        public string GetHeader(HttpRequestHeader header)
        {
            if(this.client.DefaultRequestHeaders.Contains(header.ToString()))
            {
                return string.Join(";", this.client.DefaultRequestHeaders.GetValues(header.ToString()));
            }

            return null;
        }

        #region IDownloaderMethods

        public async Task<Tuple<bool, string>> TryDownloadIfExists(string url)
        {
            this.logger.LogDebug(Resources.MSG_Downloading, url);
            string data = null;
            var success = await DoIgnoringMissingUrls(async () => data = await this.client.GetStringAsync(url));
            return new Tuple<bool, string>(success, data);
        }

        public async Task<bool> TryDownloadFileIfExists(string url, string targetFilePath)
        {
            this.logger.LogDebug(Resources.MSG_DownloadingFile, url, targetFilePath);
            return await DoIgnoringMissingUrls(async () =>
            {
                using(var contentStream = await this.client.GetStreamAsync(url))
                using (var fileStream = new FileStream(targetFilePath, FileMode.Create, FileAccess.Write))
                {
                    await contentStream.CopyToAsync(fileStream);
                }
            });
        }

        public async Task<string> Download(string url)
        {
            this.logger.LogDebug(Resources.MSG_Downloading, url);
            return await this.client.GetStringAsync(url);
        }

        #endregion IDownloaderMethods

        #region Private methods

        private static bool IsAscii(string s)
        {
            return !s.Any(c => c > sbyte.MaxValue);
        }

        /// <summary>
        /// Performs the specified web operation
        /// </summary>
        /// <returns>True if the operation completed successfully, false if the url could not be found.
        /// Other web failures will be thrown as exceptions.</returns>
        private static async Task<bool> DoIgnoringMissingUrls(Func<Task> op)
        {
            try
            {
                await op();
                return true;
            }
            catch (WebException e)
            {
                if (e.Response is HttpWebResponse response && response.StatusCode == HttpStatusCode.NotFound)
                {
                    return false;
                }
                throw;
            }
        }

        #endregion Private methods

        #region IDisposable implementation

        private bool disposed;

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!this.disposed && disposing && this.client != null)
            {
                this.client.Dispose();
            }

            this.disposed = true;
        }

        #endregion IDisposable implementation
    }
}
