import assert from "node:assert/strict";
import { describe, it } from "node:test";

import { parseCliArgs, resolveTargetGroupId } from "./import-whatsapp.mjs";

const REAL_JID = "5511981042361-1589823314@g.us";
const CANONICAL_GROUP = "whatsapp-group-5511981042361-1589823314-g-us";

describe("WhatsApp importer target identity", () => {
  it("uses the shared canonical namespace for an explicit JID", () => {
    const options = parseCliArgs(["exports", "--jid", REAL_JID, "--dry-run"]);
    assert.equal(resolveTargetGroupId(options), CANONICAL_GROUP);
    assert.equal(options.exportDir, "exports");
    assert.equal(options.dryRun, true);
  });

  it("accepts an explicit group_id unchanged", () => {
    const options = parseCliArgs(["exports", "--group-id", "whatsapp-group-test-g-us"]);
    assert.equal(resolveTargetGroupId(options), "whatsapp-group-test-g-us");
  });

  it("rejects invocation without JID or group_id", () => {
    const options = parseCliArgs(["chat.txt", "--dry-run"]);
    assert.throws(() => resolveTargetGroupId(options), /--jid.*--group-id/i);
  });

  it("never derives persistent identity from a filename", () => {
    const first = parseCliArgs(["chat.txt", "--jid", REAL_JID]);
    const second = parseCliArgs(["renamed-export.txt", "--jid", REAL_JID]);
    assert.equal(resolveTargetGroupId(first), resolveTargetGroupId(second));
  });
});
