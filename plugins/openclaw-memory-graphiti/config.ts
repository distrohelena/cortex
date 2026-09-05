export type GraphitiMemoryConfig = {
  spicedb: {
    endpoint: string;
    token: string;
    insecure: boolean;
  };
  graphiti: {
    endpoint: string;
    defaultGroupId: string;
    uuidPollIntervalMs: number;
    uuidPollMaxAttempts: number;
  };
  whatsapp: {
    selfActorIds: string[];
  };
  eventBridge: {
    enabled: boolean;
    endpoint: string;
    timeoutMs: number;
  };
  subjectType: "agent" | "person";
  subjectId: string;
  autoCapture: boolean;
  autoRecall: boolean;
  customInstructions: string;
  maxCaptureMessages: number;
};

const DEFAULT_SPICEDB_ENDPOINT = "localhost:50051";
const DEFAULT_SPICEDB_TOKEN = "";
const DEFAULT_GRAPHITI_ENDPOINT = "http://localhost:8000";
const DEFAULT_GROUP_ID = "main";
const DEFAULT_UUID_POLL_INTERVAL_MS = 3000;
const DEFAULT_UUID_POLL_MAX_ATTEMPTS = 30;
const DEFAULT_SUBJECT_TYPE = "agent";
const DEFAULT_MAX_CAPTURE_MESSAGES = 10;
const DEFAULT_EVENT_BRIDGE_ENDPOINT = "http://127.0.0.1:5147/v1/openclaw/events/inbound";
const DEFAULT_EVENT_BRIDGE_TIMEOUT_MS = 1500;

const DEFAULT_CUSTOM_INSTRUCTIONS = `Extract key facts about:
- Identity: names, roles, titles, contact info
- Preferences: likes, dislikes, preferred tools/methods
- Goals: objectives, plans, deadlines
- Relationships: connections between people, teams, organizations
- Decisions: choices made, reasoning, outcomes
- Routines: habits, schedules, recurring patterns
Do not extract: greetings, filler, meta-commentary about the conversation itself.`;

function resolveEnvVars(value: string): string {
  return value.replace(/\$\{([^}]+)\}/g, (_, envVar) => {
    const envValue = process.env[envVar];
    if (!envValue) {
      throw new Error(`Environment variable ${envVar} is not set`);
    }
    return envValue;
  });
}

function assertAllowedKeys(value: Record<string, unknown>, allowed: string[], label: string) {
  const unknown = Object.keys(value).filter((key) => !allowed.includes(key));
  if (unknown.length > 0) {
    throw new Error(`${label} has unknown keys: ${unknown.join(", ")}`);
  }
}

function loopbackHttpEndpoint(value: unknown): string {
  const endpoint = typeof value === "string" ? value : DEFAULT_EVENT_BRIDGE_ENDPOINT;
  let url: URL;
  try {
    url = new URL(endpoint);
  } catch {
    throw new Error("eventBridge.endpoint must be a valid loopback HTTP URL");
  }
  if (url.protocol !== "http:" || !["127.0.0.1", "localhost", "[::1]"].includes(url.hostname)) {
    throw new Error("eventBridge.endpoint must use HTTP on a loopback host");
  }
  if (url.username || url.password) {
    throw new Error("eventBridge.endpoint must not contain embedded credentials");
  }
  return url.toString();
}

