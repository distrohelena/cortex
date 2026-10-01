using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Cortex.Core.Flows;
using Cortex.Core.Memory;
using Xunit;
using static Filomena.Llm.Tests.ParticipationExperiments;

namespace Filomena.Llm.Tests;

public sealed class GenerationCalibrationTests
{
    internal static JsonNode Fixture(bool memory = true, string state = Inactive)
    {
        var project = JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,
            memory ? "typed-flow-slice41.json" : "typed-flow-slice41-no-memory.json")))!;
        project["project"]!["initialStateId"] = state;
        return project;
    }

    [Fact]
    public async Task BoundariesHaveIndependentBudgetActiveFilterAndAttribution()
    {
        var reader = new DeliveryMemory();
        var (result, run) = await GenerationExperiments.Execute(GenerationExperiments.Cases[7], true,
            new GenerationTests.Model(), reader, "fake", TestContext.Current.CancellationToken, Fixture(true, Engaged));
        Assert.True(result.Success, result.Error);
        Assert.Equal(2, reader.Queries.Count);
        Assert.Equal(MemoryMode.Exact, reader.Queries[0].Mode);
        Assert.Equal(700, reader.Queries[0].MaxCharacters);
        Assert.Equal(1200, reader.Queries[1].MaxCharacters);
        Assert.All(reader.Queries, q => Assert.True(q.ActiveOnly));
        var request = run.Requests.Single(q => q.NodeId == "group-generation");
        var limits = request.Inputs.GetProperty("limites");
        Assert.Single(limits.EnumerateArray());
        Assert.Contains("Helena", limits.GetRawText());
        Assert.Equal("limite", limits[0].GetProperty("categoria").GetString());
        Assert.Empty(request.Inputs.GetProperty("memorias_sociais").EnumerateArray()); // oversized social fact
        Assert.False(request.Inputs.TryGetProperty("memorias", out _));
        Assert.DoesNotContain("stale", request.Inputs.GetRawText());
        Assert.DoesNotContain(reader.Queries[0].Persons[0].ParticipantUuid.ToString(), request.Inputs.GetRawText());
        Assert.Contains("interpretação auxiliar, nunca fonte factual", request.System);
        Assert.Contains("precedência sobre estilo", request.System);
        Assert.Contains("Superioridade é presença, não desprezo.", request.System);
        Assert.DoesNotContain("Interest profile", request.System);
        Assert.All(run.Requests.Where(q => q.NodeId != "group-generation"), q => Assert.DoesNotContain("Personality core", q.System));
        Assert.DoesNotContain(result.Path, p => p.Contains("Helena", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("unavailable")]
    [InlineData("wrong-session")]
    public async Task BoundaryFailureStopsBeforeSocialAndGeneration(string failure)
    {
        var reader = new DeliveryMemory(failure);
        var (result, run) = await GenerationExperiments.Execute(GenerationExperiments.Cases[7], true,
            new GenerationTests.Model(), reader, "fake", TestContext.Current.CancellationToken, Fixture(true, Engaged));
        Assert.False(result.Success);
        Assert.Equal("NO_SEND", result.Signal);
        Assert.False(run.Outcome.TransitionApplied);
        Assert.Single(reader.Queries);
        Assert.DoesNotContain(run.Requests, r => r.NodeId == "group-generation");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task EmptyIsNotFailureAndUnconnectedMemoryIsAbsent(bool memory)
    {
        var (result, run) = await GenerationExperiments.Execute(GenerationExperiments.Cases[7], memory,
            new GenerationTests.Model(), new DeliveryMemory("empty"), "fake", TestContext.Current.CancellationToken, Fixture(memory, Engaged));
        Assert.True(result.Success, result.Error);
        var inputs = run.Requests.Single(q => q.NodeId == "group-generation").Inputs;
        Assert.Equal(memory, inputs.TryGetProperty("limites", out var limits));
        Assert.Equal(memory, inputs.TryGetProperty("memorias_sociais", out _));
        if (memory) Assert.Empty(limits.EnumerateArray());
    }

    [Fact]
    public async Task EmptyGenerationStillCannotSimulateOrTransition()
    {
        var (result, run) = await GenerationExperiments.Execute(GenerationExperiments.Cases[7], true,
            new GenerationTests.Model(" "), new DeliveryMemory("empty"), "fake", TestContext.Current.CancellationToken, Fixture(true, Engaged));
        Assert.False(result.Success);
        Assert.Equal("NO_SEND", result.Signal);
        Assert.False(run.Outcome.TransitionApplied);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Roundtrip(bool memory)
    {
        var token = TestContext.Current.CancellationToken;
        var directory = Directory.CreateTempSubdirectory("slice41-");
        using var external = new HttpClient();
        try
        {
            await using var app = App.LocalPromptPlayground.Build(new(), external, 0, projectDirectory: directory.FullName);
            await app.StartAsync(token);
            using var browser = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
            browser.DefaultRequestHeaders.Add("X-Filomena-Playground", "1");
            var p = Fixture(memory);
            (await browser.PostAsJsonAsync("/api/flow/project", p, token)).EnsureSuccessStatusCode();
            var loaded = await browser.GetFromJsonAsync<JsonElement>("/api/flow/project", token);
            Assert.True(JsonElement.DeepEquals(JsonSerializer.SerializeToElement(p), loaded));
            var response = await browser.PostAsJsonAsync("/api/flow/typed/project/validate", new { project = loaded }, token);
            Assert.True((await response.Content.ReadFromJsonAsync<JsonElement>(token)).GetProperty("valid").GetBoolean());
        }
        finally { directory.Delete(true); }
    }

    private sealed class DeliveryMemory(string mode = "normal") : IMemoryReader
    {
        internal List<MemoryQuery> Queries { get; } = [];
        public Task<IReadOnlyList<MemoryItem>> QueryAsync(MemoryQuery query, CancellationToken token)
        {
            Queries.Add(query);
            if (mode == "unavailable") throw new HttpRequestException("fake unavailable");
            if (mode == "empty") return Task.FromResult<IReadOnlyList<MemoryItem>>([]);
            var p = query.Persons[0];
            var ns = mode == "wrong-session" ? "whatsapp-group-other-g-us" : query.ConversationNamespace;
            MemoryItem boundary = new("active", ns, "Helena pediu para não brincar sobre trabalho.", MemoryCategory.Boundary, [p], "exact");
            var other = MemoryIdentity.Person(query.ConversationNamespace, "whatsapp:5511999999998@s.whatsapp.net", "Bia");
            return Task.FromResult<IReadOnlyList<MemoryItem>>([boundary,
                boundary with { Id = "stale", InvalidAt = DateTimeOffset.UtcNow.AddDays(-1) },
                boundary with { Id = "expired", ExpiredAt = DateTimeOffset.UtcNow.AddDays(-1) },
                boundary with { Id = "other", Subjects = [other], Content = "Bia only" },
                boundary with { Id = "large-social", Category = MemoryCategory.Fact, Content = new string('x', 1201) }]);
        }
    }
}
