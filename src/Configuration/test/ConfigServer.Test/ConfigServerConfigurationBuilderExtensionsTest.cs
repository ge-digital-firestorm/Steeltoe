// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Steeltoe.Common.Security;
using Steeltoe.Common.TestResources;
using Steeltoe.Common.Utils.IO;
using Steeltoe.Configuration.CloudFoundry;
using Steeltoe.Configuration.Placeholder;
using Xunit;

namespace Steeltoe.Configuration.ConfigServer.Test;

public sealed class ConfigServerConfigurationBuilderExtensionsTest
{
    private const string VcapApplication = @" 
                {
                    ""application_id"": ""fa05c1a9-0fc1-4fbd-bae1-139850dec7a3"",
                    ""application_name"": ""foo"",
                    ""application_uris"": [
                        ""foo.10.244.0.34.xip.io""
                    ],
                    ""application_version"": ""fb8fbcc6-8d58-479e-bcc7-3b4ce5a7f0ca"",
                    ""limits"": {
                        ""disk"": 1024,
                        ""fds"": 16384,
                        ""mem"": 256
                    },
                    ""name"": ""foo"",
                    ""space_id"": ""06450c72-4669-4dc6-8096-45f9777db68a"",
                    ""space_name"": ""my-space"",
                    ""uris"": [
                        ""foo.10.244.0.34.xip.io""
                    ],
                    ""users"": null,
                    ""version"": ""fb8fbcc6-8d58-479e-bcc7-3b4ce5a7f0ca""
                }";

    private const string VcapServicesV2 = @"
                {
                    ""p-config-server"": [
                    {
                        ""name"": ""config-server"",
                        ""instance_name"": ""config-server"",
                        ""binding_name"": null,
                        ""credentials"": {
                            ""uri"": ""https://uri-from-vcap-services"",
                            ""client_secret"": ""some-secret"",
                            ""client_id"": ""some-client-id"",
                            ""access_token_uri"": ""https://uaa-uri-from-vcap-services/oauth/token""
                        },
                        ""syslog_drain_url"": null,
                        ""volume_mounts"": [],
                        ""label"": ""p-config-server"",
                        ""plan"": ""standard"",
                        ""provider"": null,
                        ""tags"": [
                            ""configuration"",
                            ""spring-cloud""
                        ]
                    }]
                }";

    private const string VcapServicesV3 = @"
                {
                    ""p.config-server"": [
                    {
                        ""name"": ""config-server"",
                        ""instance_name"": ""config-server"",
                        ""binding_name"": null,
                        ""credentials"": {
                            ""uri"": ""https://uri-from-vcap-services"",
                            ""client_secret"": ""some-secret"",
                            ""client_id"": ""some-client-id"",
                            ""access_token_uri"": ""https://uaa-uri-from-vcap-services/oauth/token""
                        },
                        ""syslog_drain_url"": null,
                        ""volume_mounts"": [],
                        ""label"": ""p-config-server"",
                        ""plan"": ""standard"",
                        ""provider"": null,
                        ""tags"": [
                            ""configuration"",
                            ""spring-cloud""
                        ]
                    }]
                }";

    private const string VcapServicesAlt = @"
                {
                    ""config-server"": [
                    {
                        ""name"": ""config-server"",
                        ""instance_name"": ""config-server"",
                        ""binding_name"": null,
                        ""credentials"": {
                            ""uri"": ""https://uri-from-vcap-services"",
                            ""client_secret"": ""some-secret"",
                            ""client_id"": ""some-client-id"",
                            ""access_token_uri"": ""https://uaa-uri-from-vcap-services/oauth/token""
                        },
                        ""syslog_drain_url"": null,
                        ""volume_mounts"": [],
                        ""label"": ""p-config-server"",
                        ""plan"": ""standard"",
                        ""provider"": null,
                        ""tags"": [
                            ""configuration"",
                            ""spring-cloud""
                        ]
                    }]
                }";

