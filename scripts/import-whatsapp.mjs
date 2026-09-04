import { readdir, readFile } from "node:fs/promises";
import { basename, dirname, extname, join, resolve } from "node:path";
import { spawnSync } from "node:child_process";
import { fileURLToPath } from "node:url";

const positionalArgs = process.argv.slice(2).filter((arg) => !arg.startsWith("--"));
const SCRIPT_DIR = dirname(fileURLToPath(import.meta.url));
const EXPORT_DIR = resolve(positionalArgs[0] ?? SCRIPT_DIR);
const DRY_RUN = process.argv.includes("--dry-run");
const NO_AUTHORIZE = process.argv.includes("--no-authorize");
const ENDPOINT = "http://127.0.0.1:8000";

function slugify(value) {
  return value
    .normalize("NFKD")
    .replace(/[\u0300-\u036f]/g, "")
    .replace(/[^a-zA-Z0-9]+/g, "-")
    .replace(/^-+|-+$/g, "")
    .toLowerCase()
    .slice(0, 80) || "conversa";
}

function parseExport(text) {
  const messages = [];
  let current = null;
  const linePattern = /^(?:\[)?(\d{1,2}\/\d{1,2}\/\d{2,4}),?\s+(\d{1,2}:\d{2}(?::\d{2})?\s*(?:AM|PM)?)\s*(?:\])?\s*(?:-\s*)?(.*)$/i;

  for (const rawLine of text.replace(/^\uFEFF/, "").split(/\r?\n/)) {
    const match = rawLine.match(linePattern);
    if (!match) {
      if (current && rawLine.length > 0) current.text += `\n${rawLine}`;
      continue;
    }

    if (current) messages.push(current);
    const [, date, time, remainder] = match;
    const separator = remainder.indexOf(": ");
    const hasSpeaker = separator > 0;
    current = {
      date,
      time,
      speaker: hasSpeaker ? remainder.slice(0, separator) : "[sistema]",
      text: hasSpeaker ? remainder.slice(separator + 2) : remainder,
    };
  }
  if (current) messages.push(current);
  return messages;
}

function renderMessages(messages) {
  return messages
    .map((m) => `[${m.date} ${m.time}] ${m.speaker}: ${m.text}`)
    .join("\n");
}

function chunks(messages, maxChars = 18000) {
  const result = [];
  let current = [];
  let size = 0;
  for (const message of messages) {
    const rendered = renderMessages([message]);
    if (current.length > 0 && size + rendered.length + 1 > maxChars) {
      result.push(current);
      current = [];
      size = 0;
    }
    current.push(message);
    size += rendered.length + 1;
  }
  if (current.length > 0) result.push(current);
  return result;
}

async function readJsonResponse(response) {
  const type = response.headers.get("content-type") ?? "";
  if (type.includes("text/event-stream")) {
    const text = await response.text();
    const line = text.split("\n").find((item) => item.startsWith("data: "));
    if (!line) throw new Error("Graphiti MCP returned no SSE data");
    return JSON.parse(line.slice(6).trim());
  }
  return response.json();
}

let requestId = 1;
let sessionId;

async function rpc(method, params) {
  const headers = {
    "Content-Type": "application/json",
    Accept: "application/json, text/event-stream",
  };
  if (sessionId) headers["mcp-session-id"] = sessionId;
  const response = await fetch(`${ENDPOINT}/mcp`, {
    method: "POST",
    headers,
    body: JSON.stringify({ jsonrpc: "2.0", id: requestId++, method, params }),
  });
  if (!response.ok) throw new Error(`Graphiti MCP HTTP ${response.status}`);
  const result = await readJsonResponse(response);
  if (result.error) throw new Error(result.error.message);
  return result;
}

async function startMcpSession() {
  const response = await fetch(`${ENDPOINT}/mcp`, {
    method: "POST",
    headers: { "Content-Type": "application/json", Accept: "application/json, text/event-stream" },
    body: JSON.stringify({
      jsonrpc: "2.0",
      id: requestId++,
      method: "initialize",
      params: {
        protocolVersion: "2025-11-25",
        capabilities: {},
        clientInfo: { name: "openclaw-whatsapp-importer", version: "1.0.0" },
      },
    }),
  });
  if (!response.ok) throw new Error(`Graphiti MCP initialization failed: ${response.status}`);
  sessionId = response.headers.get("mcp-session-id") ?? undefined;
  await readJsonResponse(response);
  const headers = {
    "Content-Type": "application/json",
    Accept: "application/json, text/event-stream",
  };
  if (sessionId) headers["mcp-session-id"] = sessionId;
  await fetch(`${ENDPOINT}/mcp`, {
    method: "POST",
    headers,
    body: JSON.stringify({ jsonrpc: "2.0", method: "notifications/initialized" }),
  });
}

async function addEpisode(name, groupId, body, sourceDescription) {
  await rpc("tools/call", {
    name: "add_memory",
    arguments: {
      name,
      episode_body: body,
      source: "message",
      source_description: sourceDescription,
      group_id: groupId,
    },
  });
}

function authorize(groupId) {
  if (NO_AUTHORIZE) return;
  const command = process.platform === "win32" ? "openclaw.cmd" : "openclaw";
  const result = spawnSync(command, ["graphiti-mem", "add-member", groupId, "main", "--type", "agent"], {
    stdio: "inherit",
    shell: process.platform === "win32",
  });
  if (result.status !== 0) throw new Error(`SpiceDB authorization failed for ${groupId}`);
}

async function main() {
  const files = (await readdir(EXPORT_DIR))
    .filter((file) => extname(file).toLowerCase() === ".txt")
    .sort();
  if (files.length === 0) {
    console.log(`No .txt files found in ${EXPORT_DIR}`);
    return;
  }

  console.log(`${DRY_RUN ? "Previewing" : "Importing"} ${files.length} WhatsApp export(s) from ${EXPORT_DIR}`);
  if (!DRY_RUN) await startMcpSession();

  for (const file of files) {
    const text = await readFile(join(EXPORT_DIR, file), "utf8");
    const messages = parseExport(text);
    const groupId = `wa-${slugify(basename(file, extname(file)))}`;
    const fileChunks = chunks(messages);
    console.log(`\n${file}: ${messages.length} messages, ${fileChunks.length} episode(s), group_id=${groupId}`);
    if (DRY_RUN) continue;

    authorize(groupId);
    for (let index = 0; index < fileChunks.length; index++) {
      const body = [
        `WhatsApp conversation export: ${file}`,
        `Conversation group_id: ${groupId}`,
        `Episode chunk: ${index + 1}/${fileChunks.length}`,
        "",
        renderMessages(fileChunks[index]),
      ].join("\n");
      await addEpisode(`whatsapp_${groupId}_${String(index + 1).padStart(4, "0")}`, groupId, body, `WhatsApp export: ${file}`);
      console.log(`  queued episode ${index + 1}/${fileChunks.length}`);
    }
  }
  console.log("\nImport complete. Graphiti processes episodes asynchronously; leave the service running until its queue drains.");
}

main().catch((error) => {
  console.error(`Import failed: ${error instanceof Error ? error.message : String(error)}`);
  process.exitCode = 1;
});