export const graphitiMemoryConfigSchema = {
  parse(value: unknown): GraphitiMemoryConfig {
    if (Array.isArray(value)) {
      throw new Error("openclaw-memory-graphiti config must be an object, not an array");
    }
    // Accept undefined/null (installer writes no config key) — treat as empty {}
    const cfg = (value && typeof value === "object" ? value : {}) as Record<string, unknown>;
    assertAllowedKeys(
      cfg,
      [
        "spicedb", "graphiti", "subjectType", "subjectId",
        "autoCapture", "autoRecall", "customInstructions", "maxCaptureMessages", "whatsapp", "eventBridge",
      ],
      "openclaw-memory-graphiti config",
    );

    // SpiceDB config
    const spicedb = (cfg.spicedb as Record<string, unknown>) ?? {};
    assertAllowedKeys(spicedb, ["endpoint", "token", "insecure"], "spicedb config");

    // Graphiti config
    const graphiti = (cfg.graphiti as Record<string, unknown>) ?? {};
    assertAllowedKeys(graphiti, ["endpoint", "defaultGroupId", "uuidPollIntervalMs", "uuidPollMaxAttempts"], "graphiti config");

    const whatsapp = (cfg.whatsapp as Record<string, unknown>) ?? {};
    assertAllowedKeys(whatsapp, ["selfActorIds"], "whatsapp config");
    const selfActorIds = Array.isArray(whatsapp.selfActorIds)
      ? whatsapp.selfActorIds.filter((value): value is string => typeof value === "string" && Boolean(value.trim()))
      : [];

    const eventBridge = (cfg.eventBridge as Record<string, unknown>) ?? {};
    assertAllowedKeys(eventBridge, ["enabled", "endpoint", "timeoutMs"], "eventBridge config");

    // Subject
    const subjectType = cfg.subjectType === "person" ? "person" : DEFAULT_SUBJECT_TYPE;
    const subjectId =
      typeof cfg.subjectId === "string" ? resolveEnvVars(cfg.subjectId) : "default";

    return {
      spicedb: {
        endpoint:
          typeof spicedb.endpoint === "string" ? spicedb.endpoint : DEFAULT_SPICEDB_ENDPOINT,
        token: typeof spicedb.token === "string" ? resolveEnvVars(spicedb.token) : DEFAULT_SPICEDB_TOKEN,
        insecure: spicedb.insecure !== false,
      },
      graphiti: {
        endpoint:
          typeof graphiti.endpoint === "string" ? graphiti.endpoint : DEFAULT_GRAPHITI_ENDPOINT,
        defaultGroupId:
          typeof graphiti.defaultGroupId === "string"
            ? graphiti.defaultGroupId
            : DEFAULT_GROUP_ID,
        uuidPollIntervalMs:
          typeof graphiti.uuidPollIntervalMs === "number" && graphiti.uuidPollIntervalMs > 0
            ? graphiti.uuidPollIntervalMs
            : DEFAULT_UUID_POLL_INTERVAL_MS,
        uuidPollMaxAttempts:
          typeof graphiti.uuidPollMaxAttempts === "number" && graphiti.uuidPollMaxAttempts > 0
            ? Math.round(graphiti.uuidPollMaxAttempts)
            : DEFAULT_UUID_POLL_MAX_ATTEMPTS,
      },
      whatsapp: { selfActorIds },
      eventBridge: {
        enabled: eventBridge.enabled === true,
        endpoint: loopbackHttpEndpoint(eventBridge.endpoint),
        timeoutMs:
          typeof eventBridge.timeoutMs === "number" &&
          Number.isFinite(eventBridge.timeoutMs) &&
          eventBridge.timeoutMs >= 100 &&
          eventBridge.timeoutMs <= 10_000
            ? Math.round(eventBridge.timeoutMs)
            : DEFAULT_EVENT_BRIDGE_TIMEOUT_MS,
      },
      subjectType,
      subjectId,
      autoCapture: cfg.autoCapture !== false,
      autoRecall: cfg.autoRecall !== false,
      customInstructions:
        typeof cfg.customInstructions === "string"
          ? cfg.customInstructions
          : DEFAULT_CUSTOM_INSTRUCTIONS,
      maxCaptureMessages:
        typeof cfg.maxCaptureMessages === "number" && cfg.maxCaptureMessages > 0
          ? cfg.maxCaptureMessages
          : DEFAULT_MAX_CAPTURE_MESSAGES,
    };
  },
};
