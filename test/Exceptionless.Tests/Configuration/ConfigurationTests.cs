﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Exceptionless.Configuration;
using Exceptionless.Dependency;
using Exceptionless.Models;
using Exceptionless.Storage;
using Exceptionless.Submission;
using Exceptionless.Tests.Utility;
using Moq;
using Xunit;
using Xunit.Abstractions;

[assembly: Exceptionless("LhhP1C9gijpSKCslHHCvwdSIz298twx271n1l6xw", ServerUrl = "http://localhost:45000")]
[assembly: ExceptionlessSetting("testing", "configuration")]
namespace Exceptionless.Tests.Configuration {
    public class ConfigurationTests {
        private readonly TestOutputWriter _writer;
        public ConfigurationTests(ITestOutputHelper output) {
            _writer = new TestOutputWriter(output);
        }

        [Fact]
        public void CanConfigureApiKeyFromClientConstructor() {
            var client = new ExceptionlessClient("LhhP1C9gijpSKCslHHCvwdSIz298twx271n1l6xw");
            Assert.NotNull(client);
            Assert.Equal("LhhP1C9gijpSKCslHHCvwdSIz298twx271n1l6xw", client.Configuration.ApiKey);
        }

        [Fact]
        public void CanConfigureClientUsingActionMethod() {
            const string version = "1.2.3";
            
            var client = new ExceptionlessClient(c => {
                c.ApiKey = "LhhP1C9gijpSKCslHHCvwdSIz298twx271n1l6xw";
                c.ServerUrl = "http://localhost:45000";
                c.SetVersion(version);
            });

            Assert.Equal("LhhP1C9gijpSKCslHHCvwdSIz298twx271n1l6xw", client.Configuration.ApiKey);
            Assert.Equal("http://localhost:45000", client.Configuration.ServerUrl);
            Assert.Equal(version, client.Configuration.DefaultData[Event.KnownDataKeys.Version].ToString());
        }

        [Fact]
        public void CanReadFromAttributes() {
            var config = new ExceptionlessConfiguration(DependencyResolver.CreateDefault());
            Assert.Null(config.ApiKey);
            Assert.Equal("https://collector.exceptionless.io", config.ServerUrl);
            Assert.Equal(0, config.Settings.Count);

            config.ReadFromAttributes(typeof(ConfigurationTests).GetTypeInfo().Assembly);
            Assert.Equal("LhhP1C9gijpSKCslHHCvwdSIz298twx271n1l6xw", config.ApiKey);
            Assert.Equal("http://localhost:45000", config.ServerUrl);
            Assert.Equal(1, config.Settings.Count);
            Assert.Equal("configuration", config.Settings["testing"]);
        }

        [Fact]
        public void WillLockConfig() {
            var client = new ExceptionlessClient();
            client.Configuration.Resolver.Register<ISubmissionClient, InMemorySubmissionClient>();
            client.Configuration.ApiKey = "LhhP1C9gijpSKCslHHCvwdSIz298twx271n1l6xw";
            client.SubmitEvent(new Event());
            Assert.Throws<ArgumentException>(() => client.Configuration.ApiKey = "blah");
            Assert.Throws<ArgumentException>(() => client.Configuration.ServerUrl = "blah");
        }

        [Fact]
        public void CanUpdateSettingsFromServer() {
            var config = new ExceptionlessConfiguration(DependencyResolver.Default);
            config.ApiKey = "LhhP1C9gijpSKCslHHCvwdSIz298twx271n1l6xw";
            config.Settings["LocalSetting"] = "1";
            config.Settings["LocalSettingToOverride"] = "1";

            var submissionClient = new Mock<ISubmissionClient>();
            submissionClient.Setup(m => m.PostEvents(It.IsAny<IEnumerable<Event>>(), config, It.IsAny<IJsonSerializer>()))
                .Callback(() => SettingsManager.CheckVersion(1, config))
                .Returns(() => new SubmissionResponse(202, "Accepted"));
            submissionClient.Setup(m => m.GetSettings(config, 0, It.IsAny<IJsonSerializer>()))
                .Returns(() => new SettingsResponse(true, new SettingsDictionary { { "Test", "Test" }, { "LocalSettingToOverride", "2" } }, 1));

            config.Resolver.Register<ISubmissionClient>(submissionClient.Object);
            var client = new ExceptionlessClient(config);

            Assert.Equal(2, client.Configuration.Settings.Count);
            Assert.False(client.Configuration.Settings.ContainsKey("Test"));
            Assert.Equal("1", client.Configuration.Settings["LocalSettingToOverride"]);
            client.SubmitEvent(new Event { Type = "Log", Message = "Test" });
            client.ProcessQueue();
            Assert.True(client.Configuration.Settings.ContainsKey("Test"));
            Assert.Equal("2", client.Configuration.Settings["LocalSettingToOverride"]);
            Assert.Equal(3, client.Configuration.Settings.Count);

            var storage = config.Resolver.GetFileStorage() as InMemoryObjectStorage;
            Assert.NotNull(storage);
            Assert.NotNull(config.GetQueueName());
            Assert.True(storage.Exists(Path.Combine(config.GetQueueName(), "server-settings.json")));

            config.Settings.Clear();
            config.ApplySavedServerSettings();
            Assert.True(client.Configuration.Settings.ContainsKey("Test"));
            Assert.Equal("2", client.Configuration.Settings["LocalSettingToOverride"]);
            Assert.Equal(2, client.Configuration.Settings.Count);
        }

        [Fact]
        public void CanGetSettingsMultithreaded() {
            var settings = new SettingsDictionary();
            var result = Parallel.For(0, 20, index => {
                for (int i = 0; i < 10; i++) {
                    string key = $"setting-{i}";
                    if (!settings.ContainsKey(key))
                        settings.Add(key, (index * i).ToString());
                    else
                        settings[key] = (index * i).ToString();
                }
            });

            while (!result.IsCompleted)
                Thread.Sleep(1);
        }

        [Fact]
        public void CanGetLogSettingsMultithreaded() {
            var settings = new SettingsDictionary();
            settings.Add("@@log:*", "Info");
            settings.Add("@@log:Source1", "Trace");
            settings.Add("@@log:Source2", "Debug");
            settings.Add("@@log:Source3", "Info");
            settings.Add("@@log:Source4", "Info");

            var result = Parallel.For(0, 100, index => {
                var level = settings.GetMinLogLevel("Source1");
                _writer.WriteLine("Source1 log level: {0}", level);
            });

            while (!result.IsCompleted)
                Thread.Sleep(1);
        }
    }
}
