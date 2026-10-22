using System.Text.Json;
using System.Text.Json.Nodes;
using SaveHarbor.App.Services;

namespace SaveHarbor.App.Infrastructure.Cloud.Supabase;

// One Phoenix message of Supabase Realtime, protocol 1.0.0: a JSON object per text frame.
public sealed record RealtimeMessage(string Topic, string Event, JsonElement Payload, string? Ref, string? JoinRef);

// Builds and reads Realtime frames. Every frame comes from the network, so reading never throws on unexpected content
// and every presence value is length-checked before the window shows it.
public static class SupabaseRealtimeProtocol
{
    public const string Version = "1.0.0";

    // realtime.topic() in the database policies sees the topic without the "realtime:" prefix.
    public static string Topic(string groupId) => $"realtime:saveharbor:{groupId}";

    public static Uri SocketUri(SupabaseOptions options)
    {
        var project = new Uri(options.Url);
        var builder = new UriBuilder(project)
        {
            Scheme = project.Scheme == Uri.UriSchemeHttps ? "wss" : "ws",
            Path = "realtime/v1/websocket",
            Query = $"apikey={Uri.EscapeDataString(options.PublishableKey)}&vsn={Version}"
        };
        return builder.Uri;
    }

    // A private channel: the database lets only members of the group join it and track presence. The worlds rows carry
    // every change a friend can see (new world, new version, lock taken or released), filtered to this group. Deletes
    // are left out on purpose: Realtime cannot filter them by group.
    public static string Join(string groupId, string userId, string accessToken, string joinRef)
    {
        var filter = $"group_id=eq.{groupId}";
        return Frame(Topic(groupId), "phx_join", new JsonObject
        {
            ["config"] = new JsonObject
            {
                ["broadcast"] = new JsonObject { ["ack"] = false, ["self"] = false },
                ["presence"] = new JsonObject { ["enabled"] = true, ["key"] = userId },
                ["postgres_changes"] = new JsonArray(
                    Change("INSERT", "worlds", filter),
                    Change("UPDATE", "worlds", filter)),
                ["private"] = true
            },
            ["access_token"] = accessToken
        }, joinRef, joinRef);
    }

    public static string Heartbeat(string reference) => Frame("phoenix", "heartbeat", new JsonObject(), reference, null);

    public static string AccessToken(string groupId, string accessToken, string reference, string joinRef) =>
        Frame(Topic(groupId), "access_token", new JsonObject { ["access_token"] = accessToken }, reference, joinRef);

    public static string Track(string groupId, CloudPresence presence, string reference, string joinRef) =>
        Frame(Topic(groupId), "presence", new JsonObject
        {
            ["type"] = "presence",
            ["event"] = "track",
            ["payload"] = new JsonObject
            {
                ["display_name"] = presence.DisplayName,
                ["game"] = presence.Game,
                ["playing_world_id"] = presence.PlayingWorldId
            }
        }, reference, joinRef);

    // Stops showing this app as online; the channel stays joined.
    public static string Untrack(string groupId, string reference, string joinRef) =>
        Frame(Topic(groupId), "presence", new JsonObject { ["type"] = "presence", ["event"] = "untrack" }, reference, joinRef);

