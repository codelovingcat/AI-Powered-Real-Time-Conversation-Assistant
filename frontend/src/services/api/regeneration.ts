import type { AssistantInputKind } from "./assistantService";
import type { MessageRole } from "./messageService";

export function getRegenerationInputKind(role: MessageRole): AssistantInputKind {
  return role === "user" ? "userFormulationRequest" : "heardSpeech";
}
