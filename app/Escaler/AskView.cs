public partial class EscalerApp
{
    private static readonly string[] AskExamples =
    [
        "A customer was double-charged in March and wants a refund before Friday",
        "How do I get access to the production dashboards?",
        "Our biggest client wants a security questionnaire filled in this week",
    ];

    private readonly UserReactive<string> _askDraft = new("");
    private readonly UserReactive<bool> _routing = new(false);
    private readonly UserReactive<RoutePreview?> _preview = new(null);
    private readonly UserReactive<bool> _askFailed = new(false);
    private readonly UserReactive<string?> _sentHandoffId = new(null);
    private readonly UserReactive<bool> _sending = new(false);

    // "Auto" keeps the AI's urgency guess; anything else is the requester's call
    private readonly UserReactive<string> _askPriority = new("Auto");

    private static readonly string[] RequestPriorities = ["Auto", "Low", "Normal", "High"];

    private string EffectiveUrgency(string aiUrgency) => _askPriority.Value is "Low" or "Normal" or "High" ? _askPriority.Value : aiUrgency;

    private void RenderAsk(UIView view, Expert me)
    {
        view.Column(["gap-3 motion-[0:opacity-0_translate-y-[8px],100:opacity-100_translate-y-0] motion-duration-400ms"], content: hero =>
        {
            hero.Text([Text.Overline, "text-muted-foreground"], text: $"Hi {me.Name.Split(' ')[0]}");
            hero.Text(["font-heading text-4xl md:text-5xl font-bold tracking-tight leading-[1.02]"], text: "What's blocking you?");
            hero.Text([Text.BodyLg, "text-muted-foreground max-w-2xl"],
                text: "Describe it like you'd tell a colleague. Escaler finds who in the company knows it best and has room for it. Same as DMing @Escaler in Slack.");
        });

        RenderPinnedPriorities(view, me);

        if (me.IsDraft)
        {
            view.Alert("Your profile was set up from Slack", tone: SemanticTone.Info,
                description: me.Skills.Length > 0
                    ? $"Escaler guessed your skills from your title ({string.Join(", ", me.Skills)}). Check them so the right requests reach you."
                    : "Add what people can ask you about, so the right requests reach you.",
                content: alert => alert.Button([Button.OutlineSm, "mt-3 self-start"], text: "Check my profile", onClick: async () => OpenProfileEditor(me)));
        }

        view.Card([Card.Elevated, "p-2 gap-0"], content: card =>
        {
            card.TextArea(["w-full bg-transparent border-0 shadow-none focus:ring-0 text-base px-4 py-3 resize-none outline-hidden"],
                placeholder: "e.g. A customer was double-charged in March and wants a refund before Friday. Who can fix the invoice?",
                rows: 3,
                autoResize: true,
                maxRows: 10,
                ariaLabel: "Describe your problem",
                bind: _askDraft);

            card.Row(["items-center gap-2 px-3 pt-1 flex-wrap"], content: prio =>
            {
                prio.Text([Text.Caption, "text-muted-foreground"], text: "Priority");
                prio.Row(["p-0.5 rounded-full bg-foreground/5 gap-0.5"], content: seg =>
                {
                    foreach (var option in RequestPriorities)
                    {
                        seg.Button(["h-8 px-3 rounded-full text-xs font-medium transition-colors",
                                _askPriority.Value == option ? "bg-card text-foreground shadow-xs" : "bg-transparent text-muted-foreground hover:text-foreground"],
                            text: option == "Auto" ? "Auto" : option,
                            key: $"prio-{option}",
                            onClick: async () => _askPriority.Value = option);
                    }
                });

                if (_askPriority.Value == "Auto")
                {
                    prio.Text([Text.Caption, "text-muted-foreground hidden sm:block"], text: "Escaler judges urgency from your words");
                }
            });

            card.Row(["items-center justify-between gap-3 px-2 pb-1 pt-2 flex-wrap"], content: row =>
            {
                var length = _askDraft.Value.Trim().Length;

                row.Text([Text.Caption, length > MaxProblem ? "text-error-700 px-2" : "text-muted-foreground px-2"], text: length == 0
                    ? "Describe the problem to find someone"
                    : length > MaxProblem
                        ? $"Too long: {length:N0} of {MaxProblem:N0} characters. Sum it up a bit."
                        : $"Routing across {_experts.Values.Count(e => e.IsRoutable && e.Id != me.Id)} available people");

                row.Button([Button.PrimaryMd, "gap-2"],
                    text: "Find the right person",
                    disabled: _routing.Value || _askDraft.Value.Trim().Length is 0 or > MaxProblem,
                    onClick: async () => await FindPersonAsync(me),
                    content: b =>
                    {
                        if (_routing.Value)
                        {
                            b.Spinner(size: SpinnerSize.Sm);
                        }
                        else
                        {
                            b.Icon([Icon.Sm], name: "sparkles");
                        }

                        b.Text(text: _routing.Value ? "Matching…" : "Find the right person");
                    });
            });
        });

        if (_askDraft.Value.Length == 0 && _preview.Value is null && !_routing.Value)
        {
            view.Row(["flex-wrap gap-2 -mt-4"], content: chips =>
            {
                foreach (var example in AskExamples)
                {
                    chips.Button([Button.OutlineSm, "rounded-full text-left h-auto py-1.5 whitespace-normal"], text: example, onClick: async () => _askDraft.Value = example);
                }
            });
        }

        if (_sentHandoffId.Value is { } sentId && _handoffs.ContainsKey(sentId))
        {
            var sent = _handoffs[sentId];
            var to = FindExpert(sent.AssigneeId);

            view.Alert($"Sent to {to?.Name ?? "your colleague"}", tone: SemanticTone.Success,
                description: to?.SlackUserId is not null && SlackReady
                    ? "They have it in Slack with Accept and Pass buttons. You'll be put in a group DM as soon as they accept."
                    : "They'll see it under Handoffs. If they pass, it moves to the next best person automatically.",
                onDismiss: async () => _sentHandoffId.Value = null,
                content: alert => alert.Button([Button.OutlineSm, "mt-3 self-start"], text: "Track it in Handoffs", onClick: async () =>
                {
                    _sentHandoffId.Value = null;
                    _handoffScope.Value = "from";
                    _tab.Value = "handoffs";
                }));
        }

        if (_routing.Value)
        {
            RenderMatchSkeleton(view);
        }
        else if (_askFailed.Value)
        {
            view.Alert("Couldn't find a match just now", tone: SemanticTone.Error, description: "Your text is still there. Try again in a moment.", content: alert =>
                alert.Button([Button.OutlineSm, "mt-3 self-start"], text: "Try again", onClick: async () => await FindPersonAsync(me)));
        }
        else if (_preview.Value is { } preview)
        {
            RenderPreview(view, me, preview);
        }
        else if (_sentHandoffId.Value is null)
        {
            RenderHowItWorks(view);
        }
    }

    private async Task FindPersonAsync(Expert me)
    {
        var problem = _askDraft.Value.Trim();

        if (_routing.Value || problem.Length is 0 or > MaxProblem)
        {
            return;
        }

        using var _ = _routing.AsToken();
        _askFailed.Value = false;
        _sentHandoffId.Value = null;
        _preview.Value = null;
        _outside.Value = null;
        _webFailed.Value = false;

        var candidates = _experts.Values.Where(e => e.IsRoutable && e.Id != me.Id).ToList();

        try
        {
            var plan = candidates.Count > 0
                ? await PlanRouteAsync(problem, candidates)
                : new RoutePlan(Truncate(OneLine(problem), 90), "Normal", false, "", []);

            // Outside options are worked out up front for a weak match, so they appear together with the colleagues
            if (NeedsOutsideHelp(plan))
            {
                var summary = plan.Summary.Length > 0 ? plan.Summary : Truncate(OneLine(problem), 90);
                _outside.Value = new OutsideHelp(problem, summary, await MatchPartnersAsync(problem), [], false);
            }

            _preview.Value = new RoutePreview(problem, plan);
        }
        catch (Exception e)
        {
            Log.Instance.Warning($"Web routing failed: {e.GetType().Name}: {e.Message}");
            _askFailed.Value = true;
        }
    }

    private async Task SendAsync(Expert me, RoutePreview preview, string expertId)
    {
        if (_sending.Value)
        {
            return;
        }

        using var _ = _sending.AsToken();

        var plan = preview.Plan with { Urgency = EffectiveUrgency(preview.Plan.Urgency) };
        var handoff = await StartHandoffAsync(preview.Problem, plan, me.Name, me.Id, me.SlackUserId, "Web", expertId);
        _askPriority.Value = "Auto";
        _sentHandoffId.Value = handoff.Id;
        _preview.Value = null;
        _askDraft.Value = "";
    }

    private void RenderPreview(UIView view, Expert me, RoutePreview preview)
    {
        var plan = preview.Plan;

        if (plan.NeedsClarification)
        {
            view.Alert("Tell me a bit more", tone: SemanticTone.Info,
                description: plan.ClarifyingQuestion.Length > 0 ? plan.ClarifyingQuestion : "Add some detail about what you need, then search again.");
            return;
        }

        var outside = _outside.Value is { } help && help.Problem == preview.Problem ? help : null;

        if (plan.Matches.Count == 0)
        {
            view.EmptyState("Nobody inside fits this one", icon: "user-search",
                description: "No colleague lists expertise that covers it. Add detail and search again, or look outside below.");

            if (outside is not null)
            {
                RenderOutsideHelp(view, me, outside, noInternalMatch: true);
            }

            return;
        }

        view.Column(["gap-4 motion-[0:opacity-0_translate-y-[10px],100:opacity-100_translate-y-0] motion-duration-400ms"], content: result =>
        {
            result.Row(["items-center gap-2 flex-wrap"], content: head =>
            {
                head.Text([Text.H4, "font-heading"], text: plan.Summary);
                UrgencyBadge(head, EffectiveUrgency(plan.Urgency));
            });

            var best = plan.Matches[0];

            if (FindExpert(best.ExpertId) is { } top)
            {
                result.Card([Card.Elevated, "p-6 gap-5 border-l-4 border-l-brand"], content: card =>
                {
                    card.Row(["items-start justify-between gap-4 flex-wrap"], content: row =>
                    {
                        row.Row(["items-center gap-3"], content: who =>
                        {
                            Avatar(who, top.Name, size: "w-12 h-12 text-sm");
                            who.Column(["gap-0.5"], content: name =>
                            {
                                name.Row(["items-center gap-2"], content: line =>
                                {
                                    line.Text(["text-lg font-semibold"], text: top.Name);
                                    line.Badge("Best match", tone: SemanticTone.Brand, size: BadgeSize.Sm);
                                });
                                name.Text([Text.Caption, "text-muted-foreground"], text: $"{top.Title} · {top.Department}");
                            });
                        });

                        FitMeter(row, best.Score);
                    });

                    card.Text([Text.Body, "leading-relaxed"], text: best.Reason);
                    SkillChips(card, top.Skills, 6);

                    card.Row(["items-center justify-between gap-3 flex-wrap pt-1"], content: foot =>
                    {
                        Workload(foot, top);

                        foot.Button([Button.PrimaryMd, "gap-2"],
                            text: $"Send to {top.Name.Split(' ')[0]}",
                            disabled: _sending.Value,
                            onClick: async () => await SendAsync(me, preview, top.Id),
                            content: b =>
                            {
                                b.Icon([Icon.Sm], name: "send");
                                b.Text(text: $"Send to {top.Name.Split(' ')[0]}");
                            });
                    });
                });
            }

            if (plan.Matches.Count > 1)
            {
                result.Text([Text.Overline, "text-muted-foreground pt-2"], text: "Backups, if they pass");

                result.Column(["gap-3 md:grid md:grid-cols-2"], content: grid =>
                {
                    foreach (var match in plan.Matches.Skip(1))
                    {
                        if (FindExpert(match.ExpertId) is not { } expert)
                        {
                            continue;
                        }

                        grid.Card([Card.Subtle, "p-5 gap-3"], key: $"match-{expert.Id}", content: card =>
                        {
                            card.Row(["items-center justify-between gap-3"], content: row =>
                            {
                                row.Row(["items-center gap-3 min-w-0"], content: who =>
                                {
                                    Avatar(who, expert.Name);
                                    who.Column(["min-w-0"], content: name =>
                                    {
                                        name.Text(["text-sm font-semibold truncate"], text: expert.Name);
                                        name.Text([Text.Caption, "text-muted-foreground truncate"], text: expert.Title);
                                    });
                                });
                                row.Text([Text.Caption, "font-semibold tabular-nums text-muted-foreground"], text: $"{match.Score}% fit");
                            });

                            card.Text([Text.Caption, "text-muted-foreground leading-relaxed"], text: match.Reason);
                            card.Button([Button.OutlineSm, "self-start"], text: $"Send to {expert.Name.Split(' ')[0]} instead", disabled: _sending.Value,
                                onClick: async () => await SendAsync(me, preview, expert.Id));
                        });
                    }
                });
            }

            if (outside is not null)
            {
                RenderOutsideHelp(result, me, outside, noInternalMatch: false);
            }
        });
    }

    private static void FitMeter(UIView view, int score)
    {
        view.Column(["gap-1.5 w-32"], content: meter =>
        {
            meter.Row(["justify-between items-baseline"], content: label =>
            {
                label.Text([Text.Caption, "text-muted-foreground"], text: "Fit");
                label.Text(["font-heading text-xl font-bold tabular-nums"], text: $"{score}%");
            });
            meter.Progress(["h-1.5"], value: score, max: 100,
                indicatorStyle: [Progress.ComposeIndicator(variant: Progress.Variant.Default, indeterminate: false, "transition-all duration-700 ease-out")]);
        });
    }

    private void Workload(UIView view, Expert expert)
    {
        var open = OpenCount(expert.Id);

        view.Row(["items-center gap-2"], content: load =>
        {
            load.Box(["w-2 h-2 rounded-full", open == 0 ? "bg-success-500" : open < 3 ? "bg-warning-500" : "bg-error-500"]);
            load.Text([Text.Caption, "text-muted-foreground"], text: open == 0 ? "Nothing open right now" : $"{open} open handoff{(open == 1 ? "" : "s")}");
        });
    }

    private static void SkillChips(UIView view, string[] skills, int max)
    {
        view.Row(["flex-wrap gap-1.5"], content: chips =>
        {
            foreach (var skill in skills.Take(max))
            {
                chips.Badge(skill, size: BadgeSize.Sm);
            }

            if (skills.Length > max)
            {
                chips.Badge($"+{skills.Length - max}", size: BadgeSize.Sm, outline: true);
            }
        });
    }

    private static void UrgencyBadge(UIView view, string urgency)
    {
        if (urgency == "High")
        {
            view.Badge("Urgent", tone: SemanticTone.Error, size: BadgeSize.Sm, dot: true);
        }
        else if (urgency == "Low")
        {
            view.Badge("Low priority", size: BadgeSize.Sm, outline: true);
        }
    }

    private static void RenderMatchSkeleton(UIView view)
    {
        view.Card([Card.Elevated, "p-6 gap-5"], content: card =>
        {
            card.Row(["items-center gap-3"], content: row =>
            {
                row.Skeleton(["w-12 h-12 shrink-0"], shape: SkeletonShape.Circle);
                row.Column(["gap-2 flex-1"], content: col =>
                {
                    col.Skeleton(["w-40"], size: SkeletonSize.Sm);
                    col.Skeleton(["w-56"], size: SkeletonSize.Xs);
                });
            });
            card.Skeleton(["w-full"], size: SkeletonSize.Sm);
            card.Skeleton(["w-2/3"], size: SkeletonSize.Sm);
            card.Text([Text.Caption, "text-muted-foreground motion-[0:opacity-40,50:opacity-100,100:opacity-40] motion-duration-1500ms motion-loop"],
                text: "Reading everyone's expertise and workload…");
        });
    }

    private static void RenderHowItWorks(UIView view)
    {
        view.Column(["gap-3 md:grid md:grid-cols-3 md:gap-4 pt-2"], content: grid =>
        {
            HowCard(grid, "1", "message-square-text", "Describe it", "Plain words, in Slack or here. Vague? Escaler asks one follow-up question.");
            HowCard(grid, "2", "scan-search", "Matched by expertise", "Ranked on real skills, then on who has the lightest load right now.");
            HowCard(grid, "3", "handshake", "Accept or pass", "The expert takes it or passes it to the next best person. No dead ends.");
        });
    }

    private static void HowCard(UIView view, string number, string icon, string title, string body)
    {
        view.Card([Card.Subtle, "p-5 gap-3"], content: card =>
        {
            card.Row(["items-center justify-between"], content: row =>
            {
                row.Box(["w-10 h-10 rounded-lg bg-brand-solid/15 flex items-center justify-center"], content: tile =>
                    tile.Icon([Icon.Md, "text-brand-700"], name: icon));
                row.Text(["font-heading text-3xl font-bold text-foreground/15"], text: number);
            });
            card.Text(["font-semibold"], text: title);
            card.Text([Text.Caption, "text-muted-foreground leading-relaxed"], text: body);
        });
    }
}
