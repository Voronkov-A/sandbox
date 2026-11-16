using System.Net;
using System.Text;
using System.Text.Json;
using Picshare.Services;

namespace Picshare.Tests;

public sealed class OpenAiDuplicateDetectorTests
{
    private static readonly AiPhoto[] Photos = [new("photo-a", [1, 2]), new("photo-b", [3, 4]), new("photo-c", [5, 6])];

    [Fact]
    public async Task Request_UsesLuna_InlineImages_CompactGroups_AndNoReasoning()
    {
        using var handler = new Handler(async (request, token) =>
        {
            Assert.Equal("https://api.openai.com/v1/responses", request.RequestUri!.AbsoluteUri);
            Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
            Assert.Equal("test-key", request.Headers.Authorization.Parameter);
            using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
            var root = json.RootElement;
            Assert.Equal("gpt-6-luna", root.GetProperty("model").GetString());
            Assert.False(root.GetProperty("store").GetBoolean());
            Assert.Equal("none", root.GetProperty("reasoning").GetProperty("effort").GetString());
            var content = root.GetProperty("input")[0].GetProperty("content").EnumerateArray().ToList();
            var instructions = content[0].GetProperty("text").GetString()!;
            Assert.Contains("rotating the camera by 90, 180 or 270 degrees", instructions);
            Assert.Contains("portrait/landscape aspect ratios", instructions);
            Assert.Contains("Compare scene content regardless of orientation or aspect ratio", instructions);
            Assert.Contains("Do not group unrelated photographs just because their subject is similar", instructions);
            Assert.Contains(content, part => part.GetProperty("type").GetString() == "input_text" && part.GetProperty("text").GetString()!.Contains("different poses"));
            var images = content.Where(part => part.GetProperty("type").GetString() == "input_image").ToList();
            Assert.Equal(3, images.Count);
            Assert.Equal("data:image/jpeg;base64,AQI=", images[0].GetProperty("image_url").GetString());
            var format = root.GetProperty("text").GetProperty("format");
            Assert.True(format.GetProperty("strict").GetBoolean());
            Assert.Equal(new[] { "groups" }, format.GetProperty("schema").GetProperty("properties").EnumerateObject().Select(property => property.Name));
            return Response("{\"groups\":[[0,2]]}");
        });
        using var client = new HttpClient(handler);
        var detector = new OpenAiDuplicateDetector(client, "test-key");
        var result = await detector.FindDuplicatesAsync(Photos, "Keep different poses separate", default);
        Assert.Equal(new[] { "photo-a", "photo-c" }, Assert.Single(result));
    }

    [Theory]
    [InlineData("{\"groups\":[[0,4]]}")]
    [InlineData("{\"groups\":[[0,-1]]}")]
    [InlineData("{\"groups\":[[0,0]]}")]
    [InlineData("{\"groups\":[[0,1],[1,2]]}")]
    [InlineData("{\"groups\":[[0]]}")]
    [InlineData("{\"groups\":null}")]
    [InlineData("{\"groups\":[[0,\"private-prompt\"]]}")]
    [InlineData("private-prompt")]
    public async Task MalformedOrOverlappingGroups_AreRejectedWithoutEchoingInput(string output)
    {
        using var handler = new Handler((request, token) => Task.FromResult(Response(output)));
        using var client = new HttpClient(handler);
        var exception = await Assert.ThrowsAsync<InvalidDataException>(() => new OpenAiDuplicateDetector(client, "test-key").FindDuplicatesAsync(Photos, "", default));
        Assert.DoesNotContain("private-prompt", exception.ToString());
    }

    [Theory]
    [InlineData("incomplete", "output_text")]
    [InlineData("failed", "output_text")]
    [InlineData("completed", "refusal")]
    public async Task IncompleteAndRefusedResponses_AreRejected(string status, string type)
    {
        using var handler = new Handler((request, token) => Task.FromResult(Response("{\"groups\":[]}", status, type)));
        using var client = new HttpClient(handler);
        await Assert.ThrowsAsync<InvalidDataException>(() => new OpenAiDuplicateDetector(client, "test-key").FindDuplicatesAsync(Photos, "", default));
    }

    [Fact]
    public async Task HttpErrors_DoNotExposeProviderResponseBodies()
    {
        using var handler = new Handler((request, token) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized)
        { Content = new StringContent("private-prompt and test-key") }));
        using var client = new HttpClient(handler);
        var exception = await Assert.ThrowsAsync<HttpRequestException>(() => new OpenAiDuplicateDetector(client, "test-key").FindDuplicatesAsync(Photos, "", default));
        Assert.Contains("401", exception.Message);
        Assert.DoesNotContain("private-prompt", exception.ToString());
        Assert.DoesNotContain("test-key", exception.ToString());
    }

    [Theory]
    [InlineData("")]
    public async Task PlaceholderKeys_NeverMakeAnApiRequest(string key)
    {
        using var handler = new Handler((request, token) => throw new InvalidOperationException("Must not send"));
        using var client = new HttpClient(handler);
        await Assert.ThrowsAsync<InvalidOperationException>(() => new OpenAiDuplicateDetector(client, key).FindDuplicatesAsync(Photos, "", default));
        Assert.Equal(0, handler.Count);
    }

    [Fact]
    public async Task ConfiguredModelAndCancellation_AreForwarded()
    {
        using var cancellation = new CancellationTokenSource();
        using var handler = new Handler(async (request, token) =>
        {
            using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
            Assert.Equal("another-vision-model", json.RootElement.GetProperty("model").GetString());
            cancellation.Cancel();
            token.ThrowIfCancellationRequested();
            return Response("{\"groups\":[]}");
        });
        using var client = new HttpClient(handler);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new OpenAiDuplicateDetector(client, "test-key", "another-vision-model").FindDuplicatesAsync(Photos, "", cancellation.Token));
    }

    private static HttpResponseMessage Response(string output, string status = "completed", string type = "output_text") => new(HttpStatusCode.OK)
    {
        Content = new StringContent(JsonSerializer.Serialize(new
        {
            status,
            output = new[] { new { type = "message", content = new[] { new { type, text = output } } } }
        }), Encoding.UTF8, "application/json")
    };

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        public int Count { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Count++;
            return send(request, cancellationToken);
        }
    }
}
