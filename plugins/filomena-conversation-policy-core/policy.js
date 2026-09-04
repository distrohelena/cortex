export const RESPONSE_MODES = Object.freeze([
  "disabled",
  "allow",
  "mention",
  "reply-to-filomena",
  "mention-or-reply",
]);

export function isResponseMode(value) {
  return typeof value === "string" && RESPONSE_MODES.includes(value);
}

/** Provider-neutral response decision over adapter-normalized structural signals. */
export function shouldRespond(mode, signals = {}) {
  switch (mode) {
    case "allow": return true;
    case "mention": return signals.mentioned === true;
    case "reply-to-filomena": return signals.replyToFilomena === true;
    case "mention-or-reply": return signals.mentioned === true || signals.replyToFilomena === true;
    case "disabled":
    default: return false;
  }
}