    public static RealtimeMessage? Parse(string text)
    {
        try
        {
            using var document = JsonDocument.Parse(text);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || Text(root, "topic") is not { } topic
                || Text(root, "event") is not { } eventName)
            {
                return null;
            }

            var payload = root.TryGetProperty("payload", out var value) ? value.Clone() : default;
            return new RealtimeMessage(topic, eventName, payload, Text(root, "ref"), Text(root, "join_ref"));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // "ok" or "error" of a phx_reply.
    public static string? ReplyStatus(RealtimeMessage message) =>
        message.Payload.ValueKind == JsonValueKind.Object ? Text(message.Payload, "status") : null;

    // The table and INSERT/UPDATE/DELETE of a postgres_changes event; null when the event is not one.
    public static CloudLiveChange? Change(RealtimeMessage message)
    {
        if (message.Payload.ValueKind != JsonValueKind.Object
            || !message.Payload.TryGetProperty("data", out var data)
            || data.ValueKind != JsonValueKind.Object
            || Text(data, "table") is not { Length: > 0 and <= 63 } table
            || Text(data, "type") is not { Length: > 0 and <= 16 } type)
        {
            return null;
        }

        return new CloudLiveChange(table, type);
    }

    // Applies presence_state (replaces everything) or presence_diff (joins, then leaves) to the metas per member key.
    public static void ApplyPresence(Dictionary<string, List<JsonElement>> metasByKey, RealtimeMessage message)
    {
        if (message.Payload.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        if (message.Event == "presence_state")
        {
            metasByKey.Clear();
            AddMetas(metasByKey, message.Payload);
            return;
        }

        if (message.Payload.TryGetProperty("joins", out var joins))
        {
            AddMetas(metasByKey, joins);
        }

        if (message.Payload.TryGetProperty("leaves", out var leaves) && leaves.ValueKind == JsonValueKind.Object)
        {
            foreach (var member in leaves.EnumerateObject())
            {
                if (!metasByKey.TryGetValue(member.Name, out var metas))
                {
                    continue;
                }

                var leaving = PhxRefs(member.Value);
                metas.RemoveAll(meta => Text(meta, "phx_ref") is { } phxRef && leaving.Contains(phxRef));
                if (metas.Count == 0)
                {
                    metasByKey.Remove(member.Name);
                }
            }
        }
    }

    // One entry per member, from the newest meta (a member with SaveHarbor open on two PCs has two). Keys that are not
    // user ids are dropped, and every text is cut to the length the database allows for it.
    public static IReadOnlyList<CloudPresenceEntry> Entries(Dictionary<string, List<JsonElement>> metasByKey) =>
        metasByKey
            .Where(pair => Guid.TryParse(pair.Key, out _) && pair.Value.Count > 0)
            .Select(pair =>
            {
                var meta = pair.Value[^1];
                return new CloudPresenceEntry(
                    pair.Key,
                    Cut(Text(meta, "display_name"), 32),
                    Cut(Text(meta, "game"), 32),
                    Text(meta, "playing_world_id") is { Length: > 0 } world ? Cut(world, 128) : null);
            })
            .ToArray();

    private static JsonObject Change(string eventName, string table, string filter) => new()
    {
        ["event"] = eventName,
        ["schema"] = "public",
        ["table"] = table,
        ["filter"] = filter
    };

    private static string Frame(string topic, string eventName, JsonObject payload, string? reference, string? joinRef) =>
        new JsonObject
        {
            ["topic"] = topic,
            ["event"] = eventName,
            ["payload"] = payload,
            ["ref"] = reference,
            ["join_ref"] = joinRef
        }.ToJsonString();

    private static void AddMetas(Dictionary<string, List<JsonElement>> metasByKey, JsonElement members)
    {
        if (members.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        foreach (var member in members.EnumerateObject())
        {
            if (!member.Value.TryGetProperty("metas", out var metas) || metas.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            if (!metasByKey.TryGetValue(member.Name, out var list))
            {
                list = [];
                metasByKey[member.Name] = list;
            }

            list.AddRange(metas.EnumerateArray().Where(meta => meta.ValueKind == JsonValueKind.Object).Select(meta => meta.Clone()));
        }
    }

    private static HashSet<string> PhxRefs(JsonElement member) =>
        member.TryGetProperty("metas", out var metas) && metas.ValueKind == JsonValueKind.Array
            ? metas.EnumerateArray().Select(meta => Text(meta, "phx_ref")).OfType<string>().ToHashSet(StringComparer.Ordinal)
            : [];

    private static string? Text(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value)
            ? value.ValueKind switch
            {
                JsonValueKind.String => value.GetString(),
                JsonValueKind.Number => value.GetRawText(),
                _ => null
            }
            : null;

    private static string Cut(string? value, int maxLength)
    {
        var trimmed = (value ?? string.Empty).Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }
}
