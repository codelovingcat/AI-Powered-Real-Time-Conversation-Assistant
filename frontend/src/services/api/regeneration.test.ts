import { describe, expect, it } from "vitest";
import { getRegenerationInputKind } from "./regeneration";

describe("getRegenerationInputKind", () => {
  it("regenerates speaker results as heard speech", () => {
    expect(getRegenerationInputKind("speaker")).toBe("heardSpeech");
  });

  it("regenerates user formulation results as user requests", () => {
    expect(getRegenerationInputKind("user")).toBe("userFormulationRequest");
  });
});
