using Lingarr.Contracts.Models;
using Lingarr.Server.Models.Api;
using Xunit;

namespace Lingarr.Server.Tests.Controllers;

/// <summary>
/// Connection test is a model-catalogue probe, not a translation. These messages are what the
/// settings UI shows, so they must not claim inference succeeded.
/// </summary>
public class PluginTestConnectionResponseTests
{
    [Fact]
    public void FromModels_WithOptions_ReportsCount()
    {
        var response = PluginTestConnectionResponse.FromModels(new ModelsResponse
        {
            Options = [new LabelValue { Label = "model-a", Value = "model-a" }]
        });

        Assert.True(response.Success);
        Assert.Equal("Connected. Found 1 model(s).", response.Message);
        Assert.Single(response.Options);
    }

    [Fact]
    public void FromModels_EmptyCatalogue_StillSucceedsAndAllowsManualId()
    {
        var response = PluginTestConnectionResponse.FromModels(new ModelsResponse());

        Assert.True(response.Success);
        Assert.Equal("Connected, but no models were returned. You can enter a model id manually.", response.Message);
        Assert.Empty(response.Options);
    }

    [Fact]
    public void FromModels_Message_IsFailureWithoutPretendingConnected()
    {
        var response = PluginTestConnectionResponse.FromModels(new ModelsResponse
        {
            Message = "Error fetching models: 404"
        });

        Assert.False(response.Success);
        Assert.Equal("Error fetching models: 404", response.Message);
    }
}
