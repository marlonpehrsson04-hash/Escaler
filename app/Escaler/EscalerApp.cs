return await App.Run(args);

// One shared instance for the whole company: the directory, the handoffs and the Slack webhooks all meet here
public record SessionIdentity();

// Per-client data, mapped from URL query params (`?name=Alice` -> `Name`).
public record ClientParameters(string Name = "");

[App]
public partial class EscalerApp(IApp<SessionIdentity, ClientParameters> app)
{
    // Calm operator tool on warm paper and ink, with one signal-orange line for "this is moving"
    private UI UI { get; } = new(app, new IkonTheme
    {
        ["primary"] = "#ff5b1f",
        ["primary-foreground"] = "#140d08",
        ["background"] = "#f4f1ea",
        ["foreground"] = "#171512",
        ["card"] = "#ffffff",
        ["muted-foreground"] = "#5f5a52",
        ["border"] = "#e2ddd2",
        ["font-heading"] = "Space Grotesk",
        ["radius"] = "rounded-xl",
        ["density"] = "comfortable",

        DarkMode = new IkonTheme
        {
            ["primary"] = "#ff6a33",
            ["primary-foreground"] = "#140d08",
            ["background"] = "#12110f",
            ["foreground"] = "#f4f1ea",
            ["card"] = "#1c1b18",
            ["muted-foreground"] = "#a7a196",
            ["border"] = "#2e2c27",
        },
    });

    private ClientProfiles Profiles { get; } = new(app);

    private readonly PersistentReactiveDictionary<string, Expert> _experts = new(key: "escaler-experts");
    private readonly PersistentReactiveDictionary<string, Handoff> _handoffs = new(key: "escaler-handoffs");

    private readonly ClientReactive<string> _tab = new("ask");

    public async Task Main()
    {
        app.OnStarting(async () =>
        {
            // An empty directory routes nowhere; example colleagues show how it works until real people join
            if (_experts.Count == 0)
            {
                foreach (var demo in DemoExperts)
                {
                    _experts[demo.Id] = demo;
                }
            }
        });

        app.OnClientJoined(async ctx =>
        {
            if (string.IsNullOrEmpty(ctx.UserId))
            {
                return;
            }

            // Prefill the profile form from the sign-in profile, so onboarding starts half done
            var profile = await Profiles.GetProfileAsync(ctx);

            using (ReactiveScope.Use(new UserScope(ctx.UserId)))
            {
                if (!_formLoaded.Value)
                {
                    FillForm(FindExpert(ctx.UserId), profile);
                }
            }
        });

        _ = Task.Run(CheckSlackAsync);

        UI.Root([Page.Default], content: view =>
        {
            view.Column(["h-screen w-full bg-background text-foreground"], content: page =>
            {
                if (Me() is not { } me)
                {
                    RenderOnboarding(page);
                    return;
                }

                RenderTopBar(page, me);

                page.ScrollArea(["flex-1 min-h-0"], content: scroll =>
                {
                    scroll.Column(["w-full max-w-5xl mx-auto px-4 md:px-8 py-8 md:py-10 gap-8"], content: body =>
                    {
                        switch (_tab.Value)
                        {
                            case "people":
                                RenderPeople(body, me);
                                break;
                            case "handoffs":
                                RenderHandoffs(body, me);
                                break;
                            case "slack":
                                RenderSlackSetup(body);
                                break;
                            case "profile":
                                RenderProfilePage(body);
                                break;
                            default:
                                RenderAsk(body, me);
                                break;
                        }
                    });
                });
            });
        });
    }

    private Expert? Me() => ReactiveScope.UserIdOrNull is { Length: > 0 } id ? FindExpert(id) : null;

