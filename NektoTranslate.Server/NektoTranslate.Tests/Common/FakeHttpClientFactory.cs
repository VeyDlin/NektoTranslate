namespace NektoTranslate.Tests.Common;


// A single canned response (or a thrown exception) for whatever request comes in - every test here
// asks one thing of one endpoint, so there is no request to route by URL or method.
public class FakeHttpMessageHandler : HttpMessageHandler {

    private readonly Func<HttpRequestMessage, HttpResponseMessage> respond;


    public FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) {
        this.respond = respond;
    }


    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken
    ) {
        return Task.FromResult(respond(request));
    }
}


// IHttpClientFactory without a real DI container behind it - every service under test here calls
// CreateClient() with no name, so this only ever needs to hand back the one client it was built with.
public class FakeHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory {

    public HttpClient CreateClient(string name) {
        return new HttpClient(handler, disposeHandler: false);
    }
}
