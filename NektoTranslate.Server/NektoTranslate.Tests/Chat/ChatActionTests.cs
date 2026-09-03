using NektoTranslate.Chat.Services;
using Xunit;


namespace NektoTranslate.Tests.Chat;


// The agent is asked for bare JSON and will now and then wrap it in a code fence or put a sentence
// in front of it. Refusing the turn over a formatting habit would make the chat feel broken, so the
// parser is forgiving - and these tests pin exactly how forgiving, including the case where the
// safest reading is "this was just an answer".
public class ChatActionTests {

    [Fact]
    public void APlainReplyIsRead() {
        ChatAction? action = ChatAction.Parse("""{"action":"reply","text":"Готово"}""");

        Assert.Equal("reply", action?.action);
        Assert.Equal("Готово", action?.text);
    }


    [Fact]
    public void AToolCallCarriesItsArguments() {
        ChatAction? action = ChatAction.Parse(
            """{"action":"tool","name":"glossary_record","arguments":{"sourceTerm":"田中"}}"""
        );

        Assert.Equal("tool", action?.action);
        Assert.Equal("glossary_record", action?.name);
        Assert.Equal("田中", action?.arguments.GetProperty("sourceTerm").GetString());
    }


    [Fact]
    public void ACodeFenceIsStripped() {
        ChatAction? action = ChatAction.Parse("```json\n{\"action\":\"reply\",\"text\":\"ок\"}\n```");

        Assert.Equal("ок", action?.text);
    }


    [Fact]
    public void ProseAroundTheObjectIsIgnored() {
        ChatAction? action = ChatAction.Parse("Here you go: {\"action\":\"reply\",\"text\":\"ок\"} hope that helps");

        Assert.Equal("ок", action?.text);
    }


    [Theory]
    [InlineData("Просто ответ без всякого JSON.")]
    [InlineData("")]
    [InlineData("{ это не json }")]
    public void AnythingUnreadableFallsBackToBeingATextReply(string reply) {
        Assert.Null(ChatAction.Parse(reply));
    }
}
