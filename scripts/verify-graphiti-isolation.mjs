import assert from "node:assert/strict";

const endpoint = process.env.GRAPHITI_ENDPOINT ?? "http://127.0.0.1:8000";
const groupA = process.argv[2];
const groupB = process.argv[3];
const expectedText = process.argv[4] ?? "Zerei Shadow of Mordor";

if (!groupA || !groupB) {
  throw new Error(
    "Usage: node scripts/verify-graphiti-isolation.mjs <group-a> <group-b> [expected-text]",
  );
}

let requestId = 1;
let sessionId;

async function readJsonResponse(response) {
  const contentType = response.headers.get("content-type") ?? "";
  const body = await response.text();
  if (!body) return undefined;
  if (!contentType.includes("text/event-stream")) return JSON.parse(body);

  const data = body
    .split("\n")
    .map((line) => line.replace(/\r$/, ""))
    .find((line) => line.startsWith("data:"));
  if (!data) throw new Error("Graphiti MCP returned no SSE data");
  return JSON.parse(data.slice(5).trim());
}

async function post(payload) {
  const headers = {
    "Content-Type": "application/json",
    Accept: "application/json, text/event-stream",
  };
  if (sessionId) headers["mcp-session-id"] = sessionId;

  const response = await fetch(`${endpoint}/mcp`, {
    method: "POST",
    headers,
    body: JSON.stringify(payload),
  });
  if (!response.ok) throw new Error(`Graphiti MCP HTTP ${response.status}`);
  return { response, body: await readJsonResponse(response) };
}

async function initialize() {
  const result = await post({
    jsonrpc: "2.0",
    id: requestId++,
    method: "initialize",
    params: {
      protocolVersion: "2025-11-25",
      capabilities: {},
      clientInfo: { name: "graphiti-isolation-check", version: "1.0.0" },
    },
  });
  sessionId = result.response.headers.get("mcp-session-id") ?? undefined;
  await post({ jsonrpc: "2.0", method: "notifications/initialized" });
}

function parseEpisodes(result) {
  if (result.error) throw new Error(result.error.message);
  const content = result.result?.content?.[0];
  if (content?.type !== "text") throw new Error("Unexpected Graphiti MCP result");
  const parsed = JSON.parse(content.text);
  if (parsed.error) throw new Error(parsed.error);
  return parsed.episodes ?? [];
}

async function getEpisodes(groupId) {
  const { body } = await post({
    jsonrpc: "2.0",
    id: requestId++,
    method: "tools/call",
    params: {
      name: "get_episodes",
      arguments: { group_ids: [groupId], max_episodes: 100 },
    },
  });
  return parseEpisodes(body);
}

await initialize();
const episodesA = await getEpisodes(groupA);
const episodesB = await getEpisodes(groupB);
const foundInA = episodesA.some((episode) => episode.content?.includes(expectedText));
const foundInB = episodesB.some((episode) => episode.content?.includes(expectedText));

assert.equal(foundInA, true, `Expected text was not found in group A: ${groupA}`);
assert.equal(foundInB, false, `Expected text leaked into group B: ${groupB}`);

console.log(`group A: ${groupA} -> ${episodesA.length} episode(s), expected text found`);
console.log(`group B: ${groupB} -> ${episodesB.length} episode(s), expected text absent`);
