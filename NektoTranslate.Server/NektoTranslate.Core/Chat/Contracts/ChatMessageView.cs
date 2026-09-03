using NektoTranslate.Chat.Entities;
using NektoTranslate.Chat.Enums;


namespace NektoTranslate.Chat.Contracts;


// What the interface receives for one turn.
//
// A view rather than the entity because the entity carries a navigation property back to the novel,
// and the novel carries its glossary, and every glossary entry carries the novel again. Returning
// the entity serialises that loop until it gives up. Projecting is also the honest answer to "what
// does the client actually need" - the novel is already known from the URL.
public sealed record ChatMessageView(
    long id,
    ChatRole role,
    string text,
    double costUsd,
    DateTimeOffset createdAt
) {

    public static ChatMessageView From(ChatMessage message) {
        return new ChatMessageView(
            message.id,
            message.role,
            message.text,
            message.costUsd,
            message.createdAt
        );
    }
}
