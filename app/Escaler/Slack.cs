using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

// The Slack side: Events API and interactivity webhooks in, Web API calls out.
// Needs the SLACK_BOT_TOKEN and SLACK_SIGNING_SECRET secrets; without them the app runs web-only.
public partial class EscalerApp
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

    // Slack retries an event it thinks timed out; the event id makes each one run once
    private readonly ConcurrentDictionary<string, byte> _seenEvents = new();

    // A vague first message waits here so the answer to the clarifying question routes with it
    private readonly ConcurrentDictionary<string, PendingClarification> _pendingClarify = new();

    private readonly Reactive<string?> _slackWorkspace = new(null);
    private readonly Reactive<string?> _slackError = new(null);

    private bool HasSigningSecret => app.Secrets.TryGet("SLACK_SIGNING_SECRET", out var secret) && !string.IsNullOrWhiteSpace(secret);

    private string? SlackToken => app.Secrets.TryGet("SLACK_BOT_TOKEN", out var token) && !string.IsNullOrWhiteSpace(token) ? token : null;

    private bool SlackReady => SlackToken is not null;

    private string? EndpointUrl(string method) =>
        app.Endpoints.FirstOrDefault(e => e.FunctionName.EndsWith("_" + method, StringComparison.Ordinal))?.PublicUrl;

    private string? AppUrl => EndpointUrl(nameof(SlackEvents)) is { } url && url.IndexOf("/api/", StringComparison.Ordinal) is var at and > 0 ? url[..at] : null;

    [HttpPost("/slack/events", Auth = EndpointAuth.Public)]
    public HttpResult SlackEvents(Ikon.App.HttpRequest req)
    {
        JsonElement root;

        try
        {
            root = JsonDocument.Parse(req.Body).RootElement.Clone();
        }
        catch (JsonException)
        {
            return HttpResult.BadRequest();
        }

        // Echoing the challenge reveals nothing, and answering it before the secrets are set lets setup run in any order
        // Once a signing secret exists, even the handshake has to be signed
        if (Str(root, "type") == "url_verification" && (!HasSigningSecret || VerifySlack(req)))
        {
            return HttpResult.Json(new { challenge = Str(root, "challenge") });
        }

        if (!VerifySlack(req))
        {
            return HttpResult.Unauthorized();
        }

        // Slack only retries for minutes, so an occasional reset keeps this from growing without bound
        if (_seenEvents.Count > 10_000)
        {
            _seenEvents.Clear();
        }

        if (Str(root, "type") != "event_callback" || Str(root, "event_id") is not { } eventId || !_seenEvents.TryAdd(eventId, 0))
        {
            return HttpResult.Ok();
        }

        if (!root.TryGetProperty("event", out var ev))
        {
            return HttpResult.Ok();
        }

        var type = Str(ev, "type");
        var user = Str(ev, "user");
        var channel = Str(ev, "channel");
        var text = Str(ev, "text") ?? "";
        var isFromBot = ev.TryGetProperty("bot_id", out _) || ev.TryGetProperty("subtype", out _);

        if (user is null || channel is null || isFromBot)
        {
            return HttpResult.Ok();
        }

        // Slack wants an answer within 3 seconds, so the routing runs after the response
        if (type == "message" && Str(ev, "channel_type") == "im")
        {
            _ = Task.Run(() => HandleSlackAskAsync(user, channel, text, threadTs: null));
        }
        else if (type == "app_mention")
        {
            _ = Task.Run(() => HandleSlackAskAsync(user, channel, text, threadTs: Str(ev, "thread_ts") ?? Str(ev, "ts")));
        }

        return HttpResult.Ok();
    }

    [HttpPost("/slack/interactions", Auth = EndpointAuth.Public)]
    public HttpResult SlackInteractions(Ikon.App.HttpRequest req)
    {
        if (!VerifySlack(req))
        {
            return HttpResult.Unauthorized();
        }

        var field = req.Body.Split('&').Select(p => p.Split('=', 2)).FirstOrDefault(p => p.Length == 2 && p[0] == "payload");

        if (field is null)
        {
            return HttpResult.BadRequest();
        }

        JsonElement payload;

        try
        {
            payload = JsonDocument.Parse(WebUtility.UrlDecode(field[1])).RootElement.Clone();
        }
        catch (JsonException)
        {
            return HttpResult.BadRequest();
        }

        if (Str(payload, "type") != "block_actions"
            || !payload.TryGetProperty("actions", out var actions) || actions.ValueKind != JsonValueKind.Array || actions.GetArrayLength() == 0
            || !payload.TryGetProperty("user", out var user) || Str(user, "id") is not { } slackUserId)
        {
            return HttpResult.Ok();
        }

        var action = actions[0];
        var actionId = Str(action, "action_id") ?? "";
        var handoffId = Str(action, "value") ?? "";
        // Only ever call back to Slack's own hooks host, whatever the payload says
        var responseUrl = Str(payload, "response_url") is { } url
            && Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps && uri.Host == "hooks.slack.com"
            ? url
            : null;

        _ = Task.Run(() => HandleSlackActionAsync(actionId, handoffId, slackUserId, responseUrl));
        return HttpResult.Ok();
    }

    private async Task HandleSlackAskAsync(string slackUserId, string channel, string text, string? threadTs)
    {
        try
        {
            // Mentions arrive as <@U123>; the model only needs the words
            text = Regex.Replace(text, @"<@[A-Z0-9]+>", "").Trim();

            if (text.Length > MaxProblem)
            {
                await SlackPostAsync(channel, $"That's a lot to route. Can you sum it up in under {MaxProblem:N0} characters?", threadTs: threadTs);
                return;
            }

            if (text.Length == 0)
            {
                await SlackPostAsync(channel, "Hi! Tell me what you're stuck on and I'll find the colleague who knows it best.", threadTs: threadTs);
                return;
            }

            var requester = FindBySlack(slackUserId) ?? await LinkSlackUserByEmailAsync(slackUserId);
            var requesterName = requester?.Name ?? await SlackUserNameAsync(slackUserId) ?? "A colleague";

            if (_pendingClarify.TryRemove(slackUserId, out var pending) && pending.At > DateTime.UtcNow.AddMinutes(-15))
            {
                text = Truncate($"{pending.Text}\n{text}", MaxProblem);
            }

            var candidates = _experts.Values
                .Where(e => e.IsRoutable && e.SlackUserId is not null && e.SlackUserId != slackUserId)
                .ToList();

            if (candidates.Count == 0)
            {
                var where = AppUrl is { } url ? $" at {url}" : "";
                await SlackPostAsync(channel, $"Nobody has added their expertise yet, so I have no one to send this to. Ask your team to sign in to Escaler{where} and fill in their profile.", threadTs: threadTs);
                return;
            }

            await SlackPostAsync(channel, ":mag: Finding the best person for this…", threadTs: threadTs);

            RoutePlan plan;

            try
            {
                plan = await PlanRouteAsync(text, candidates);
            }
            catch (Exception e)
            {
                Log.Instance.Warning($"Routing failed for Slack user {slackUserId}: {e.GetType().Name}: {e.Message}");
                await SlackPostAsync(channel, "I couldn't work that out just now. Send it again in a moment.", threadTs: threadTs);
                return;
            }

            if (plan.NeedsClarification)
            {
                foreach (var stale in _pendingClarify.Where(p => p.Value.At < DateTime.UtcNow.AddMinutes(-15)).Select(p => p.Key).ToList())
                {
                    _pendingClarify.TryRemove(stale, out _);
                }

                _pendingClarify[slackUserId] = new PendingClarification(text, DateTime.UtcNow);
                await SlackPostAsync(channel, plan.ClarifyingQuestion.Length > 0 ? Esc(plan.ClarifyingQuestion) : "Can you tell me a bit more about what you need?", threadTs: threadTs);
                return;
            }

            var handoff = await StartHandoffAsync(text, plan, requesterName, requester?.Id, slackUserId, "Slack");

            if (FindExpert(handoff.AssigneeId) is { } expert)
            {
                var backups = handoff.CandidateIds.Length - 1;
                var backupText = backups > 0 ? $" {backups} backup{(backups == 1 ? "" : "s")} lined up if they can't." : "";
                await SlackPostAsync(channel,
                    $":handshake: Asked *{Esc(expert.Name)}*, {Esc(expert.Title)}: _{Esc(handoff.AssigneeReason ?? "")}_\nI'll put you two in a group DM as soon as they accept.{backupText}",
                    threadTs: threadTs);
            }
            else if (await MatchPartnersAsync(text) is { Count: > 0 } partners)
            {
                // No colleague fits: name the partners the company already uses, and leave contacting them to the person
                var lines = partners
                    .Select(m => FindExpert(m.ExpertId))
                    .OfType<Expert>()
                    .Select(p => $"• *{Esc(p.Company ?? p.Name)}*" + (p.Email is { Length: > 0 } mail ? $" ({Esc(mail)})" : "") + $": _{Esc(partners.First(m => m.ExpertId == p.Id).Reason)}_");
                await SlackPostAsync(channel,
                    $"Nobody inside lists expertise for this one. These external partners your company works with might:\n{string.Join("\n", lines)}\nEscaler doesn't contact them for you.",
                    threadTs: threadTs);
            }
            else
            {
                await SlackPostAsync(channel, "I couldn't find anyone whose expertise fits this one. Add a bit more detail, or ask in a team channel.", threadTs: threadTs);
            }
        }
        catch (Exception e)
        {
            Log.Instance.Warning($"Slack ask failed: {e.GetType().Name}: {e.Message}");
        }
    }

    private async Task HandleSlackActionAsync(string actionId, string handoffId, string slackUserId, string? responseUrl)
    {
        try
        {
            var expert = FindBySlack(slackUserId);
            var summary = _handoffs.ContainsKey(handoffId) ? _handoffs[handoffId].Summary : "this request";

            string? reply = null;

            if (actionId == "accept" && expert is not null && await AcceptAsync(handoffId, expert.Id))
            {
                reply = $":white_check_mark: You took *{Esc(summary)}*. I've opened a group DM with {Esc(_handoffs[handoffId].RequesterName)}.";
            }
            else if (actionId == "decline" && expert is not null && await DeclineAsync(handoffId, expert.Id))
            {
                reply = $":arrow_right_hook: Passed on *{Esc(summary)}*. Thanks for saying so quickly.";
            }
            else if (actionId == "done" && IsParty(handoffId, slackUserId) && MarkDone(handoffId))
            {
                reply = $":tada: *{Esc(summary)}* is done. Nice work.";
            }

            if (responseUrl is null)
            {
                return;
            }

            if (reply is not null)
            {
                await SlackRespondAsync(responseUrl, new { replace_original = true, text = reply });
            }
            else
            {
                var status = _handoffs.ContainsKey(handoffId) ? StatusLabel(_handoffs[handoffId].Status).ToLowerInvariant() : "gone";
                await SlackRespondAsync(responseUrl, new { replace_original = false, response_type = "ephemeral", text = $"This one has already moved on (it's {status})." });
            }
        }
        catch (Exception e)
        {
            Log.Instance.Warning($"Slack action {actionId} failed: {e.GetType().Name}: {e.Message}");
        }
    }

    private bool IsParty(string handoffId, string slackUserId)
    {
        if (!_handoffs.ContainsKey(handoffId))
        {
            return false;
        }

        var handoff = _handoffs[handoffId];
        return handoff.RequesterSlackId == slackUserId || FindExpert(handoff.AssigneeId)?.SlackUserId == slackUserId;
    }

    // DMs the current assignee with the problem and the two buttons that move it on
    private async Task SlackAskAssigneeAsync(Handoff handoff)
    {
        if (!SlackReady || FindExpert(handoff.AssigneeId)?.SlackUserId is not { } slackId)
        {
            return;
        }

        var urgency = handoff.Urgency == "High" ? " :rotating_light: *urgent*" : "";
        var backups = handoff.CandidateIds.Length - handoff.Index - 1;
        var quote = string.Join("\n", Truncate(handoff.Problem, 700).Split('\n').Select(line => "> " + Esc(line)));

        object[] blocks =
        [
            new { type = "section", text = new { type = "mrkdwn", text = $"*{Esc(handoff.RequesterName)}* needs a hand{urgency}\n*{Esc(handoff.Summary)}*" } },
            new { type = "section", text = new { type = "mrkdwn", text = quote } },
            new { type = "context", elements = new object[] { new { type = "mrkdwn", text = $"Why you: {Esc(handoff.AssigneeReason ?? "")}" + (backups > 0 ? $" · {backups} more lined up if you pass" : "") } } },
            new
            {
                type = "actions",
                elements = new object[]
                {
                    new { type = "button", action_id = "accept", value = handoff.Id, style = "primary", text = new { type = "plain_text", text = "I'll take it" } },
                    new { type = "button", action_id = "decline", value = handoff.Id, text = new { type = "plain_text", text = "Not me, pass it on" } },
                },
            },
        ];

        await SlackPostAsync(slackId, $"{Esc(handoff.RequesterName)} needs a hand: {Esc(handoff.Summary)}", blocks);
    }

    // After an accept: one group DM with both people, the problem and a Done button
    private async Task SlackConnectAsync(Handoff handoff)
    {
        if (!SlackReady || FindExpert(handoff.AssigneeId) is not { } expert)
        {
            return;
        }

        var requester = FindExpert(handoff.RequesterExpertId);

        if (expert.SlackUserId is { } expertSlack && handoff.RequesterSlackId is { } requesterSlack)
        {
            var opened = await SlackCallAsync("conversations.open", new() { ["users"] = $"{expertSlack},{requesterSlack}" });

            if (opened?.TryGetProperty("channel", out var channel) == true && Str(channel, "id") is { } channelId)
            {
                object[] blocks =
                [
                    new { type = "section", text = new { type = "mrkdwn", text = $"<@{requesterSlack}>, meet <@{expertSlack}> ({Esc(expert.Title)}). They've got *{Esc(handoff.Summary)}*." } },
                    new { type = "section", text = new { type = "mrkdwn", text = string.Join("\n", Truncate(handoff.Problem, 700).Split('\n').Select(line => "> " + Esc(line))) } },
                    new
                    {
                        type = "actions",
                        elements = new object[] { new { type = "button", action_id = "done", value = handoff.Id, text = new { type = "plain_text", text = "Mark done" } } },
                    },
                ];

                await SlackPostAsync(channelId, $"Connected you with {Esc(expert.Name)} about {Esc(handoff.Summary)}", blocks);
                return;
            }
        }

        // One side isn't on Slack: tell whoever is how to reach the other
        if (handoff.RequesterSlackId is not null)
        {
            var contact = expert.Email is { Length: > 0 } email ? $" Reach them at {Esc(email)}." : "";
            await SlackTellRequesterAsync(handoff, $":white_check_mark: *{Esc(expert.Name)}* accepted *{Esc(handoff.Summary)}*.{contact}");
        }

        if (expert.SlackUserId is { } slackId)
        {
            var contact = requester?.Email is { Length: > 0 } email ? $" at {Esc(email)}" : "";
            await SlackPostAsync(slackId, $"Thanks for taking *{Esc(handoff.Summary)}*. {Esc(handoff.RequesterName)} isn't on Slack. Reach them{contact}.");
        }
    }

    private async Task SlackTellRequesterAsync(Handoff handoff, string text)
    {
        if (SlackReady && handoff.RequesterSlackId is { } slackId)
        {
            await SlackPostAsync(slackId, text);
        }
    }

    // Finds the person's Slack account from their sign-in email; null with a reason when it can't
    private async Task<(string? SlackUserId, string? Problem)> LookupSlackByEmailAsync(string? email)
    {
        if (!SlackReady)
        {
            return (null, null);
        }

        if (string.IsNullOrWhiteSpace(email))
        {
            return (null, "Add your work email to link Slack.");
        }

        var found = await SlackCallAsync("users.lookupByEmail", new() { ["email"] = email.Trim() });

        return found?.TryGetProperty("user", out var user) == true && Str(user, "id") is { } id
            ? (id, null)
            : (null, $"No Slack account uses {email.Trim()}. Use the email you sign in to Slack with.");
    }

    // Someone DMs the bot before linking: match their Slack email to a profile and link it for them
    private async Task<Expert?> LinkSlackUserByEmailAsync(string slackUserId)
    {
        var info = await SlackCallAsync("users.info", new() { ["user"] = slackUserId });

        if (info?.TryGetProperty("user", out var user) != true
            || !user.TryGetProperty("profile", out var profile)
            || Str(profile, "email") is not { } email)
        {
            return null;
        }

        var match = _experts.Values.FirstOrDefault(e => !e.IsExternal && e.SlackUserId is null && string.Equals(e.Email, email, StringComparison.OrdinalIgnoreCase));

        if (match is not null)
        {
            match = match with { SlackUserId = slackUserId };
            _experts[match.Id] = match;
        }

        return match;
    }

    private async Task<string?> SlackUserNameAsync(string slackUserId)
    {
        var info = await SlackCallAsync("users.info", new() { ["user"] = slackUserId });

        if (info?.TryGetProperty("user", out var user) != true)
        {
            return null;
        }

        return user.TryGetProperty("profile", out var profile) && Str(profile, "real_name") is { Length: > 0 } real ? real : Str(user, "name");
    }

    private async Task CheckSlackAsync()
    {
        if (!SlackReady)
        {
            _slackWorkspace.Value = null;
            _slackError.Value = null;
            return;
        }

        var auth = await SlackCallAsync("auth.test", new());

        if (auth is { } ok)
        {
            _slackWorkspace.Value = Str(ok, "team") ?? "your workspace";
            _slackError.Value = null;
        }
        else
        {
            _slackWorkspace.Value = null;
            _slackError.Value = "Slack rejected the bot token. Check SLACK_BOT_TOKEN and that the app is installed to your workspace.";
        }
    }

    private async Task SlackPostAsync(string channel, string text, object[]? blocks = null, string? threadTs = null)
    {
        var args = new Dictionary<string, string> { ["channel"] = channel, ["text"] = text };

        if (blocks is not null)
        {
            args["blocks"] = JsonSerializer.Serialize(blocks);
        }

        if (threadTs is not null)
        {
            args["thread_ts"] = threadTs;
        }

        await SlackCallAsync("chat.postMessage", args);
    }

    // Form-encoded, because Slack's read methods (users.info, users.lookupByEmail) don't take JSON bodies
    private async Task<JsonElement?> SlackCallAsync(string method, Dictionary<string, string> args)
    {
        if (SlackToken is not { } token)
        {
            return null;
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"https://slack.com/api/{method}") { Content = new FormUrlEncodedContent(args) };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            using var response = await Http.SendAsync(request);
            var root = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();

            if (root.TryGetProperty("ok", out var ok) && ok.ValueKind == JsonValueKind.True)
            {
                return root;
            }

            Log.Instance.Warning($"Slack {method} failed: {Str(root, "error") ?? response.StatusCode.ToString()}");
            return null;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException)
        {
            Log.Instance.Warning($"Slack {method} failed: {e.GetType().Name}: {e.Message}");
            return null;
        }
    }

    private static async Task SlackRespondAsync(string responseUrl, object body)
    {
        try
        {
            using var content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
            using var _ = await Http.PostAsync(responseUrl, content);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            Log.Instance.Warning($"Slack response_url failed: {e.Message}");
        }
    }

    // Slack signs every request: HMAC-SHA256 of "v0:{timestamp}:{body}" with the signing secret
    private bool VerifySlack(Ikon.App.HttpRequest req)
    {
        if (!app.Secrets.TryGet("SLACK_SIGNING_SECRET", out var secret) || string.IsNullOrWhiteSpace(secret))
        {
            return false;
        }

        if (Header(req, "X-Slack-Request-Timestamp") is not { } timestamp
            || Header(req, "X-Slack-Signature") is not { } signature
            || !long.TryParse(timestamp, out var seconds)
            || Math.Abs(DateTimeOffset.UtcNow.ToUnixTimeSeconds() - seconds) > 300)
        {
            return false;
        }

        var hash = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes($"v0:{timestamp}:{req.Body}"));
        var expected = "v0=" + Convert.ToHexStringLower(hash);

        return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(signature));
    }

    private static string? Header(Ikon.App.HttpRequest req, string name) =>
        req.Headers.FirstOrDefault(h => string.Equals(h.Key, name, StringComparison.OrdinalIgnoreCase)).Value;

    private static string? Str(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    // Slack mrkdwn treats &, < and > as control characters
    private static string Esc(string text) => text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

    private static string Truncate(string text, int max) => text.Length <= max ? text : text[..max] + "…";

    private string SlackManifest()
    {
        var manifest = new
        {
            display_information = new { name = "Escaler", description = "Tell it what is blocking you. It finds the colleague who knows.", background_color = "#171512" },
            features = new
            {
                app_home = new { home_tab_enabled = false, messages_tab_enabled = true, messages_tab_read_only_enabled = false },
                bot_user = new { display_name = "Escaler", always_online = true },
            },
            oauth_config = new
            {
                scopes = new { bot = new[] { "chat:write", "im:history", "im:read", "im:write", "mpim:write", "users:read", "users:read.email", "app_mentions:read" } },
            },
            settings = new
            {
                event_subscriptions = new { request_url = EndpointUrl(nameof(SlackEvents)) ?? "https://<your-app>.ikonai.app/api/slack/events", bot_events = new[] { "message.im", "app_mention" } },
                interactivity = new { is_enabled = true, request_url = EndpointUrl(nameof(SlackInteractions)) ?? "https://<your-app>.ikonai.app/api/slack/interactions" },
                org_deploy_enabled = false,
                socket_mode_enabled = false,
                token_rotation_enabled = false,
            },
        };

        return JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });
    }
}
