using System.Text.Json;
using SaveHarbor.App.Infrastructure.Cloud.Supabase;
using SaveHarbor.App.Services;

namespace SaveHarbor.Tests.Cloud.Supabase;

// Realtime frames as SaveHarbor sends them, and how frames from the network are read.
public sealed class SupabaseRealtimeProtocolTests
{
    private const string GroupId = "00000000-0000-4000-8000-000000012345";
    private const string UserId = "00000000-0000-4000-8000-0000000000aa";

    [Fact]
    public void SocketUri_UsesSecureWebSocketsForTheHostedProject()
    {
        var uri = SupabaseRealtimeProtocol.SocketUri(new SupabaseOptions { Url = "https://test-ref.supabase.co/", PublishableKey = "TEST_KEY+/=" });

        Assert.Equal("wss", uri.Scheme);
        Assert.Equal("/realtime/v1/websocket", uri.AbsolutePath);
        Assert.Equal("?apikey=TEST_KEY%2B%2F%3D&vsn=1.0.0", uri.Query);
    }

    [Fact]
    public void SocketUri_UsesPlainWebSocketsOnlyForTheLocalStack()
    {
        var uri = SupabaseRealtimeProtocol.SocketUri(new SupabaseOptions { Url = "http://127.0.0.1:54321/", PublishableKey = "TEST_KEY" });

        Assert.Equal("ws", uri.Scheme);
        Assert.Equal(54321, uri.Port);
    }

    [Fact]
    public void Join_AsksForThePrivateGroupChannelWithFilteredWorldChanges()
    {
        using var frame = JsonDocument.Parse(SupabaseRealtimeProtocol.Join(GroupId, UserId, "TEST_TOKEN", "1"));
        var root = frame.RootElement;
        var config = root.GetProperty("payload").GetProperty("config");

        Assert.Equal($"realtime:saveharbor:{GroupId}", root.GetProperty("topic").GetString());
        Assert.Equal("phx_join", root.GetProperty("event").GetString());
        Assert.Equal("1", root.GetProperty("ref").GetString());
        Assert.Equal("1", root.GetProperty("join_ref").GetString());
        Assert.Equal("TEST_TOKEN", root.GetProperty("payload").GetProperty("access_token").GetString());
        Assert.True(config.GetProperty("private").GetBoolean());
        Assert.Equal(UserId, config.GetProperty("presence").GetProperty("key").GetString());

        var changes = config.GetProperty("postgres_changes").EnumerateArray().ToArray();
        Assert.Equal(["INSERT", "UPDATE"], changes.Select(change => change.GetProperty("event").GetString()));
        Assert.All(changes, change =>
        {
            Assert.Equal("worlds", change.GetProperty("table").GetString());
            Assert.Equal($"group_id=eq.{GroupId}", change.GetProperty("filter").GetString());
        });
    }

    [Fact]
    public void Heartbeat_GoesToThePhoenixTopic()
    {
        using var frame = JsonDocument.Parse(SupabaseRealtimeProtocol.Heartbeat("7"));

        Assert.Equal("phoenix", frame.RootElement.GetProperty("topic").GetString());
        Assert.Equal("heartbeat", frame.RootElement.GetProperty("event").GetString());
        Assert.Equal("7", frame.RootElement.GetProperty("ref").GetString());
    }

    [Fact]
    public void AccessToken_PassesTheRefreshedTokenOnTheChannel()
    {
        using var frame = JsonDocument.Parse(SupabaseRealtimeProtocol.AccessToken(GroupId, "TEST_TOKEN_2", "8", "1"));

        Assert.Equal("access_token", frame.RootElement.GetProperty("event").GetString());
        Assert.Equal("TEST_TOKEN_2", frame.RootElement.GetProperty("payload").GetProperty("access_token").GetString());
        Assert.Equal("1", frame.RootElement.GetProperty("join_ref").GetString());
    }

