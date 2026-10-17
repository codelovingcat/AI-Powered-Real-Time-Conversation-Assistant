namespace Conversa.Domain.Conversations;

public enum ConversationLifecycleState
{
    // Keep Ready non-zero. EF Core uses the CLR default (zero) as a sentinel for
    // store-generated properties, and the database default must stay Active so
    // rows created before this migration remain usable.
    Ready = 1,
    Active = 2,
    Paused = 3,
    Completed = 4
}
