import type { ConversationMessage, MessageRole } from "../api/messageService";

export interface ConversationTurn {
  role: MessageRole;
  messages: ConversationMessage[];
  startedAt: string;
  endedAt: string;
}

export function groupMessagesIntoTurns(
  messages: ConversationMessage[]
): ConversationTurn[] {
  return messages.reduce<ConversationTurn[]>((turns, message) => {
    const previous = turns.at(-1);

    if (previous?.role === message.role && canGroupRole(message.role)) {
      previous.messages.push(message);
      previous.endedAt = message.createdAt;
      return turns;
    }

    turns.push({
      role: message.role,
      messages: [message],
      startedAt: message.createdAt,
      endedAt: message.createdAt
    });

    return turns;
  }, []);
}

function canGroupRole(role: MessageRole): boolean {
  return role === "speaker" || role === "user";
}