    [Fact]
    public void Track_SendsOnlyNameGameAndWorld()
    {
        using var frame = JsonDocument.Parse(SupabaseRealtimeProtocol.Track(GroupId, new CloudPresence("TEST_USER", "windrose", "TEST_WORLD_ID"), "5", "1"));
        var payload = frame.RootElement.GetProperty("payload");

        Assert.Equal("track", payload.GetProperty("event").GetString());
        Assert.Equal(
            ["display_name", "game", "playing_world_id"],
            payload.GetProperty("payload").EnumerateObject().Select(property => property.Name));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("{\"topic\":\"phoenix\"}")]
    public void Parse_IgnoresFramesThatAreNotMessages(string text)
    {
        Assert.Null(SupabaseRealtimeProtocol.Parse(text));
    }

    [Fact]
    public void Change_ReadsTableAndTypeOnly()
    {
        var message = SupabaseRealtimeProtocol.Parse(
            "{\"topic\":\"t\",\"event\":\"postgres_changes\",\"payload\":{\"ids\":[1],\"data\":{\"table\":\"worlds\",\"type\":\"UPDATE\",\"record\":{\"world_name\":\"Test World\"}}},\"ref\":null}")!;

        Assert.Equal(new CloudLiveChange("worlds", "UPDATE"), SupabaseRealtimeProtocol.Change(message));
    }

    [Fact]
    public void Presence_FollowsStateJoinsAndLeaves()
    {
        var metas = new Dictionary<string, List<JsonElement>>();
        SupabaseRealtimeProtocol.ApplyPresence(metas, Message("presence_state",
            $"{{\"{UserId}\":{{\"metas\":[{{\"phx_ref\":\"A\",\"display_name\":\"TEST_USER\",\"game\":\"windrose\"}}]}}}}"));
        SupabaseRealtimeProtocol.ApplyPresence(metas, Message("presence_diff",
            "{\"joins\":{\"00000000-0000-4000-8000-0000000000bb\":{\"metas\":[{\"phx_ref\":\"B\",\"display_name\":\"TEST_FRIEND\",\"game\":\"dragonwilds\",\"playing_world_id\":\"TEST_WORLD_ID\"}]}},\"leaves\":{}}"));

        var entries = SupabaseRealtimeProtocol.Entries(metas);
        Assert.Equal(2, entries.Count);
        Assert.Contains(entries, entry => entry.DisplayName == "TEST_FRIEND" && entry.PlayingWorldId == "TEST_WORLD_ID");

        SupabaseRealtimeProtocol.ApplyPresence(metas, Message("presence_diff",
            $"{{\"joins\":{{}},\"leaves\":{{\"{UserId}\":{{\"metas\":[{{\"phx_ref\":\"A\"}}]}}}}}}"));

        Assert.Equal(["TEST_FRIEND"], SupabaseRealtimeProtocol.Entries(metas).Select(entry => entry.DisplayName));
    }

    [Fact]
    public void Presence_DropsKeysThatAreNotUserIdsAndCutsLongNames()
    {
        var metas = new Dictionary<string, List<JsonElement>>();
        SupabaseRealtimeProtocol.ApplyPresence(metas, Message("presence_state",
            $"{{\"not-a-user\":{{\"metas\":[{{\"phx_ref\":\"A\",\"display_name\":\"TEST_USER\"}}]}},\"{UserId}\":{{\"metas\":[{{\"phx_ref\":\"B\",\"display_name\":\"{new string('x', 500)}\"}}]}}}}"));

        var entry = Assert.Single(SupabaseRealtimeProtocol.Entries(metas));
        Assert.Equal(UserId, entry.UserId);
        Assert.Equal(32, entry.DisplayName.Length);
        Assert.Null(entry.PlayingWorldId);
    }

    private static RealtimeMessage Message(string eventName, string payload) =>
        SupabaseRealtimeProtocol.Parse($"{{\"topic\":\"t\",\"event\":\"{eventName}\",\"payload\":{payload}}}")!;
}
