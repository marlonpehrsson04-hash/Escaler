public partial class EscalerApp
{
    private readonly ClientReactive<bool> _checkingSlack = new(false);

    private void RenderSlackSetup(UIView view)
    {
        view.Column(["gap-1"], content: head =>
        {
            head.Text([Text.H2, "font-heading"], text: "Connect Slack");
            head.Text([Text.Body, "text-muted-foreground max-w-2xl"],
                text: "Once connected, anyone can DM @Escaler what they're stuck on. The expert gets the request with Accept and Pass buttons, and an accept opens a group DM for the two of them.");
        });

        view.Card([Card.Elevated, "p-5 gap-4"], content: card =>
        {
            card.Row(["items-center justify-between gap-4 flex-wrap"], content: row =>
            {
                row.Row(["items-center gap-3"], content: status =>
                {
                    var connected = _slackWorkspace.Value is not null;

                    status.Box(["w-11 h-11 rounded-xl flex items-center justify-center", connected ? "bg-success-500/15" : "bg-warning-500/15"], content: tile =>
                        tile.Icon([Icon.Md, connected ? "text-success-700" : "text-warning-700"], name: connected ? "plug-zap" : "unplug"));

                    status.Column(["gap-0.5"], content: text =>
                    {
                        text.Text(["font-semibold"], text: connected ? $"Connected to {_slackWorkspace.Value}" : SlackReady ? "Token set, but Slack didn't accept it" : "Not connected yet");
                        text.Text([Text.Caption, "text-muted-foreground"], text: connected
                            ? $"{_experts.Values.Count(e => e.SlackUserId is not null)} of {_experts.Values.Count(e => !e.IsDemo)} people linked to Slack"
                            : "Escaler works on the web until then.");
                    });
                });

                row.Button([Button.OutlineSm, "gap-1.5"], text: "Check again", disabled: _checkingSlack.Value, onClick: async () =>
                {
                    using var _ = _checkingSlack.AsToken();
                    await CheckSlackAsync();
                }, content: b =>
                {
                    if (_checkingSlack.Value)
                    {
                        b.Spinner(size: SpinnerSize.Sm);
                    }
                    else
                    {
                        b.Icon([Icon.Sm], name: "refresh-cw");
                    }

                    b.Text(text: "Check again");
                });
            });

            if (_slackError.Value is { } error)
            {
                card.Alert("Slack rejected the connection", tone: SemanticTone.Error, description: error);
            }
        });

        var manifest = SlackManifest();

        view.Column(["gap-4"], content: steps =>
        {
            SetupStep(steps, 1, "Create the Slack app from this manifest",
                "In Slack, choose Create New App, then From an app manifest. Pick your workspace and paste this in. The webhook URLs are already filled in.",
                body =>
                {
                    body.Row(["gap-2 flex-wrap"], content: actions =>
                    {
                        actions.ActionButton([Button.PrimaryMd, "gap-2"], action: ActionKind.CopyToClipboard,
                            options: new CopyToClipboardActionOptions { Text = manifest },
                            content: b =>
                            {
                                b.Icon([Icon.Sm], name: "clipboard-copy");
                                b.Text(text: "Copy manifest");
                            });
                        actions.Link([Button.OutlineMd, "gap-2"], text: "Open Slack apps", href: "https://api.slack.com/apps?new_app=1", target: "_blank", rel: "noopener", icon: "external-link");
                    });

                    body.Box(["max-h-64 overflow-auto rounded-lg bg-foreground/5 p-4"], content: code =>
                        code.Text(["font-mono text-xs whitespace-pre leading-relaxed"], text: manifest));
                });

            SetupStep(steps, 2, "Install it and copy two values",
                "Click Install to Workspace. Then copy the Bot User OAuth Token (starts with xoxb-) from OAuth & Permissions, and the Signing Secret from Basic Information.",
                null);

            SetupStep(steps, 3, "Store them as app secrets",
                "Run these in the project folder. Each one asks for the value and hides it. Then redeploy so the app picks them up.",
                body =>
                {
                    const string commands = "ikon secret set SLACK_BOT_TOKEN\nikon secret set SLACK_SIGNING_SECRET";

                    body.Row(["items-start gap-2 rounded-lg bg-foreground/5 p-4"], content: code =>
                    {
                        code.Text(["font-mono text-xs whitespace-pre leading-relaxed flex-1"], text: commands);
                        code.ActionButton([Button.GhostSm], action: ActionKind.CopyToClipboard, options: new CopyToClipboardActionOptions { Text = commands },
                            content: b => b.Icon([Icon.Sm], name: "clipboard-copy", ariaLabel: "Copy commands"));
                    });
                });

            SetupStep(steps, 4, "Verify the events URL, then try it",
                "Back in the Slack app settings, open Event Subscriptions and press Retry next to the request URL if it isn't verified yet. Then DM @Escaler something you're stuck on.",
                body =>
                {
                    EndpointLine(body, "Events", EndpointUrl(nameof(SlackEvents)));
                    EndpointLine(body, "Interactivity", EndpointUrl(nameof(SlackInteractions)));
                });
        });

        view.Alert("People link automatically", tone: SemanticTone.Info,
            description: "Escaler matches each profile's work email to a Slack account. Anyone who DMs the bot with a matching email is linked on their first message.");
    }

    private static void SetupStep(UIView view, int number, string title, string description, Action<UIView>? body)
    {
        view.Card([Card.Default, "p-5 gap-4"], content: card =>
        {
            card.Row(["items-start gap-4"], content: row =>
            {
                row.Box(["w-8 h-8 shrink-0 rounded-full bg-foreground text-background flex items-center justify-center font-heading font-bold text-sm"], content: n =>
                    n.Text(text: number.ToString()));

                row.Column(["gap-3 flex-1 min-w-0"], content: col =>
                {
                    col.Column(["gap-1"], content: text =>
                    {
                        text.Text(["font-semibold"], text: title);
                        text.Text([Text.Caption, "text-muted-foreground leading-relaxed"], text: description);
                    });

                    body?.Invoke(col);
                });
            });
        });
    }

    private static void EndpointLine(UIView view, string label, string? url)
    {
        view.Row(["items-center gap-3 rounded-lg bg-foreground/5 px-3 py-2 min-w-0"], content: row =>
        {
            row.Text([Text.Caption, "font-semibold w-24 shrink-0"], text: label);
            row.Text(["font-mono text-xs truncate flex-1 min-w-0"], text: url ?? "Available once the app is deployed");

            if (url is not null)
            {
                row.ActionButton([Button.GhostSm], action: ActionKind.CopyToClipboard, options: new CopyToClipboardActionOptions { Text = url },
                    content: b => b.Icon([Icon.Sm], name: "clipboard-copy", ariaLabel: $"Copy {label} URL"));
            }
        });
    }
}
