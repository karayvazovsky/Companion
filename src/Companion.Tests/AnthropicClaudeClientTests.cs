using System.Net;
using Anthropic.Exceptions;
using Companion.Core;
using Xunit;

namespace Companion.Tests;

public class AnthropicClaudeClientMapTests
{
    [Fact]
    public void Mid_stream_errors_map_to_server()
    {
        Assert.Equal(ClaudeErrorKind.Server, AnthropicClaudeClient.Map(new AnthropicSseException("overloaded_error")).Kind);
    }

    [Fact]
    public void Forbidden_and_not_found_map_to_no_access()
    {
        Assert.Equal(ClaudeErrorKind.NoAccess, AnthropicClaudeClient.Map(new AnthropicForbiddenException { StatusCode = HttpStatusCode.Forbidden, ResponseBody = "{}" }).Kind);
        Assert.Equal(ClaudeErrorKind.NoAccess, AnthropicClaudeClient.Map(new AnthropicNotFoundException { StatusCode = HttpStatusCode.NotFound, ResponseBody = "{}" }).Kind);
    }

    [Theory]
    [InlineData("Your credit balance is too low to access the Anthropic API.")]
    [InlineData("This organization has been disabled due to a billing issue.")]
    public void Billing_bad_requests_map_to_billing(string message)
    {
        Assert.Equal(ClaudeErrorKind.Billing, AnthropicClaudeClient.Map(new AnthropicBadRequestException { StatusCode = HttpStatusCode.BadRequest, ResponseBody = message }).Kind);
    }
}
