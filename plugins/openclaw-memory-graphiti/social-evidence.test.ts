import { describe, expect, test } from "vitest";

import { establishNickname, extractAddressTerm } from "./social-evidence.js";

describe("nickname evidence", () => {
  test("extracts only an explicit leading vocative from Filomena output", () => {
    expect(extractAddressTerm("Filomena: Professor, você viu isso?")).toBe("Professor");
    expect(extractAddressTerm("Filomena: meu bem, calma")).toBe("meu bem");
    expect(extractAddressTerm("Filomena: Helena disse que Gabriel terminou o jogo.")).toBeUndefined();
    expect(extractAddressTerm("Sem prefixo, Professor")).toBeUndefined();
  });
  test("generic voice terms never become participant nicknames", () => {
    for (const term of ["amor", "amiga", "amigo", "meu bem"]) {
      expect(establishNickname([
        { term, runId: "run-1", episodeUuid: "episode-1" },
        { term, runId: "run-2", episodeUuid: "episode-2" },
      ])).toBeUndefined();
    }
  });

  test("distinctive term in two agent turns becomes established", () => {
    expect(establishNickname([
      { term: "Professor", runId: "run-1", episodeUuid: "episode-1" },
      { term: "professor", runId: "run-2", episodeUuid: "episode-2" },
    ])).toMatchObject({ displayTerm: "Professor", evidenceRunIds: ["run-1", "run-2"] });
  });

  test("two provider chunks from one run count as one evidence", () => {
    expect(establishNickname([
      { term: "Professor", runId: "run-1", episodeUuid: "episode-1" },
      { term: "Professor", runId: "run-1", episodeUuid: "episode-2" },
    ])).toBeUndefined();
  });

  test("explicit request plus one later actual use establishes a distinctive name", () => {
    expect(establishNickname(
      [{ term: "Capitão", runId: "run-2", episodeUuid: "episode-2" }],
      { requestedTerm: "capitao", episodeUuid: "request-episode" },
    )).toMatchObject({ displayTerm: "Capitão" });
  });
});
