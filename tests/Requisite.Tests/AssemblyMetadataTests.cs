using System;
using System.Diagnostics;
using System.Reflection;
using Requisite;
using Xunit;

namespace Requisite.Tests;

public sealed class AssemblyMetadataTests
{
    [Fact]
    public void AssemblyAndFileVersionsMatchPackageVersion()
    {
        Assembly assembly = typeof(Probability).Assembly;
        Version? assemblyVersion = assembly.GetName().Version;
        string? fileVersion = FileVersionInfo
            .GetVersionInfo(assembly.Location)
            .FileVersion;
        string? informationalVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        Assert.Equal(new Version(0, 1, 0, 0), assemblyVersion);
        Assert.Equal("0.1.0.0", fileVersion);
        Assert.NotNull(informationalVersion);
        Assert.StartsWith("0.1.0", informationalVersion, StringComparison.Ordinal);
    }
}
