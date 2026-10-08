using BDIT.TenantToolkit.App.Services;
using Xunit;

namespace BDIT.TenantToolkit.Tests;

public sealed class PortableCliHostTests
{
    [Fact]
    public void Dispatch_is_explicit_and_preserves_exact_argument_boundaries()
    {
        Assert.False(PortableCliHost.IsRequested([]));
        Assert.False(PortableCliHost.IsRequested(["--root", "--cli"]));
        var args = new[] { "--CLI", "inventory", "--snapshot", "synthetic folder/a & b.json", "--root", "synthetic root" };
        Assert.True(PortableCliHost.IsRequested(args));
        Assert.Equal(args.Skip(1), PortableCliHost.Arguments(args));
        Assert.Empty(PortableCliHost.Arguments(["--cli"]));
        Assert.Throws<ArgumentException>(() => PortableCliHost.Arguments(["inventory"]));
    }
}
