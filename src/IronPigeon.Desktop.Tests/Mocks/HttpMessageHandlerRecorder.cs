﻿// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the Microsoft Reciprocal License (Ms-RL) license. See LICENSE file in the project root for full license information.

namespace IronPigeon.Tests.Mocks
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Globalization;
    using System.IO;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using System.Reflection;
    using System.Runtime.CompilerServices;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Validation;

    internal class HttpMessageHandlerRecorder : HttpClientHandler
    {
        internal const string TestNameMacro = "{TestName}";
        private readonly Mode mode;
        private string recordingPath;

        private HttpMessageHandlerRecorder(string recordingPath, Mode mode)
        {
            Requires.NotNullOrEmpty(recordingPath, "recordingPath");
            this.recordingPath = recordingPath;
            this.mode = mode;
        }

        private enum Mode
        {
            Live,
            Recording,
            Playback,
        }

        protected string RecordingPath
        {
            get
            {
                Verify.Operation(!this.recordingPath.Contains(TestNameMacro), "The scenario has not had its test name set yet.");
                return this.recordingPath;
            }
        }

        internal static HttpMessageHandlerRecorder CreateRecorder(Type testClass)
        {
            Requires.NotNull(testClass, nameof(testClass));
            string scenario = $"{testClass.Name}.{TestNameMacro}";

            var stack = new StackTrace(1, true);
            string testClassDirectory = null;
            foreach (var frame in stack.GetFrames())
            {
                if (frame.GetMethod().DeclaringType.IsEquivalentTo(testClass))
                {
                    testClassDirectory = Path.GetDirectoryName(frame.GetFileName());
                    break;
                }
            }

            return new HttpMessageHandlerRecorder(Path.Combine(testClassDirectory, scenario), Mode.Recording);
        }

        internal static HttpMessageHandlerRecorder CreatePlayback(Type testClass)
        {
            Requires.NotNull(testClass, nameof(testClass));
            string scenario = $"{testClass.Name}.{TestNameMacro}";

            return new HttpMessageHandlerRecorder(testClass.Namespace + "." + scenario, Mode.Playback);
        }

        internal void SetTestName([CallerMemberName] string testName = "")
        {
            Requires.NotNullOrEmpty(testName, nameof(testName));

            Verify.Operation(this.recordingPath.Contains(TestNameMacro), "The scenario doesn't have a {TestName} macro to replace.");
            this.recordingPath = this.recordingPath.Replace(TestNameMacro, testName);
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            switch (this.mode)
            {
                case Mode.Live:
                    return base.SendAsync(request, cancellationToken);
                case Mode.Recording:
                    return this.RecordSendAsync(request, cancellationToken);
                case Mode.Playback:
                    return this.PlaybackSendAsync(request, cancellationToken);
                default:
                    throw Assumes.NotReachable();
            }
        }

        private void GetRecordedFileNames(HttpRequestMessage request, out string headerFile, out string bodyFile)
        {
            Requires.NotNull(request, "request");

            string persistedUri = request.Method + " " + request.RequestUri.GetComponents(UriComponents.AbsoluteUri & ~UriComponents.Fragment, UriFormat.Unescaped);

            if (this.mode == Mode.Recording)
            {
                headerFile = Path.Combine(this.RecordingPath, Uri.EscapeDataString(persistedUri + ".headers"));
                bodyFile = Path.Combine(this.RecordingPath, Uri.EscapeDataString(persistedUri + ".body"));
            }
            else
            {
                headerFile = this.RecordingPath + "." + persistedUri + ".headers";
                bodyFile = this.RecordingPath + "." + persistedUri + ".body";
            }
        }

        private async Task<HttpResponseMessage> RecordSendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = await base.SendAsync(request, cancellationToken);

            // Record the request and response.
            string headerFile, bodyFile;
            this.GetRecordedFileNames(request, out headerFile, out bodyFile);
            Directory.CreateDirectory(Path.GetDirectoryName(headerFile));
            using (var file = File.Open(headerFile, FileMode.Create, FileAccess.Write))
            {
                using (var writer = new StreamWriter(file))
                {
                    await writer.WriteLineAsync(response.StatusCode.ToString());
                    foreach (var header in response.Headers)
                    {
                        await writer.WriteLineAsync(string.Format(CultureInfo.InvariantCulture, "{0}:{1}", header.Key, string.Join("\t", header.Value)));
                    }
                }
            }

            if (response.Content != null)
            {
                using (var file = File.Open(bodyFile, FileMode.Create, FileAccess.Write))
                {
                    var contentCopy = new MemoryStream();
                    await response.Content.CopyToAsync(contentCopy);
                    contentCopy.Position = 0;
                    await contentCopy.CopyToAsync(file);
                    contentCopy.Position = 0;

                    response.Content = new StreamContent(contentCopy);
                }
            }

            return response;
        }

        private async Task<HttpResponseMessage> PlaybackSendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string headerFile, bodyFile;
            this.GetRecordedFileNames(request, out headerFile, out bodyFile);

            // Record the request and response.
            var response = new HttpResponseMessage();
            using (var file = Assembly.GetExecutingAssembly().GetManifestResourceStream(headerFile))
            {
                Assumes.NotNull(file);
                var reader = new StreamReader(file);
                response.StatusCode = (HttpStatusCode)Enum.Parse(typeof(HttpStatusCode), await reader.ReadLineAsync());
                string line;
                while ((line = await reader.ReadLineAsync()) != null)
                {
                    var parts = line.Split(new[] { ':' }, 2);
                    response.Headers.Add(parts[0], parts[1].Split('\t'));
                }
            }

            using (var file = Assembly.GetExecutingAssembly().GetManifestResourceStream(bodyFile))
            {
                if (file != null)
                {
                    var contentCopy = new MemoryStream();
                    await file.CopyToAsync(contentCopy);
                    contentCopy.Position = 0;
                    response.Content = new StreamContent(contentCopy);
                }
            }

            return response;
        }
    }
}
