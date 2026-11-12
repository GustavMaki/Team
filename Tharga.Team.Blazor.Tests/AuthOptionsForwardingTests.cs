using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Tharga.Team.Blazor.Features.Authentication;
using Tharga.Team.Blazor.Framework;

namespace Tharga.Team.Blazor.Tests;

/// <summary>
/// Every <see cref="ThargaAuthOptions"/> property set through <c>AddThargaTeam(o =&gt; o.Auth...)</c> must reach
/// the registered options. The facade copied <c>LoginPath</c>, <c>LogoutPath</c> and <c>ValidateConfiguration</c>
/// by hand, so <c>FederatedSignOut</c> and <c>PromptForAccount</c> compiled, read naturally, and did nothing.
/// </summary>
/// <remarks>
/// <see cref="EveryProperty_ReachesTheRegisteredOptions"/> drives itself from the options' own shape, so a property
/// added later is covered without anyone remembering, as <see cref="IconOptionsForwardingTests"/> does for icons.
/// </remarks>
public class AuthOptionsForwardingTests
{
    private const string ValidAzureAdConfig = """
        { "AzureAd": { "Authority": "https://test.ciamlogin.com/test", "ClientId": "c", "TenantId": "t", "CallbackPath": "/signin-oidc" } }
        """;

    private static WebApplicationBuilder CreateBuilder()
    {
        var builder = WebApplication.CreateBuilder();
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(ValidAzureAdConfig));
        builder.Configuration.AddJsonStream(stream);
        return builder;
    }

    /// <summary>The reported case: a host opting out of federated sign-out, and into the account picker.</summary>
    [Fact]
    public void FederatedSignOutAndPromptForAccount_ReachTheRegisteredOptions()
    {
        var builder = CreateBuilder();
        builder.AddThargaTeam(o =>
        {
            o.Auth.FederatedSignOut = false;
            o.Auth.PromptForAccount = true;
        });

        using var provider = builder.Services.BuildServiceProvider();
        var resolved = provider.GetRequiredService<ThargaAuthOptions>();

        Assert.False(resolved.FederatedSignOut);
        Assert.True(resolved.PromptForAccount);
    }

    /// <summary>
    /// Sets each property to a value distinct from its default and asserts it survives into the container. A
    /// property whose type this test cannot set fails loudly rather than being skipped.
    /// </summary>
    [Fact]
    public void EveryProperty_ReachesTheRegisteredOptions()
    {
        var properties = OptionsForwarder.ForwardableProperties<ThargaAuthOptions>().ToArray();

        Assert.True(properties.Length >= 5, $"Expected at least the five known ThargaAuthOptions properties, found {properties.Length}.");

        var defaults = new ThargaAuthOptions();
        var expected = new Dictionary<string, object>();
        var builder = CreateBuilder();
        builder.AddThargaTeam(o =>
        {
            foreach (var property in properties)
            {
                var value = DistinctValueFor(property.PropertyType, property.Name, property.GetValue(defaults));
                property.SetValue(o.Auth, value);
                expected[property.Name] = value;
            }
        });

        using var provider = builder.Services.BuildServiceProvider();
        var resolved = provider.GetRequiredService<ThargaAuthOptions>();

        foreach (var property in properties)
        {
            Assert.Equal(expected[property.Name], property.GetValue(resolved));
        }
    }

    private static object DistinctValueFor(Type type, string propertyName, object defaultValue)
    {
        if (type == typeof(bool)) return !(bool)defaultValue;
        if (type == typeof(string)) return $"/forwarded-{propertyName}";
        throw new InvalidOperationException($"{nameof(ThargaAuthOptions)}.{propertyName} is a {type.Name}; teach {nameof(DistinctValueFor)} a distinct value for it.");
    }
}
