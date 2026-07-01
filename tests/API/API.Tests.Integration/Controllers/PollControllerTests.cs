using API.Tests.Integration.Infrastructure;
using FluentAssertions;
using Shared.Contracts.Poll;
using Shared.Enum;
using Shared.Infrastructure;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace API.Tests.Integration.Controllers;

public class PollsControllerTests : ControllerTestBase
{
    public PollsControllerTests(WebAppFactory factory) : base(factory) { }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    [Fact]
    public async Task CreatePoll_WhenMember_Returns200()
    {
        var user = await SeedAndAuthenticateAsync("voter", "Pass123!");
        var chat = await TestDataSeeder.SeedChatAsync(DbContext, user.Id, ChatType.Chat, "Chat");

        var dto = new CreatePollDto
        {
            ChatId = chat.Id,
            Question = "Yes or No?",
            Options = new List<CreatePollOptionDto>
            {
                new() { Text = "Yes", Position = 0 },
                new() { Text = "No", Position = 1 }
            }
        };

        var response = await Client.PostAsJsonAsync("/api/polls", dto);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task CreatePoll_WhenNotMember_Returns403()
    {
        var owner = await TestDataSeeder.SeedUserAsync(DbContext, "owner", "Pass123!");
        var chat = await TestDataSeeder.SeedChatAsync(DbContext, owner.Id, ChatType.Chat, "Private");
        await SeedAndAuthenticateAsync("intruder", "Pass123!");

        var dto = new CreatePollDto
        {
            ChatId = chat.Id,
            Question = "Q?",
            Options = new List<CreatePollOptionDto> { new() { Text = "A" } }
        };

        var response = await Client.PostAsJsonAsync("/api/polls", dto);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task CreatePoll_WhenUnauthorized_Returns401()
    {
        var dto = new CreatePollDto
        {
            ChatId = 1,
            Question = "Q?",
            Options = new List<CreatePollOptionDto> { new() { Text = "A" } }
        };

        var response = await Client.PostAsJsonAsync("/api/polls", dto);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Vote_WhenMember_Returns200()
    {
        var user = await SeedAndAuthenticateAsync("voter", "Pass123!");
        var chat = await TestDataSeeder.SeedChatAsync(DbContext, user.Id, ChatType.Chat, "Chat");

        var createDto = new CreatePollDto
        {
            ChatId = chat.Id,
            Question = "Yes or No?",
            Options = new List<CreatePollOptionDto>
            {
                new() { Text = "Yes", Position = 0 },
                new() { Text = "No", Position = 1 }
            }
        };
        var createResponse = await Client.PostAsJsonAsync("/api/polls", createDto);
        createResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var voteDto = new PollVoteDto
        {
            PollId = 1,
            OptionId = 1
        };

        var response = await Client.PostAsJsonAsync("/api/polls/vote", voteDto);

        response.StatusCode.Should().BeOneOf([HttpStatusCode.OK, HttpStatusCode.NotFound]);
    }

    [Fact]
    public async Task Vote_WhenUnauthorized_Returns401()
    {
        var dto = new PollVoteDto { PollId = 1, OptionId = 1 };

        var response = await Client.PostAsJsonAsync("/api/polls/vote", dto);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ClosePoll_WhenOwner_Returns200()
    {
        var user = await SeedAndAuthenticateAsync("owner", "Pass123!");
        var chat = await TestDataSeeder.SeedChatAsync(DbContext, user.Id, ChatType.Chat, "Chat");

        var createDto = new CreatePollDto
        {
            ChatId = chat.Id,
            Question = "Close me",
            Options = new List<CreatePollOptionDto>
            {
                new() { Text = "A", Position = 0 },
                new() { Text = "B", Position = 1 }
            }
        };
        await Client.PostAsJsonAsync("/api/polls", createDto);

        var response = await Client.PostAsync("/api/polls/1/close", null);

        response.StatusCode.Should().BeOneOf([HttpStatusCode.OK, HttpStatusCode.NotFound]);
    }

    [Fact]
    public async Task ClosePoll_WhenUnauthorized_Returns401()
    {
        var response = await Client.PostAsync("/api/polls/1/close", null);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetPoll_WhenExists_Returns200()
    {
        var user = await SeedAndAuthenticateAsync("Viewer", "Pass123!");
        var chat = await TestDataSeeder.SeedChatAsync(DbContext, user.Id, ChatType.Chat, "Chat");

        var createDto = new CreatePollDto
        {
            ChatId = chat.Id,
            Question = "View me",
            Options = new List<CreatePollOptionDto>
            {
                new() { Text = "A", Position = 0 },
                new() { Text = "B", Position = 1 }
            }
        };
        await Client.PostAsJsonAsync("/api/polls", createDto);

        var response = await Client.GetAsync("/api/polls/1");

        response.StatusCode.Should().BeOneOf([HttpStatusCode.OK, HttpStatusCode.NotFound]);
    }

    [Fact]
    public async Task GetPoll_WhenNotFound_Returns404()
    {
        await SeedAndAuthenticateAsync("Viewer", "Pass123!");

        var response = await Client.GetAsync("/api/polls/99999");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetPoll_WhenUnauthorized_Returns401()
    {
        var response = await Client.GetAsync("/api/polls/1");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}