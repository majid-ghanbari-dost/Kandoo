using Kandoo.App;
using Xunit;

namespace Kandoo.App.Tests;

public sealed class RoleProfileTests
{
    [Fact]
    public void C58_AndroidSupportsAllRolesAndWindowsOnlyStore()
    {
        Assert.Equal(
            new[] { AppUserRole.Store, AppUserRole.Customer, AppUserRole.Both },
            AppRolePolicy.SupportedRoles(false));
        Assert.Equal(new[] { AppUserRole.Store }, AppRolePolicy.SupportedRoles(true));
        Assert.False(AppRolePolicy.IsSupported(AppUserRole.Customer, true));
        Assert.False(AppRolePolicy.IsSupported(AppUserRole.Both, true));
        Assert.Equal(AppUserRole.Store, AppRolePolicy.Normalize(AppUserRole.Customer, true));
        Assert.Equal(AppUserRole.Store, AppRolePolicy.Normalize(AppUserRole.Both, true));
    }

    [Theory]
    [InlineData(AppUserRole.Store, "store")]
    [InlineData(AppUserRole.Customer, "customer")]
    [InlineData(AppUserRole.Both, "both")]
    public void C58_LocalRoleValuesRoundTrip(AppUserRole role, string stored)
    {
        Assert.Equal(stored, AppRolePolicy.Serialize(role));
        Assert.True(AppRolePolicy.TryParse(stored, out var parsed));
        Assert.Equal(role, parsed);
    }

    [Fact]
    public void C58_InvalidStoredRoleIsFirstRunAndDemoNamesAreExplicit()
    {
        Assert.False(AppRolePolicy.TryParse("unexpected", out _));
        foreach (var role in AppRolePolicy.SupportedRoles(false))
        {
            Assert.Contains("نمونه", AppRolePolicy.DemoDisplayName(role));
        }
    }
}