    private void RenderTopBar(UIView view, Expert me)
    {
        var waiting = _handoffs.Values.Count(h => h.Status == HandoffStatus.Waiting && h.AssigneeId == me.Id);

        view.Row(["shrink-0 h-16 px-3 md:px-8 gap-2 items-center justify-between border-b border-border bg-card/70 backdrop-blur-md"], content: bar =>
        {
            bar.Button(["flex items-center gap-2.5 shrink-0 rounded-lg px-1 py-1 bg-transparent hover:opacity-80 transition-opacity"],
                text: "Escaler home",
                onClick: async () => _tab.Value = "ask",
                content: logo =>
                {
                    logo.Box(["w-9 h-9 rounded-lg bg-brand-solid flex items-center justify-center shadow-xs"], content: mark =>
                        mark.Icon(["text-primary-foreground"], name: "arrow-up-right", size: IconSize.Md));
                    logo.Text(["hidden sm:block font-heading text-xl font-bold tracking-tight"], text: "Escaler");
                });

            bar.Row(["gap-1 items-center overflow-x-auto"], content: nav =>
            {
                NavItem(nav, "ask", "Ask", "message-circle-question");
                NavItem(nav, "people", "People", "users");
                NavItem(nav, "handoffs", "Handoffs", "arrow-right-left", waiting);
                NavItem(nav, "slack", "Slack", "slack");
            });

            bar.Button(["shrink-0 flex items-center gap-2 rounded-full pl-1 pr-1 sm:pr-3 h-10 bg-transparent hover:bg-foreground/5 transition-colors", _tab.Value == "profile" ? "bg-foreground/5" : ""],
                text: "My profile",
                onClick: async () => OpenProfileEditor(me),
                content: chip =>
                {
                    Avatar(chip, me.Name, mine: true);
                    chip.Text(["hidden md:block text-sm font-medium max-w-32 truncate"], text: me.Name);
                });
        });
    }

    private void NavItem(UIView view, string tab, string label, string icon, int count = 0)
    {
        var active = _tab.Value == tab;

        view.Button(
            ["relative flex items-center gap-2 h-10 px-3 rounded-full text-sm font-medium transition-colors shrink-0",
                active ? "bg-foreground text-background" : "bg-transparent text-muted-foreground hover:text-foreground hover:bg-foreground/5"],
            text: label,
            onClick: async () => _tab.Value = tab,
            content: item =>
            {
                item.Icon([Icon.Sm], name: icon);
                item.Text(["hidden sm:inline"], text: label);

                if (count > 0)
                {
                    item.Box(["min-w-5 h-5 px-1.5 rounded-full bg-brand-solid text-primary-foreground text-[11px] font-bold flex items-center justify-center"], content: badge =>
                        badge.Text(text: count.ToString()));
                }
            });
    }

    private static void Avatar(UIView view, string name, bool mine = false, string size = "w-8 h-8 text-xs")
    {
        var initials = string.Concat(name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(part => char.ToUpperInvariant(part[0])));

        view.Box([size, "shrink-0 rounded-full flex items-center justify-center font-semibold",
            mine ? "bg-brand-solid text-primary-foreground" : "bg-foreground text-background"], content: avatar =>
            avatar.Text(text: initials.Length > 0 ? initials : "?"));
    }

    private static string StatusLabel(HandoffStatus status) => status switch
    {
        HandoffStatus.Waiting => "Waiting",
        HandoffStatus.Accepted => "In progress",
        HandoffStatus.Done => "Done",
        HandoffStatus.Unassigned => "No match",
        _ => "Cancelled",
    };

    private static SemanticTone StatusTone(HandoffStatus status) => status switch
    {
        HandoffStatus.Waiting => SemanticTone.Warning,
        HandoffStatus.Accepted => SemanticTone.Info,
        HandoffStatus.Done => SemanticTone.Success,
        HandoffStatus.Unassigned => SemanticTone.Error,
        _ => SemanticTone.Neutral,
    };

    private static string Ago(DateTime utc)
    {
        var span = DateTime.UtcNow - utc;

        return span.TotalMinutes switch
        {
            < 1 => "just now",
            < 60 => $"{(int)span.TotalMinutes}m ago",
            < 1440 => $"{(int)span.TotalHours}h ago",
            _ => $"{(int)span.TotalDays}d ago",
        };
    }
}
