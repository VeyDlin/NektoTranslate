using Microsoft.AspNetCore.SignalR;


namespace NektoTranslate.Translation.Hubs;


// One group per novel. A reader watching a book only receives that book's traffic, and a long
// translation run does not broadcast into every open tab.
public class TranslationHub : Hub {

    public static string GroupFor(long novelId) {
        return $"novel-{novelId}";
    }


    public Task Watch(long novelId) {
        return Groups.AddToGroupAsync(Context.ConnectionId, GroupFor(novelId));
    }


    public Task Unwatch(long novelId) {
        return Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupFor(novelId));
    }
}
