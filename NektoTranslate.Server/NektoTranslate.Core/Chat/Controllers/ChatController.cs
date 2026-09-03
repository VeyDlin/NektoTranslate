using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NektoTranslate.Chat.Contracts;
using NektoTranslate.Chat.Entities;
using NektoTranslate.Chat.Services;
using NektoTranslate.Common.Data;


namespace NektoTranslate.Chat.Controllers;


[ApiController]
[Route("api/novels/{novelId:long}/chat")]
public class ChatController(NektoDbContext database, IChatAgentService agent) : ControllerBase {

    [HttpGet]
    public async Task<IReadOnlyList<ChatMessageView>> History(long novelId, CancellationToken cancellationToken) {
        return await database.chatMessages
            .AsNoTracking()
            .Where(message => message.novelId == novelId)
            .OrderBy(message => message.id)
            .Select(message => new ChatMessageView(
                message.id,
                message.role,
                message.text,
                message.costUsd,
                message.createdAt
            ))
            .ToListAsync(cancellationToken);
    }


    // Returns every message the turn produced, tool notes included, so the interface can show what
    // the agent did rather than only what it said.
    [HttpPost]
    public async Task<IReadOnlyList<ChatMessageView>> Send(
        long novelId,
        [FromBody] SendChatMessageRequest request,
        CancellationToken cancellationToken
    ) {
        IReadOnlyList<ChatMessage> written = await agent.SendAsync(novelId, request.text, cancellationToken);

        return written.Select(ChatMessageView.From).ToList();
    }
}