    [Fact]
    public void AddConfigServer_ThrowsIfConfigBuilderNull()
    {
        const IConfigurationBuilder configurationBuilder = null;

        var ex = Assert.Throws<ArgumentNullException>(() => configurationBuilder.AddConfigServer(new ConfigServerClientSettings()));
        Assert.Contains(nameof(configurationBuilder), ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AddConfigServer_ThrowsIfSettingsNull()
    {
        IConfigurationBuilder configurationBuilder = new ConfigurationBuilder();
        const ConfigServerClientSettings clientSettings = null;

        var ex = Assert.Throws<ArgumentNullException>(() => configurationBuilder.AddConfigServer(clientSettings));
        Assert.Contains(nameof(clientSettings), ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AddConfigServer_WithPemFiles_AddsConfigServerSourceWithCertificate()
    {
        var configurationBuilder = new ConfigurationBuilder();

        var settings = new ConfigServerClientSettings
        {
            Timeout = 10
        };

        configurationBuilder.AddPemFiles("instance.crt", "instance.key").AddConfigServer(settings);
        configurationBuilder.Build();

        var source = configurationBuilder.FindConfigurationSource<ConfigServerConfigurationSource>();
        Assert.NotNull(source);
        Assert.NotNull(source.DefaultSettings.ClientCertificate);
    }

    [Fact]
    public void AddConfigServer_AddsConfigServerSourceToList()
    {
        var configurationBuilder = new ConfigurationBuilder();
        var settings = new ConfigServerClientSettings();

        configurationBuilder.AddConfigServer(settings);

        ConfigServerConfigurationSource configServerSource = configurationBuilder.Sources.OfType<ConfigServerConfigurationSource>().SingleOrDefault();
        Assert.NotNull(configServerSource);
    }

    [Fact]
    public void AddConfigServer_WithLoggerFactorySucceeds()
    {
        var configurationBuilder = new ConfigurationBuilder();
        var loggerFactory = new LoggerFactory();
        var settings = new ConfigServerClientSettings();

        configurationBuilder.AddConfigServer(settings, loggerFactory);

        ConfigServerConfigurationSource configServerSource = configurationBuilder.Sources.OfType<ConfigServerConfigurationSource>().SingleOrDefault();

        Assert.NotNull(configServerSource);
        Assert.NotNull(configServerSource.LoggerFactory);
    }

    [Fact]
    public void AddConfigServer_JsonAppSettingsConfiguresClient()
    {
        const string appsettings = @"
                {
                    ""spring"": {
                        ""application"": {
                            ""name"": ""myName""
                    },
                      ""cloud"": {
                        ""config"": {
                            ""uri"": ""https://user:password@foo.com:9999"",
                            ""enabled"": false,
                            ""failFast"": false,
                            ""label"": ""myLabel"",
                            ""username"": ""myUsername"",
                            ""password"": ""myPassword"",
                            ""timeout"": 10000,
                            ""token"" : ""vaulttoken"",
                            ""retry"": {
                                ""enabled"":""false"",
                                ""initialInterval"":55555,
                                ""maxInterval"": 55555,
                                ""multiplier"": 5.5,
                                ""maxAttempts"": 55555
                            }
                        }
                      }
                    }
                }";

        using var sandbox = new Sandbox();
        string path = sandbox.CreateFile("appsettings.json", appsettings);
        string directory = Path.GetDirectoryName(path);
        string fileName = Path.GetFileName(path);
        var configurationBuilder = new ConfigurationBuilder();
        configurationBuilder.SetBasePath(directory);

        var clientSettings = new ConfigServerClientSettings();
        configurationBuilder.AddJsonFile(fileName);

        configurationBuilder.AddConfigServer(clientSettings);
        IConfigurationRoot configurationRoot = configurationBuilder.Build();

        ConfigServerConfigurationProvider configServerProvider = configurationRoot.Providers.OfType<ConfigServerConfigurationProvider>().SingleOrDefault();
        Assert.NotNull(configServerProvider);

        ConfigServerClientSettings settings = configServerProvider.Settings;

        Assert.False(settings.Enabled);
        Assert.False(settings.FailFast);
        Assert.Equal("https://user:password@foo.com:9999", settings.Uri);
        Assert.Equal(ConfigServerClientSettings.DefaultEnvironment, settings.Environment);
        Assert.Equal("myName", settings.Name);
        Assert.Equal("myLabel", settings.Label);
        Assert.Equal("myUsername", settings.Username);
        Assert.Equal("myPassword", settings.Password);
        Assert.False(settings.RetryEnabled);
        Assert.Equal(55555, settings.RetryAttempts);
        Assert.Equal(55555, settings.RetryInitialInterval);
        Assert.Equal(55555, settings.RetryMaxInterval);
        Assert.Equal(5.5, settings.RetryMultiplier);
        Assert.Equal(10000, settings.Timeout);
        Assert.Equal("vaulttoken", settings.Token);
    }

    [Fact]
    public void AddConfigServer_XmlAppSettingsConfiguresClient()
    {
        const string appsettings = @"
<settings>
    <spring>
      <cloud>
        <config>
            <uri>https://foo.com:9999</uri>
            <enabled>false</enabled>
            <failFast>false</failFast>
            <label>myLabel</label>
            <name>myName</name>
            <username>myUsername</username>
            <password>myPassword</password>
        </config>
      </cloud>
    </spring>
</settings>";

        using var sandbox = new Sandbox();
        string path = sandbox.CreateFile("appsettings.json", appsettings);
        string directory = Path.GetDirectoryName(path);
        string fileName = Path.GetFileName(path);
        var configurationBuilder = new ConfigurationBuilder();
        configurationBuilder.SetBasePath(directory);

        var clientSettings = new ConfigServerClientSettings();
        configurationBuilder.AddXmlFile(fileName);

        configurationBuilder.AddConfigServer(clientSettings);
        IConfigurationRoot configurationRoot = configurationBuilder.Build();

        ConfigServerConfigurationProvider configServerProvider = configurationRoot.Providers.OfType<ConfigServerConfigurationProvider>().FirstOrDefault();

        Assert.NotNull(configServerProvider);
        ConfigServerClientSettings settings = configServerProvider.Settings;

        Assert.False(settings.Enabled);
        Assert.False(settings.FailFast);
        Assert.Equal("https://foo.com:9999", settings.Uri);
        Assert.Equal(ConfigServerClientSettings.DefaultEnvironment, settings.Environment);
        Assert.Equal("myName", settings.Name);
        Assert.Equal("myLabel", settings.Label);
        Assert.Equal("myUsername", settings.Username);
        Assert.Equal("myPassword", settings.Password);
    }

    [Fact]
    public void AddConfigServer_IniAppSettingsConfiguresClient()
    {
        const string appsettings = @"
[spring:cloud:config]
    uri=https://foo.com:9999
    enabled=false
    failFast=false
    label=myLabel
    name=myName
    username=myUsername
    password=myPassword
";

        using var sandbox = new Sandbox();
        string path = sandbox.CreateFile("appsettings.json", appsettings);
        string directory = Path.GetDirectoryName(path);
        string fileName = Path.GetFileName(path);
        var configurationBuilder = new ConfigurationBuilder();
        configurationBuilder.SetBasePath(directory);

        var clientSettings = new ConfigServerClientSettings();
        configurationBuilder.AddIniFile(fileName);

        configurationBuilder.AddConfigServer(clientSettings);
        IConfigurationRoot configurationRoot = configurationBuilder.Build();

        ConfigServerConfigurationProvider configServerProvider = configurationRoot.Providers.OfType<ConfigServerConfigurationProvider>().SingleOrDefault();

        Assert.NotNull(configServerProvider);
        ConfigServerClientSettings settings = configServerProvider.Settings;

        Assert.False(settings.Enabled);
        Assert.False(settings.FailFast);
        Assert.Equal("https://foo.com:9999", settings.Uri);
        Assert.Equal(ConfigServerClientSettings.DefaultEnvironment, settings.Environment);
        Assert.Equal("myName", settings.Name);
        Assert.Equal("myLabel", settings.Label);
        Assert.Equal("myUsername", settings.Username);
        Assert.Equal("myPassword", settings.Password);
    }

    [Fact]
    public void AddConfigServer_CommandLineAppSettingsConfiguresClient()
    {
        string[] appsettings =
        {
            "spring:cloud:config:enabled=false",
            "--spring:cloud:config:failFast=false",
            "/spring:cloud:config:uri=https://foo.com:9999",
            "--spring:cloud:config:name",
            "myName",
            "/spring:cloud:config:label",
            "myLabel",
            "--spring:cloud:config:username",
            "myUsername",
            "--spring:cloud:config:password",
            "myPassword"
        };

        var configurationBuilder = new ConfigurationBuilder();
        var clientSettings = new ConfigServerClientSettings();
        configurationBuilder.AddCommandLine(appsettings);

        configurationBuilder.AddConfigServer(clientSettings);
        IConfigurationRoot configurationRoot = configurationBuilder.Build();
        ConfigServerConfigurationProvider configServerProvider = configurationRoot.Providers.OfType<ConfigServerConfigurationProvider>().SingleOrDefault();

        Assert.NotNull(configServerProvider);
        ConfigServerClientSettings settings = configServerProvider.Settings;

        Assert.False(settings.Enabled);
        Assert.False(settings.FailFast);
        Assert.Equal("https://foo.com:9999", settings.Uri);
        Assert.Equal(ConfigServerClientSettings.DefaultEnvironment, settings.Environment);
        Assert.Equal("myName", settings.Name);
        Assert.Equal("myLabel", settings.Label);
        Assert.Equal("myUsername", settings.Username);
        Assert.Equal("myPassword", settings.Password);
    }

    [Fact]
    public void AddConfigServer_HandlesPlaceHolders()
    {
        const string appsettings = @"
                {
                    ""foo"": {
                        ""bar"": {
                            ""name"": ""testName""
                        },
                    },
                    ""spring"": {
                        ""application"": {
                            ""name"": ""myName""
                        },
                      ""cloud"": {
                        ""config"": {
                            ""uri"": ""https://user:password@foo.com:9999"",
                            ""enabled"": false,
                            ""failFast"": false,
                            ""name"": ""${foo:bar:name?foobar}"",
                            ""label"": ""myLabel"",
                            ""username"": ""myUsername"",
                            ""password"": ""myPassword""
                        }
                      }
                    }
                }";

        using var sandbox = new Sandbox();
        string path = sandbox.CreateFile("appsettings.json", appsettings);

        string directory = Path.GetDirectoryName(path);
        string fileName = Path.GetFileName(path);
        var configurationBuilder = new ConfigurationBuilder();
        configurationBuilder.SetBasePath(directory);

        var clientSettings = new ConfigServerClientSettings();
        configurationBuilder.AddJsonFile(fileName);

        configurationBuilder.AddConfigServer(clientSettings);
        IConfigurationRoot configurationRoot = configurationBuilder.Build();

        ConfigServerConfigurationProvider configServerProvider = configurationRoot.Providers.OfType<ConfigServerConfigurationProvider>().SingleOrDefault();

        Assert.NotNull(configServerProvider);
        ConfigServerClientSettings settings = configServerProvider.Settings;

        Assert.False(settings.Enabled);
        Assert.False(settings.FailFast);
        Assert.Equal("https://user:password@foo.com:9999", settings.Uri);
        Assert.Equal(ConfigServerClientSettings.DefaultEnvironment, settings.Environment);
        Assert.Equal("testName", settings.Name);
        Assert.Equal("myLabel", settings.Label);
        Assert.Equal("myUsername", settings.Username);
        Assert.Equal("myPassword", settings.Password);
    }

    [Theory]
    [InlineData(VcapServicesV2)]
    [InlineData(VcapServicesV3)]
    [InlineData(VcapServicesAlt)]
    public void AddConfigServer_VCAP_SERVICES_Override_Defaults(string vcapServices)
    {
        using var appScope = new EnvironmentVariableScope("VCAP_APPLICATION", VcapApplication);
        using var servicesScope = new EnvironmentVariableScope("VCAP_SERVICES", vcapServices);

        var configurationBuilder = new ConfigurationBuilder();

        var settings = new ConfigServerClientSettings
        {
            Uri = "https://uri-from-settings",
            RetryEnabled = false,
            Timeout = 10
        };

        configurationBuilder.AddEnvironmentVariables().AddConfigServer(settings);
        IConfigurationRoot configurationRoot = configurationBuilder.Build();
        ConfigServerConfigurationProvider configServerProvider = configurationRoot.Providers.OfType<ConfigServerConfigurationProvider>().FirstOrDefault();

        Assert.NotNull(configServerProvider);
        Assert.IsType<ConfigServerConfigurationProvider>(configServerProvider);

        Assert.NotEqual("https://uri-from-settings", configServerProvider.Settings.Uri);
        Assert.Equal("https://uri-from-vcap-services", configServerProvider.Settings.Uri);
    }

    [Fact]
    public void AddConfigServer_PaysAttentionToSettings()
    {
        var configServerClientSettings = new ConfigServerClientSettings
        {
            Name = "testConfigName",
            Label = "testConfigLabel",
            Environment = "testEnv",
            Username = "testUser",
            Password = "testPassword",
            Timeout = 10,
            RetryEnabled = false
        };

        IConfigurationBuilder builder = new ConfigurationBuilder().AddConfigServer(configServerClientSettings);

        IConfigurationRoot configurationRoot = builder.Build();
        ConfigServerConfigurationProvider provider = configurationRoot.Providers.OfType<ConfigServerConfigurationProvider>().FirstOrDefault();

        Assert.NotNull(provider);
        Assert.Equal("testConfigLabel", provider.Settings.Label);
        Assert.Equal("testConfigName", provider.Settings.Name);
        Assert.Equal("testEnv", provider.Settings.Environment);
        Assert.Equal("testUser", provider.Settings.Username);
        Assert.Equal("testPassword", provider.Settings.Password);
    }

    [Fact]
    public void AddConfigServer_AddsCloudFoundryConfigurationSource()
    {
        var configurationBuilder = new ConfigurationBuilder();

        configurationBuilder.AddConfigServer();

        var source = configurationBuilder.FindConfigurationSource<CloudFoundryConfigurationSource>();
        Assert.NotNull(source);
    }

    [Fact]
    public void AddConfigServer_Only_AddsOneCloudFoundryConfigurationSource()
    {
        var configurationBuilder = new ConfigurationBuilder();

        configurationBuilder.AddCloudFoundry(new CustomCloudFoundrySettingsReader());
        configurationBuilder.AddConfigServer();

        Assert.Single(configurationBuilder.GetConfigurationSources<CloudFoundryConfigurationSource>());
    }
}
