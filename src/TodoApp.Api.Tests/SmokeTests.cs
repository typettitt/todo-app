using FluentAssertions;

using Microsoft.AspNetCore.Mvc.Testing;

namespace TodoApp.Api.Tests;

public class SmokeTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public SmokeTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Host_StartsAndRespondsToRoot()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync(new Uri("/", UriKind.Relative));

        response.IsSuccessStatusCode.Should().BeTrue();
    }
}
