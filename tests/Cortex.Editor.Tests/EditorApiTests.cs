using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Cortex.Core.Flows;
using Cortex.Core.Plugins;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Cortex.Editor.Tests;

[Trait("Category", "LocalSocket")]
public sealed class EditorApiTests
{
    [Fact]
    public async Task CatalogAndNodeDescriptionIdentifyTheirPluginLibrary()
    {
        var (app, client, _) = await StartAsync(true);
        await using (app)
        using (client)
        {
            (await client.PostAsJsonAsync("api/graph/open", new EditorGraphRequest(EditorRuntimeTests.Graph("fixture")), TestContext.Current.CancellationToken)).EnsureSuccessStatusCode();
            using var state = await client.GetFromJsonAsync<JsonDocument>("api/state", TestContext.Current.CancellationToken);
            var library = state!.RootElement.GetProperty("catalog").EnumerateArray().Single(node => node.GetProperty("kind").GetString() == "fixture").GetProperty("library");
            Assert.Equal("fixture", library.GetProperty("id").GetString());
            Assert.Equal("Fixture", library.GetProperty("displayName").GetString());
            var described = state.RootElement.GetProperty("descriptions").GetProperty("input");
            Assert.True(JsonElement.DeepEquals(library, described.GetProperty("library")));
            Assert.Equal("event", described.GetProperty("semantic").GetString());
            Assert.Equal("Entry", described.GetProperty("executionMode").GetString());
            Assert.Equal("string", described.GetProperty("ports")[0].GetProperty("type").GetString());
            Assert.Contains(state.RootElement.GetProperty("catalog").EnumerateArray(), node => node.GetProperty("kind").GetString() == "count");
            await app.StopAsync(TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task HostAndUseMetadataDiscoveryDoNotBindRuntimeHandlers()
    {
        var (app, client, runtime) = await StartAsync(true, runtimeFactoryThrows: true);
        await using (app)
        using (client)
        {
            Assert.True(runtime.Plugins.Reports.Single().Loaded);
            (await client.PostAsJsonAsync("api/graph/open", new EditorGraphRequest(EditorRuntimeTests.Graph("fixture")), TestContext.Current.CancellationToken)).EnsureSuccessStatusCode();
            using var validation = await client.PostAsync("api/validate", null, TestContext.Current.CancellationToken);
            validation.EnsureSuccessStatusCode();
            using var result = JsonDocument.Parse(await validation.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
            Assert.True(result.RootElement.GetProperty("success").GetBoolean());
            using var state = await client.GetFromJsonAsync<JsonDocument>("api/state", TestContext.Current.CancellationToken);
            Assert.Contains(state!.RootElement.GetProperty("catalog").EnumerateArray(), node => node.GetProperty("kind").GetString() == "fixture");
            using var described = await client.PostAsJsonAsync("api/graph/describe", new EditorGraphRequest(runtime.Graph.Graph), TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.OK, described.StatusCode);
            Assert.Contains("fixture", await described.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
            Assert.True(runtime.Plugins.Reports.Single().Loaded);
            await app.StopAsync(TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task StateEndpointDescribesInvalidSavedDefaultsAndPromptFieldsWithoutAbortingGraph()
    {
        var (app, client, _) = await StartAsync(false);
        await using (app)
        using (client)
        {
            var graph = JsonSerializer.SerializeToElement(new
            {
                version = 4,
                nodes = new object[]
                {
                    new { id = "entry", kind = "dm", settings = new { } },
                    new { id = "prompt", kind = "prompt", prompt = "write", responseFields = new[] { new { id = "score", name = "score", type = "number", min = 5, max = 1 } } },
                    new { id = "count", kind = "count", settings = new { } }
                }, edges = Array.Empty<object>(), controlEdges = Array.Empty<object>()
            });
            (await client.PostAsJsonAsync("api/graph/open", new EditorGraphRequest(graph), TestContext.Current.CancellationToken)).EnsureSuccessStatusCode();
            using var stateResponse = await client.GetAsync("api/state", TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.OK, stateResponse.StatusCode);
            using var state = JsonDocument.Parse(await stateResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
            Assert.Contains(state.RootElement.GetProperty("descriptions").GetProperty("prompt").GetProperty("diagnostics").EnumerateArray(), issue => issue.GetProperty("port").GetString() == "score");
            Assert.True(state.RootElement.GetProperty("descriptions").TryGetProperty("count", out _));
        }
    }

    [Fact]
    public async Task ConnectionValidationUsesResolvedTypesWithoutRequiringTheWholeDraftToCompile()
    {
        var (app, client, runtime) = await StartAsync(true);
        await using (app)
        using (client)
        {
            var graph = JsonSerializer.SerializeToElement(new
            {
                version = 4,
                nodes = new object[]
                {
                    new { id = "source", kind = "fixture", settings = new { } },
                    new { id = "branch", kind = "branch", settings = new { @operator = "equal", b = "ready" } },
                    new { id = "accumulate", kind = "accumulate", settings = new { quietSeconds = 1, maxMessages = 5, maxWaitSeconds = 60 } },
                    new { id = "unfinished", kind = "send", settings = new { } }
                },
                edges = Array.Empty<object>(), controlEdges = Array.Empty<object>()
            });
            (await client.PostAsJsonAsync("api/graph/open", new EditorGraphRequest(graph), TestContext.Current.CancellationToken)).EnsureSuccessStatusCode();
            var compatible = new EditorConnectRequest(graph, "data", "source", "value", "branch", "a");
            Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("api/graph/validate-connection", compatible, TestContext.Current.CancellationToken)).StatusCode);
            var incompatible = compatible with { Target = "accumulate", TargetPort = "message" };
            var response = await client.PostAsJsonAsync("api/graph/validate-connection", incompatible, TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains("incompatible", (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)), StringComparison.OrdinalIgnoreCase);
            Assert.True(JsonElement.DeepEquals(graph, runtime.Graph.Graph));
            await app.StopAsync(TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task RegisteredFunctionNavigationOpensReadonlyTargetAndRestoresCallerGraph()
    {
        var (app, client, runtime) = await StartAsync(true, navigationReference: "function://review");
        await using (app)
        using (client)
        {
            var caller = JsonSerializer.SerializeToElement(new
            {
                version = 4, nodes = new object[] { new { id = "call", kind = "fixture", settings = new { } } },
                edges = Array.Empty<object>(), controlEdges = Array.Empty<object>()
            });
            var implementation = JsonSerializer.SerializeToElement(new
            {
                version = 4, nodes = new object[] { new { id = "inside", kind = "fixture", settings = new { }, x = 120, y = 80 } },
                edges = Array.Empty<object>(), controlEdges = Array.Empty<object>()
            });
            runtime.Navigation.Register("function://review", implementation, "inside");
            (await client.PostAsJsonAsync("api/graph/open", new EditorGraphRequest(caller), TestContext.Current.CancellationToken)).EnsureSuccessStatusCode();

            using var opened = await client.PostAsJsonAsync("api/graph/navigate", new
            { graph = caller, entryId = (string?)null, nodeId = "call", reference = "function://review" }, TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.OK, opened.StatusCode);
            using var openedState = JsonDocument.Parse(await opened.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
            Assert.Equal("inside", openedState.RootElement.GetProperty("graph").GetProperty("nodes")[0].GetProperty("id").GetString());
            Assert.True(openedState.RootElement.GetProperty("readOnly").GetBoolean());
            Assert.True(openedState.RootElement.GetProperty("canGoBack").GetBoolean());
            var readonlyEdit = await client.PutAsJsonAsync("api/graph", new EditorGraphRequest(implementation), TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.Forbidden, readonlyEdit.StatusCode);
            using var unknownReference = await client.PostAsJsonAsync("api/graph/navigate", new
            { graph = caller, entryId = (string?)null, nodeId = "call", reference = "file:///not-registered" }, TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.BadRequest, unknownReference.StatusCode);

            using var returned = await client.PostAsync("api/graph/back", null, TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.OK, returned.StatusCode);
            using var restored = JsonDocument.Parse(await returned.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
            Assert.True(JsonElement.DeepEquals(caller, restored.RootElement.GetProperty("graph")));
            Assert.False(restored.RootElement.GetProperty("readOnly").GetBoolean());
            Assert.False(restored.RootElement.GetProperty("canGoBack").GetBoolean());
            Assert.Equal("call", runtime.Graph.Graph.GetProperty("nodes")[0].GetProperty("id").GetString());
        }
    }

    [Fact]
    public async Task OpeningAnotherGraphClearsRegisteredNavigationAndOldSaveAssociation()
    {
        var (app, client, runtime) = await StartAsync(true, navigationReference: "function://edit");
        await using (app)
        using (client)
        {
            var caller = JsonSerializer.SerializeToElement(new
            { version = 4, nodes = new object[] { new { id = "call", kind = "fixture", settings = new { } } }, edges = Array.Empty<object>(), controlEdges = Array.Empty<object>() });
            var implementation = JsonSerializer.SerializeToElement(new
            { version = 4, nodes = new object[] { new { id = "inside", kind = "fixture", settings = new { } } }, edges = Array.Empty<object>(), controlEdges = Array.Empty<object>() });
            var newDocument = JsonSerializer.SerializeToElement(new
            { version = 4, nodes = new object[] { new { id = "new-document", kind = "fixture", settings = new { } } }, edges = Array.Empty<object>(), controlEdges = Array.Empty<object>() });
            var saveCalls = 0;
            runtime.Navigation.Register("function://edit", implementation, "inside", canEdit: true,
                saveAsync: (_, _) => { saveCalls++; return Task.CompletedTask; });
            (await client.PostAsJsonAsync("api/graph/open", new EditorGraphRequest(caller), TestContext.Current.CancellationToken)).EnsureSuccessStatusCode();
            (await client.PostAsJsonAsync("api/graph/navigate", new { graph = caller, entryId = (string?)null, nodeId = "call", reference = "function://edit" }, TestContext.Current.CancellationToken)).EnsureSuccessStatusCode();

            (await client.PostAsJsonAsync("api/graph/open", new EditorGraphRequest(newDocument), TestContext.Current.CancellationToken)).EnsureSuccessStatusCode();
            using var state = await client.GetFromJsonAsync<JsonDocument>("api/state", TestContext.Current.CancellationToken);
            Assert.False(state!.RootElement.GetProperty("canGoBack").GetBoolean());
            Assert.False(state.RootElement.GetProperty("readOnly").GetBoolean());

            using var saved = await client.PostAsync("api/graph/save", null, TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
            using var savedJson = JsonDocument.Parse(await saved.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
            Assert.False(savedJson.RootElement.TryGetProperty("reference", out _));
            Assert.Equal(0, saveCalls);
            using var back = await client.PostAsync("api/graph/back", null, TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.Conflict, back.StatusCode);
        }
    }

    [Fact]
    public async Task SuccessfulEditableFunctionSavePublishesTheSnapshotForLaterNavigation()
    {
        var (app, client, runtime) = await StartAsync(true, navigationReference: "function://edit");
        await using (app)
        using (client)
        {
            var caller = JsonSerializer.SerializeToElement(new
            { version = 4, nodes = new object[] { new { id = "call", kind = "fixture", settings = new { } } }, edges = Array.Empty<object>(), controlEdges = Array.Empty<object>() });
            var original = JsonSerializer.SerializeToElement(new
            { version = 4, nodes = new object[] { new { id = "inside", kind = "fixture", settings = new { label = "before" } } }, edges = Array.Empty<object>(), controlEdges = Array.Empty<object>() });
            var updated = JsonSerializer.SerializeToElement(new
            { version = 4, nodes = new object[] { new { id = "inside", kind = "fixture", settings = new { label = "saved" } } }, edges = Array.Empty<object>(), controlEdges = Array.Empty<object>() });
            JsonElement savedGraph = default;
            runtime.Navigation.Register("function://edit", original, "inside", canEdit: true,
                saveAsync: (graph, _) => { savedGraph = graph.Clone(); return Task.CompletedTask; });
            (await client.PostAsJsonAsync("api/graph/open", new EditorGraphRequest(caller), TestContext.Current.CancellationToken)).EnsureSuccessStatusCode();
            (await client.PostAsJsonAsync("api/graph/navigate", new { graph = caller, entryId = (string?)null, nodeId = "call", reference = "function://edit" }, TestContext.Current.CancellationToken)).EnsureSuccessStatusCode();
            (await client.PutAsJsonAsync("api/graph", new EditorGraphRequest(updated, "inside"), TestContext.Current.CancellationToken)).EnsureSuccessStatusCode();
            (await client.PostAsync("api/graph/save", null, TestContext.Current.CancellationToken)).EnsureSuccessStatusCode();
            Assert.Equal("saved", savedGraph.GetProperty("nodes")[0].GetProperty("settings").GetProperty("label").GetString());
            (await client.PostAsync("api/graph/back", null, TestContext.Current.CancellationToken)).EnsureSuccessStatusCode();
            using var reopened = await client.PostAsJsonAsync("api/graph/navigate", new { graph = caller, entryId = (string?)null, nodeId = "call", reference = "function://edit" }, TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.OK, reopened.StatusCode);
            using var reopenedState = JsonDocument.Parse(await reopened.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
            Assert.Equal("saved", reopenedState.RootElement.GetProperty("graph").GetProperty("nodes")[0].GetProperty("settings").GetProperty("label").GetString());
        }
    }

    [Fact]
    public async Task FailedEditableFunctionSaveKeepsThePreviouslyRegisteredSnapshot()
    {
        var (app, client, runtime) = await StartAsync(true, navigationReference: "function://edit");
        await using (app)
        using (client)
        {
            var caller = JsonSerializer.SerializeToElement(new
            { version = 4, nodes = new object[] { new { id = "call", kind = "fixture", settings = new { } } }, edges = Array.Empty<object>(), controlEdges = Array.Empty<object>() });
            var original = JsonSerializer.SerializeToElement(new
            { version = 4, nodes = new object[] { new { id = "inside", kind = "fixture", settings = new { label = "before" } } }, edges = Array.Empty<object>(), controlEdges = Array.Empty<object>() });
            var unsaved = JsonSerializer.SerializeToElement(new
            { version = 4, nodes = new object[] { new { id = "inside", kind = "fixture", settings = new { label = "failed-save" } } }, edges = Array.Empty<object>(), controlEdges = Array.Empty<object>() });
            runtime.Navigation.Register("function://edit", original, "inside", canEdit: true,
                saveAsync: (_, _) => throw new IOException("host save failed"));
            (await client.PostAsJsonAsync("api/graph/open", new EditorGraphRequest(caller), TestContext.Current.CancellationToken)).EnsureSuccessStatusCode();
            (await client.PostAsJsonAsync("api/graph/navigate", new { graph = caller, entryId = (string?)null, nodeId = "call", reference = "function://edit" }, TestContext.Current.CancellationToken)).EnsureSuccessStatusCode();
            (await client.PutAsJsonAsync("api/graph", new EditorGraphRequest(unsaved, "inside"), TestContext.Current.CancellationToken)).EnsureSuccessStatusCode();
            using var failedSave = await client.PostAsync("api/graph/save", null, TestContext.Current.CancellationToken);
            Assert.False(failedSave.IsSuccessStatusCode);
            (await client.PostAsync("api/graph/back", null, TestContext.Current.CancellationToken)).EnsureSuccessStatusCode();
            using var reopened = await client.PostAsJsonAsync("api/graph/navigate", new { graph = caller, entryId = (string?)null, nodeId = "call", reference = "function://edit" }, TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.OK, reopened.StatusCode);
            using var reopenedState = JsonDocument.Parse(await reopened.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
            Assert.Equal("before", reopenedState.RootElement.GetProperty("graph").GetProperty("nodes")[0].GetProperty("settings").GetProperty("label").GetString());
        }
    }

    [Fact]
    public async Task ReadonlyNestedNavigationUsesTheRegisteredCallerGraphAndEntry()
    {
        var (app, client, runtime) = await StartAsync(true, navigationReference: "function://review");
        await using (app)
        using (client)
        {
            var caller = JsonSerializer.SerializeToElement(new
            { version = 4, nodes = new object[] { new { id = "call", kind = "fixture", settings = new { } } }, edges = Array.Empty<object>(), controlEdges = Array.Empty<object>() });
            var registeredCaller = JsonSerializer.SerializeToElement(new
            { version = 4, nodes = new object[] { new { id = "nested-call", kind = "fixture", settings = new { source = "host" }, x = 20, y = 30 } }, edges = Array.Empty<object>(), controlEdges = Array.Empty<object>() });
            var modifiedReadonlyCaller = JsonSerializer.SerializeToElement(new
            { version = 4, nodes = new object[] { new { id = "nested-call", kind = "fixture", settings = new { source = "forged" }, x = 999, y = -500 } }, edges = Array.Empty<object>(), controlEdges = Array.Empty<object>() });
            var forgedOnlyGraph = JsonSerializer.SerializeToElement(new
            { version = 4, nodes = new object[] { new { id = "forged-call", kind = "fixture", settings = new { } } }, edges = Array.Empty<object>(), controlEdges = Array.Empty<object>() });
            runtime.Navigation.Register("function://review", registeredCaller, "trusted-entry");
            (await client.PostAsJsonAsync("api/graph/open", new EditorGraphRequest(caller), TestContext.Current.CancellationToken)).EnsureSuccessStatusCode();
            (await client.PostAsJsonAsync("api/graph/navigate", new { graph = caller, entryId = (string?)null, nodeId = "call", reference = "function://review" }, TestContext.Current.CancellationToken)).EnsureSuccessStatusCode();

            using var forgedCall = await client.PostAsJsonAsync("api/graph/navigate", new { graph = forgedOnlyGraph, entryId = "forged-entry", nodeId = "forged-call", reference = "function://review" }, TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.BadRequest, forgedCall.StatusCode);
            using var nested = await client.PostAsJsonAsync("api/graph/navigate", new { graph = modifiedReadonlyCaller, entryId = "forged-entry", nodeId = "nested-call", reference = "function://review" }, TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.OK, nested.StatusCode);
            (await client.PostAsync("api/graph/back", null, TestContext.Current.CancellationToken)).EnsureSuccessStatusCode();
            using var restored = await client.GetFromJsonAsync<JsonDocument>("api/state", TestContext.Current.CancellationToken);
            Assert.Equal("trusted-entry", restored!.RootElement.GetProperty("entryId").GetString());
            Assert.True(JsonElement.DeepEquals(registeredCaller, restored.RootElement.GetProperty("graph")));
            Assert.True(restored.RootElement.GetProperty("readOnly").GetBoolean());
        }
    }

    [Fact]
    public async Task UseModeRootNavigationUsesTheHostTemplateInsteadOfTheRequestGraph()
    {
        var (app, client, runtime) = await StartAsync(true, useMode: true, navigationReference: "function://review");
        await using (app)
        using (client)
        {
            var template = runtime.Graph.Graph.Clone();
            var implementation = JsonSerializer.SerializeToElement(new
            { version = 4, nodes = new object[] { new { id = "inside", kind = "fixture", settings = new { } } }, edges = Array.Empty<object>(), controlEdges = Array.Empty<object>() });
            runtime.Navigation.Register("function://review", implementation, "inside");
            var forged = JsonSerializer.SerializeToElement(new
            { version = 4, nodes = new object[] { new { id = "input", kind = "fixture", settings = new { forged = true }, x = 999, y = -500 } }, edges = Array.Empty<object>(), controlEdges = Array.Empty<object>() });

            using var opened = await client.PostAsJsonAsync("api/graph/navigate", new
            { graph = forged, entryId = "forged-entry", nodeId = "input", reference = "function://review" }, TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.OK, opened.StatusCode);
            using var back = await client.PostAsync("api/graph/back", null, TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.OK, back.StatusCode);
            using var restored = JsonDocument.Parse(await back.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
            Assert.True(JsonElement.DeepEquals(template, restored.RootElement.GetProperty("graph")));
            Assert.Equal("input", restored.RootElement.GetProperty("entryId").GetString());
            Assert.True(JsonElement.DeepEquals(template, runtime.Graph.Graph));
        }
    }

    [Fact]
    public async Task UseModeStateKeepsResolvedRootEntryOnlyAtNavigationRoot()
    {
        var (app, client, runtime) = await StartAsync(true, useMode: true, navigationReference: "function://review", useEntryId: null);
        await using (app)
        using (client)
        {
            Assert.Null(runtime.Graph.EntryId);
            Assert.Equal("input", runtime.Use!.EntryId);

            using var initial = await client.GetFromJsonAsync<JsonDocument>("api/state", TestContext.Current.CancellationToken);
            Assert.Equal("input", initial!.RootElement.GetProperty("entryId").GetString());
            (await client.PostAsync("api/use/reset", null, TestContext.Current.CancellationToken)).EnsureSuccessStatusCode();
            using var reset = await client.GetFromJsonAsync<JsonDocument>("api/state", TestContext.Current.CancellationToken);
            Assert.Equal("input", reset!.RootElement.GetProperty("entryId").GetString());

            var root = runtime.Graph.Graph.Clone();
            var child = JsonSerializer.SerializeToElement(new
            { version = 4, nodes = new object[] { new { id = "child", kind = "fixture", settings = new { } } }, edges = Array.Empty<object>(), controlEdges = Array.Empty<object>() });
            runtime.Navigation.Register("function://review", child, "child-entry");
            using var opened = await client.PostAsJsonAsync("api/graph/navigate", new
            { graph = root, entryId = (string?)null, nodeId = "input", reference = "function://review" }, TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.OK, opened.StatusCode);
            using var childState = JsonDocument.Parse(await opened.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
            Assert.Equal("child-entry", childState.RootElement.GetProperty("entryId").GetString());
            (await client.PostAsync("api/graph/back", null, TestContext.Current.CancellationToken)).EnsureSuccessStatusCode();
            using var returned = await client.GetFromJsonAsync<JsonDocument>("api/state", TestContext.Current.CancellationToken);
            Assert.Equal("input", returned!.RootElement.GetProperty("entryId").GetString());
        }
    }

    [Fact]
    public async Task UseModeStateDoesNotInheritRootEntryWhenChildHasNoEntry()
    {
        var (app, client, runtime) = await StartAsync(true, useMode: true, navigationReference: "function://review", useEntryId: null);
        await using (app)
        using (client)
        {
            var root = runtime.Graph.Graph.Clone();
            var child = JsonSerializer.SerializeToElement(new
            { version = 4, nodes = new object[] { new { id = "child", kind = "fixture", settings = new { } } }, edges = Array.Empty<object>(), controlEdges = Array.Empty<object>() });
            runtime.Navigation.Register("function://review", child, entryId: null);
            using var opened = await client.PostAsJsonAsync("api/graph/navigate", new
            { graph = root, entryId = (string?)null, nodeId = "input", reference = "function://review" }, TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.OK, opened.StatusCode);
            using var childState = JsonDocument.Parse(await opened.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
            Assert.Equal(JsonValueKind.Null, childState.RootElement.GetProperty("entryId").ValueKind);
            (await client.PostAsync("api/graph/back", null, TestContext.Current.CancellationToken)).EnsureSuccessStatusCode();
            using var returned = await client.GetFromJsonAsync<JsonDocument>("api/state", TestContext.Current.CancellationToken);
            Assert.Equal("input", returned!.RootElement.GetProperty("entryId").GetString());
        }
    }

    [Fact]
    public async Task UseModeEditableNestedNavigationStillUsesTheRegisteredHostSnapshot()
    {
        var (app, client, runtime) = await StartAsync(true, useMode: true, navigationReference: "function://review");
        await using (app)
        using (client)
        {
            var root = runtime.Graph.Graph.Clone();
            var implementation = JsonSerializer.SerializeToElement(new
            { version = 4, nodes = new object[] { new { id = "inside", kind = "fixture", settings = new { trusted = true }, x = 20, y = 30 } }, edges = Array.Empty<object>(), controlEdges = Array.Empty<object>() });
            var forged = JsonSerializer.SerializeToElement(new
            { version = 4, nodes = new object[] { new { id = "inside", kind = "fixture", settings = new { trusted = false }, x = 900, y = -400 } }, edges = Array.Empty<object>(), controlEdges = Array.Empty<object>() });
            runtime.Navigation.Register("function://review", implementation, "inside-entry", canEdit: true,
                saveAsync: (_, _) => Task.CompletedTask);
            (await client.PostAsJsonAsync("api/graph/navigate", new
            { graph = root, entryId = "forged-root", nodeId = "input", reference = "function://review" }, TestContext.Current.CancellationToken)).EnsureSuccessStatusCode();
            using var nested = await client.PostAsJsonAsync("api/graph/navigate", new
            { graph = forged, entryId = "forged-entry", nodeId = "inside", reference = "function://review" }, TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.OK, nested.StatusCode);
            using var nestedState = JsonDocument.Parse(await nested.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
            Assert.True(nestedState.RootElement.GetProperty("readOnly").GetBoolean());
            (await client.PostAsync("api/graph/back", null, TestContext.Current.CancellationToken)).EnsureSuccessStatusCode();
            using var restoredTarget = await client.GetFromJsonAsync<JsonDocument>("api/state", TestContext.Current.CancellationToken);
            Assert.True(JsonElement.DeepEquals(implementation, restoredTarget!.RootElement.GetProperty("graph")));
            Assert.Equal("inside-entry", restoredTarget.RootElement.GetProperty("entryId").GetString());
            (await client.PostAsync("api/graph/back", null, TestContext.Current.CancellationToken)).EnsureSuccessStatusCode();
            using var restoredRoot = await client.GetFromJsonAsync<JsonDocument>("api/state", TestContext.Current.CancellationToken);
            Assert.True(JsonElement.DeepEquals(root, restoredRoot!.RootElement.GetProperty("graph")));
            Assert.Equal("input", restoredRoot.RootElement.GetProperty("entryId").GetString());
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PendingFunctionSavePublishesItsCapturedSnapshotAfterBackOrOpen(bool openAnotherGraph)
    {
        var (app, client, runtime) = await StartAsync(true, navigationReference: "function://edit");
        await using (app)
        using (client)
        {
            var caller = JsonSerializer.SerializeToElement(new
            { version = 4, nodes = new object[] { new { id = "call", kind = "fixture", settings = new { } } }, edges = Array.Empty<object>(), controlEdges = Array.Empty<object>() });
            var otherCaller = JsonSerializer.SerializeToElement(new
            { version = 4, nodes = new object[] { new { id = "other-call", kind = "fixture", settings = new { } } }, edges = Array.Empty<object>(), controlEdges = Array.Empty<object>() });
            var original = JsonSerializer.SerializeToElement(new
            { version = 4, nodes = new object[] { new { id = "inside", kind = "fixture", settings = new { label = "before" } } }, edges = Array.Empty<object>(), controlEdges = Array.Empty<object>() });
            var savedB = JsonSerializer.SerializeToElement(new
            { version = 4, nodes = new object[] { new { id = "inside", kind = "fixture", settings = new { label = "saved-B" } } }, edges = Array.Empty<object>(), controlEdges = Array.Empty<object>() });
            var saveStarted = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseSave = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            runtime.Navigation.Register("function://edit", original, "before-entry", canEdit: true,
                saveAsync: async (graph, token) =>
                {
                    saveStarted.TrySetResult(graph.Clone());
                    await releaseSave.Task.WaitAsync(token);
                });
            (await client.PostAsJsonAsync("api/graph/open", new EditorGraphRequest(caller), TestContext.Current.CancellationToken)).EnsureSuccessStatusCode();
            (await client.PostAsJsonAsync("api/graph/navigate", new { graph = caller, entryId = (string?)null, nodeId = "call", reference = "function://edit" }, TestContext.Current.CancellationToken)).EnsureSuccessStatusCode();
            (await client.PutAsJsonAsync("api/graph", new EditorGraphRequest(savedB, "saved-entry"), TestContext.Current.CancellationToken)).EnsureSuccessStatusCode();

            var saveRequest = client.PostAsync("api/graph/save", null, TestContext.Current.CancellationToken);
            var callbackSnapshot = await saveStarted.Task.WaitAsync(TestContext.Current.CancellationToken);
            Assert.True(JsonElement.DeepEquals(savedB, callbackSnapshot));
            if (openAnotherGraph)
                (await client.PostAsJsonAsync("api/graph/open", new EditorGraphRequest(otherCaller), TestContext.Current.CancellationToken)).EnsureSuccessStatusCode();
            else
                (await client.PostAsync("api/graph/back", null, TestContext.Current.CancellationToken)).EnsureSuccessStatusCode();
            releaseSave.SetResult();
            using var saveResponse = await saveRequest;
            Assert.Equal(HttpStatusCode.OK, saveResponse.StatusCode);

            var currentCaller = openAnotherGraph ? otherCaller : caller;
            var currentCallId = openAnotherGraph ? "other-call" : "call";
            using var reopened = await client.PostAsJsonAsync("api/graph/navigate", new
            { graph = currentCaller, entryId = (string?)null, nodeId = currentCallId, reference = "function://edit" }, TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.OK, reopened.StatusCode);
            using var reopenedState = JsonDocument.Parse(await reopened.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
            Assert.True(JsonElement.DeepEquals(savedB, reopenedState.RootElement.GetProperty("graph")));
            Assert.Equal("saved-entry", reopenedState.RootElement.GetProperty("entryId").GetString());
        }
    }

    [Fact]
    public void DescriptionsReportInvalidSavedWiresAndOnlyExposeEditableHostDefaults()
    {
        var source = new NodeDefinition("source", 1, FlowEffect.Pure,
            dataOutputs: [new("text", new StringType())], controlOutputs: [new("then")])
        { ExecutionMode = FlowNodeExecutionMode.Entry };
        var host = new NodeDefinition("host", 1, FlowEffect.Pure, dataInputs: [new("limit", new NumberType())])
        { ExecutionMode = FlowNodeExecutionMode.Pure };
        var sink = new NodeDefinition("sink", 1, FlowEffect.Pure, dataInputs: [new("value", new NumberType())])
        { ExecutionMode = FlowNodeExecutionMode.Pure };
        var graph = JsonSerializer.SerializeToElement(new
        {
            version = 4,
            nodes = new object[]
            {
                new { id = "source", kind = "source", settings = new { } },
                new { id = "host", kind = "host", settings = new { inputValues = new { limit = 5 } } },
                new { id = "sink", kind = "sink", settings = new { } },
                new { id = "branch", kind = "branch", settings = new { @operator = "equal", b = "ready" } }
            },
            edges = new object[]
            {
                new { source = "source", sourcePort = "text", target = "sink", targetPort = "value" },
                new { source = "source", sourcePort = "text", target = "branch", targetPort = "a" }
            },
            controlEdges = Array.Empty<object>()
        });
        var json = JsonSerializer.SerializeToElement(EditorCatalog.DescriptionsDefinitions(graph, [source, host, sink]));
        var hostPort = json.GetProperty("host").GetProperty("ports").EnumerateArray().Single(port => port.GetProperty("id").GetString() == "limit");
        Assert.True(hostPort.GetProperty("hasDefault").GetBoolean());
        Assert.True(hostPort.GetProperty("editableDefault").GetBoolean());
        var branchPort = json.GetProperty("branch").GetProperty("ports").EnumerateArray().Single(port => port.GetProperty("id").GetString() == "b");
        Assert.True(branchPort.GetProperty("hasDefault").GetBoolean());
        Assert.False(branchPort.GetProperty("editableDefault").GetBoolean());
        var diagnostic = json.GetProperty("sink").GetProperty("wireDiagnostics").EnumerateArray().Single();
        Assert.Contains("incompatible", diagnostic.GetProperty("reason").GetString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EditorCatalogExposesNullableDictionaryDefaultsOnlyForSupportedHostInputs()
    {
        var type = new NullableType(new DictionaryType(new IntegerType()));
        var host = new NodeDefinition("budgetHost", 1, FlowEffect.Pure,
            dataInputs: [new("budget", type)]) { ExecutionMode = FlowNodeExecutionMode.Pure };
        JsonElement Describe(object settings)
        {
            var graph = JsonSerializer.SerializeToElement(new
            {
                version = 4,
                nodes = new object[]
                {
                    new { id = "host", kind = "budgetHost", settings },
                    new { id = "branch", kind = "branch", settings = new { @operator = "equal", b = "ready" } }
                },
                edges = Array.Empty<object>(), controlEdges = Array.Empty<object>()
            });
            return JsonSerializer.SerializeToElement(EditorCatalog.DescriptionsDefinitions(graph, [host]));
        }

        var withoutDefault = Describe(new { });
        var hostPort = withoutDefault.GetProperty("host").GetProperty("ports").EnumerateArray()
            .Single(port => port.GetProperty("id").GetString() == "budget");
        Assert.True(hostPort.GetProperty("editableDefault").GetBoolean());
        Assert.False(hostPort.GetProperty("hasDefault").GetBoolean());
        Assert.Contains("object", hostPort.GetProperty("schema").GetProperty("type").EnumerateArray().Select(item => item.GetString()));
        Assert.False(withoutDefault.GetProperty("branch").GetProperty("ports").EnumerateArray()
            .Single(port => port.GetProperty("id").GetString() == "b").GetProperty("editableDefault").GetBoolean());

        using var validValue = JsonDocument.Parse("""{"Model A":8192,"gpt-6.1-sol":4096}""");
        var withValidDefault = Describe(new { inputValues = new Dictionary<string, JsonElement> { ["budget"] = validValue.RootElement.Clone() } });
        hostPort = withValidDefault.GetProperty("host").GetProperty("ports").EnumerateArray()
            .Single(port => port.GetProperty("id").GetString() == "budget");
        Assert.True(hostPort.GetProperty("editableDefault").GetBoolean());
        Assert.True(hostPort.GetProperty("hasDefault").GetBoolean());
        Assert.Equal(validValue.RootElement.GetRawText(), hostPort.GetProperty("value").GetRawText());

        var withInvalidDefault = Describe(new { inputValues = new { budget = "not a dictionary" } });
        hostPort = withInvalidDefault.GetProperty("host").GetProperty("ports").EnumerateArray()
            .Single(port => port.GetProperty("id").GetString() == "budget");
        Assert.True(hostPort.GetProperty("editableDefault").GetBoolean());
        Assert.False(hostPort.GetProperty("hasDefault").GetBoolean());
        Assert.NotEmpty(withInvalidDefault.GetProperty("host").GetProperty("diagnostics").EnumerateArray());
    }

    [Fact]
    public void EditorCatalogExposesAndValidatesNativeJsonDefaultsFromRealMetadata()
    {
        var host = new NodeDefinition("jsonHost", 1, FlowEffect.Pure,
            dataInputs: [new("payload", new JsonType())]) { ExecutionMode = FlowNodeExecutionMode.Pure };
        JsonElement Describe(JsonElement value)
        {
            var graph = JsonSerializer.SerializeToElement(new
            {
                version = 4,
                nodes = new[] { new { id = "host", kind = "jsonHost", settings = new { inputValues = new Dictionary<string, JsonElement> { ["payload"] = value } } } },
                edges = Array.Empty<object>(), controlEdges = Array.Empty<object>()
            });
            return JsonSerializer.SerializeToElement(EditorCatalog.DescriptionsDefinitions(graph, [host]));
        }

        foreach (var json in new[] { "null", "false", "19", "\"raw\"", "[1,null]", "{\"unexpected\":true}" })
        {
            using var value = JsonDocument.Parse(json);
            var described = Describe(value.RootElement.Clone()).GetProperty("host");
            var port = described.GetProperty("ports").EnumerateArray().Single(item => item.GetProperty("id").GetString() == "payload");
            Assert.True(port.GetProperty("editableDefault").GetBoolean());
            Assert.True(port.GetProperty("hasDefault").GetBoolean());
            Assert.Equal("json", port.GetProperty("type").GetString());
            Assert.Equal("{}", port.GetProperty("schema").GetRawText());
            Assert.True(JsonElement.DeepEquals(value.RootElement, port.GetProperty("value")));
            Assert.Empty(described.GetProperty("diagnostics").EnumerateArray());
        }
    }

    [Fact]
    public async Task SessionAndSameOriginAreRequired()
    {
        var (app, client, _) = await StartAsync(false);
        await using (app)
        using (client)
        {
            using var anonymous = new HttpClient { BaseAddress = client.BaseAddress };
            Assert.Equal(HttpStatusCode.Forbidden, (await anonymous.GetAsync("api/state", TestContext.Current.CancellationToken)).StatusCode);
            client.DefaultRequestHeaders.Remove("Origin");
            client.DefaultRequestHeaders.Add("Origin", "http://foreign.invalid");
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("api/graph/open", new EditorGraphRequest(EditorRuntimeTests.Graph("fixture")), TestContext.Current.CancellationToken)).StatusCode);
            client.DefaultRequestHeaders.Remove("Origin");
            client.DefaultRequestHeaders.Add("Origin", client.BaseAddress!.GetLeftPart(UriPartial.Authority));
            Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("api/graph/open", new EditorGraphRequest(EditorRuntimeTests.Graph("fixture")), TestContext.Current.CancellationToken)).StatusCode);
            await app.StopAsync(TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task SavedPluginChangeIsPendingRestartAndDoesNotLoadDll()
    {
        var (app, client, runtime) = await StartAsync(false);
        await using (app)
        using (client)
        {
            Assert.Empty(runtime.Plugins.Definitions);
            var response = await client.PutAsJsonAsync("api/settings", new CortexEditorSettings
            { Plugins = [new() { Id = "fixture", Enabled = true }] }, TestContext.Current.CancellationToken);
            response.EnsureSuccessStatusCode();
            Assert.True(runtime.PendingRestart);
            Assert.Empty(runtime.Plugins.Definitions);
            Assert.False(runtime.Plugins.Reports.Single().Loaded);
            Assert.True((await runtime.SettingsStore.ReadAsync(TestContext.Current.CancellationToken)).Settings.Plugins.Single().Enabled);
            await app.StopAsync(TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task RunRejectsEditsAndCancelReleasesIt()
    {
        var (app, client, runtime) = await StartAsync(true, wait: true);
        await using (app)
        using (client)
        {
            (await client.PostAsJsonAsync("api/graph/open", new EditorGraphRequest(EditorRuntimeTests.Graph("fixture"), "input"), TestContext.Current.CancellationToken)).EnsureSuccessStatusCode();
            (await client.PostAsync("api/run", null, TestContext.Current.CancellationToken)).EnsureSuccessStatusCode();
            Assert.True(runtime.Execution.Busy);
            Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsJsonAsync("api/graph", new EditorGraphRequest(EditorRuntimeTests.Graph("fixture"), "input"), TestContext.Current.CancellationToken)).StatusCode);
            Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsJsonAsync("api/settings", new CortexEditorSettings(), TestContext.Current.CancellationToken)).StatusCode);
            (await client.PostAsync("api/run/cancel", null, TestContext.Current.CancellationToken)).EnsureSuccessStatusCode();
            await runtime.Execution.WaitAsync(TestContext.Current.CancellationToken);
            Assert.Equal("cancelled", runtime.Execution.Snapshot().State);
            Assert.False(runtime.Execution.Busy);
            await app.StopAsync(TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task CorruptSettingsStartWithErrorAndRemainUnchanged()
    {
        var root = Path.Combine(Path.GetTempPath(), "cortex-corrupt-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "settings.json");
        await File.WriteAllTextAsync(path, "broken", TestContext.Current.CancellationToken);
        await using var app = await EditorHost.BuildAsync(new(SettingsPath: path, InstallationDirectory: root, OpenBrowser: false), TestContext.Current.CancellationToken);
        Assert.NotNull(app.Services.GetRequiredService<EditorRuntime>().SettingsError);
        Assert.Equal("broken", await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task UseModeLocksGraphAndSettingsEvenWhenRequestsBypassTheInterface()
    {
        var (app, client, runtime) = await StartAsync(true, useMode: true);
        await using(app)
        using(client)
        {
            var original = runtime.Graph.Graph.GetRawText();
            Assert.Equal(HttpStatusCode.Forbidden,(await client.PutAsJsonAsync("api/graph",new EditorGraphRequest(EditorRuntimeTests.Graph("fixture")),TestContext.Current.CancellationToken)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden,(await client.PostAsync("api/graph/save",null,TestContext.Current.CancellationToken)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden,(await client.PostAsJsonAsync("api/graph/open",new EditorGraphRequest(EditorRuntimeTests.Graph("fixture")),TestContext.Current.CancellationToken)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden,(await client.PutAsJsonAsync("api/settings",new CortexEditorSettings(),TestContext.Current.CancellationToken)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden,(await client.PostAsync("api/run",null,TestContext.Current.CancellationToken)).StatusCode);
            (await client.PostAsJsonAsync("api/use/step",new UseStepRequest(),TestContext.Current.CancellationToken)).EnsureSuccessStatusCode();
            await runtime.Use!.WaitAsync(TestContext.Current.CancellationToken);
            Assert.Equal("completed",runtime.Use.Snapshot().State);
            Assert.Equal(original,runtime.Graph.Graph.GetRawText());
            await app.StopAsync(TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task ApplicationRootServesConsumerOwnedPageThroughTheSameRuntime()
    {
        var (app,client,runtime)=await StartAsync(true,useMode:true,application:true);
        await using(app)
        using(client)
        {
            var page=await client.GetStringAsync("/",TestContext.Current.CancellationToken);
            Assert.Contains("Consumer-owned UI",page);
            Assert.DoesNotContain("Graph canvas",page);
            Assert.True(runtime.UseMode);
            Assert.Equal(HttpStatusCode.Forbidden,(await client.PostAsync("api/graph/save",null,TestContext.Current.CancellationToken)).StatusCode);
            await app.StopAsync(TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task PublicTunnelRequiresLockedUseModeAndKeepsSessionAuthentication()
    {
        const string origin = "https://slogan.ngrok-free.dev";
        await Assert.ThrowsAsync<ArgumentException>(() => EditorHost.BuildAsync(new(PublicOrigin: origin), TestContext.Current.CancellationToken));
        var (app, client, _) = await StartAsync(true, useMode: true, application: true, publicOrigin: origin);
        await using (app)
        using (client)
        {
            client.DefaultRequestHeaders.Host = "slogan.ngrok-free.dev";
            client.DefaultRequestHeaders.Remove("Origin");
            client.DefaultRequestHeaders.Add("Origin", origin);
            (await client.GetAsync("api/state", TestContext.Current.CancellationToken)).EnsureSuccessStatusCode();
            (await client.PostAsync("api/use/reset", null, TestContext.Current.CancellationToken)).EnsureSuccessStatusCode();
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync("api/graph/save", null, TestContext.Current.CancellationToken)).StatusCode);
            client.DefaultRequestHeaders.Remove("Origin");
            client.DefaultRequestHeaders.Add("Origin", "https://foreign.invalid");
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync("api/use/reset", null, TestContext.Current.CancellationToken)).StatusCode);
            client.DefaultRequestHeaders.Remove("X-Cortex-Session");
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("api/state", TestContext.Current.CancellationToken)).StatusCode);
            await app.StopAsync(TestContext.Current.CancellationToken);
        }
    }

    private static async Task<(WebApplication App, HttpClient Client, EditorRuntime Runtime)> StartAsync(bool enabled, bool wait = false, bool useMode = false, bool application = false, string? publicOrigin = null, bool runtimeFactoryThrows = false, string? navigationReference = null, string? useEntryId = "input")
    {
        var root = Path.Combine(Path.GetTempPath(), "cortex-api-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "plugins"));
        var manifest = new NodePluginManifest
        {
            Id = "fixture", DisplayName = "Fixture", EntryType = "Cortex.Plugin.TestFixture.FixtureNodePlugin",
            AssemblyPath = Path.Combine(AppContext.BaseDirectory, "plugin-fixture", "Cortex.Plugin.TestFixture.dll")
        };
        await File.WriteAllTextAsync(Path.Combine(root, "plugins", "fixture.plugin.json"), JsonSerializer.Serialize(manifest, CortexSettingsStore.JsonOptions), TestContext.Current.CancellationToken);
        var store = new CortexSettingsStore(Path.Combine(root, "settings.json"));
        await store.SaveAsync(new() { Plugins = [new() { Id = "fixture", Enabled = enabled, Configuration = JsonSerializer.SerializeToElement(new { wait, runtimeFactoryThrows, navigationReference }) }] }, TestContext.Current.CancellationToken);
        var graphPath = Path.Combine(root, "graph.json");
        if (useMode) await File.WriteAllTextAsync(graphPath, EditorRuntimeTests.Graph("fixture").GetRawText(), TestContext.Current.CancellationToken);
        var applicationRoot=Path.Combine(root,"application");
        if(application){Directory.CreateDirectory(applicationRoot);await File.WriteAllTextAsync(Path.Combine(applicationRoot,"index.html"),"<html><body>Consumer-owned UI</body></html>",TestContext.Current.CancellationToken);}
        var app = await EditorHost.BuildAsync(new(GraphPath: useMode ? graphPath : null, EntryId: useMode ? useEntryId : null, SettingsPath: store.FilePath, InstallationDirectory: root, OpenBrowser: false, UseMode: useMode, ApplicationRoot: application?applicationRoot:null, PublicOrigin: publicOrigin), TestContext.Current.CancellationToken);
        await app.StartAsync(TestContext.Current.CancellationToken);
        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        var client = new HttpClient { BaseAddress = new Uri(address) };
        client.DefaultRequestHeaders.Add("X-Cortex-Session", app.Services.GetRequiredService<EditorSession>().Token);
        client.DefaultRequestHeaders.Add("Origin", address);
        return (app, client, app.Services.GetRequiredService<EditorRuntime>());
    }
}
