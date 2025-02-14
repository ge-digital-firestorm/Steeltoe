// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

namespace Steeltoe.Configuration.CloudFoundry;

internal sealed class CloudFoundryEnvironmentSettingsReader : ICloudFoundrySettingsReader
{
    private const string CfInstanceGuid = "CF_INSTANCE_GUID";
    private const string CfInstanceIndex = "CF_INSTANCE_INDEX";
    private const string CfInstanceInternalIP = "CF_INSTANCE_INTERNAL_IP";
    private const string CfInstanceIP = "CF_INSTANCE_IP";
    private const string CfInstancePort = "CF_INSTANCE_PORT";
    private const string VcapApplication = "VCAP_APPLICATION";
    private const string VcapServices = "VCAP_SERVICES";

    public string ApplicationJson => Environment.GetEnvironmentVariable(VcapApplication);

    public string InstanceId => Environment.GetEnvironmentVariable(CfInstanceGuid);

    public string InstanceIndex => Environment.GetEnvironmentVariable(CfInstanceIndex);

    public string InstanceInternalIP => Environment.GetEnvironmentVariable(CfInstanceInternalIP);

    public string InstanceIP => Environment.GetEnvironmentVariable(CfInstanceIP);

    public string InstancePort => Environment.GetEnvironmentVariable(CfInstancePort);

    public string ServicesJson => Environment.GetEnvironmentVariable(VcapServices);
}
