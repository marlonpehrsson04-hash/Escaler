public partial class EscalerApp
{
    private readonly ClientReactive<string> _handoffScope = new("for");
    private readonly ClientReactive<string> _handoffStatus = new("open");
    private readonly ClientReactive<string> _handoffSearch = new("");

    private IReadOnlyList<Handoff> VisibleHandoffs(Expert me)
    {
        IEnumerable<Handoff> rows = _handoffs.Values;

        // Problems can be personal (pay, health, a conflict), so only the people involved see one; admins see all
        rows = _handoffScope.Value switch
        {
            "from" => rows.Where(h => IsRequester(h, me)),
            "all" when _isAdmin.Value => rows,
            _ => rows.Where(h => h.AssigneeId == me.Id),
        };

        rows = _handoffStatus.Value switch
        {
            "open" => rows.Where(h => h.IsOpen),
            "done" => rows.Where(h => h.Status == HandoffStatus.Done),
            "unassigned" => rows.Where(h => h.Status == HandoffStatus.Unassigned),
            "cancelled" => rows.Where(h => h.Status == HandoffStatus.Cancelled),
            _ => rows,
        };

        var search = _handoffSearch.Value.Trim();

        if (search.Length > 0)
        {
            rows = rows.Where(h =>
                h.Summary.Contains(search, StringComparison.OrdinalIgnoreCase)
                || h.Problem.Contains(search, StringComparison.OrdinalIgnoreCase)
                || h.RequesterName.Contains(search, StringComparison.OrdinalIgnoreCase)
                || (FindExpert(h.AssigneeId)?.Name.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false));
        }

        // Open urgent work first, then newest
        return rows
            .OrderByDescending(h => h.IsOpen)
            .ThenByDescending(h => h.Urgency switch { "High" => 2, "Normal" => 1, _ => 0 })
            .ThenByDescending(h => h.CreatedAt)
            .ToList();
    }

    private void RenderHandoffs(UIView view, Expert me)
    {
        var all = _handoffs.Values.ToList();
        var waitingOnMe = all.Count(h => h.Status == HandoffStatus.Waiting && h.AssigneeId == me.Id);
        var inProgress = all.Count(h => h.Status == HandoffStatus.Accepted);
        var weekAgo = DateTime.UtcNow.AddDays(-7);
        var resolved = all.Count(h => h.Status == HandoffStatus.Done && h.UpdatedAt >= weekAgo);
        var firstTry = all.Where(h => h.Status is HandoffStatus.Accepted or HandoffStatus.Done).ToList();
        var firstTryRate = firstTry.Count == 0 ? "–" : $"{100 * firstTry.Count(h => h.Index == 0) / firstTry.Count}%";

        view.Column(["gap-1"], content: head =>
        {
            head.Text([Text.H2, "font-heading"], text: "Handoffs");
            head.Text([Text.Body, "text-muted-foreground"], text: _isAdmin.Value ? "Every request Escaler routed, in Slack and here." : "Requests routed to you or by you, in Slack and here.");
        });

        view.Column(["gap-3 grid grid-cols-2 lg:grid-cols-4"], content: stats =>
        {
            Stat(stats, "inbox", "Waiting on you", waitingOnMe.ToString(), waitingOnMe > 0);
            Stat(stats, "loader", "In progress", inProgress.ToString(), false);
            Stat(stats, "circle-check", "Resolved this week", resolved.ToString(), false);
            Stat(stats, "target", "Right person first try", firstTryRate, false);
        });

        view.Row(["flex-wrap items-center gap-2"], content: bar =>
        {
            bar.Row(["p-1 rounded-full bg-foreground/5 gap-1"], content: seg =>
            {
                Segment(seg, "for", "For me");
                Segment(seg, "from", "From me");
                if (_isAdmin.Value)
                {
                    Segment(seg, "all", "Everyone");
                }
            });

            bar.Select(["w-full sm:w-40"], triggerStyle: [Select.Size.Sm], ariaLabel: "Status", bind: _handoffStatus, options:
            [
                new SelectOption("open", "Open"),
                new SelectOption("all", "Any status"),
                new SelectOption("done", "Done"),
                new SelectOption("unassigned", "No match"),
                new SelectOption("cancelled", "Cancelled"),
            ]);

            bar.TextField([Input.DefaultSm, "w-full sm:w-60"], placeholder: "Search requests or people", ariaLabel: "Search handoffs", bind: _handoffSearch);
        });

        var visible = VisibleHandoffs(me);

        if (visible.Count == 0)
        {
            var (title, description) = _handoffScope.Value switch
            {
                "for" => ("Nothing waiting on you", "When Escaler routes a problem to you, it shows up here and in your Slack DMs."),
                "from" => ("You haven't asked for anything", "Describe what's blocking you on the Ask tab, or DM @Escaler in Slack."),
                _ => ("No handoffs here", "Try another status or clear the search."),
            };

            view.EmptyState(title, icon: "inbox", description: description, action: a =>
            {
                if (_handoffStatus.Value != "all" || _handoffSearch.Value.Length > 0)
                {
                    a.Button([Button.OutlineMd], text: "Show all statuses", onClick: async () =>
                    {
                        _handoffStatus.Value = "all";
                        _handoffSearch.Value = "";
                    });
                }
                else
                {
                    a.Button([Button.PrimaryMd], text: "Ask for help", onClick: async () => _tab.Value = "ask");
                }
            });
            return;
        }

        view.Column(["gap-3"], content: list =>
        {
            foreach (var handoff in visible)
            {
                RenderHandoffRow(list, me, handoff);
            }
        });
    }

    private void Segment(UIView view, string value, string label)
    {
        var active = _handoffScope.Value == value;

        view.Button(["h-9 px-4 rounded-full text-sm font-medium transition-colors",
                active ? "bg-card text-foreground shadow-xs" : "bg-transparent text-muted-foreground hover:text-foreground"],
            text: label,
            onClick: async () => _handoffScope.Value = value);
    }

    private static void Stat(UIView view, string icon, string label, string value, bool highlight)
    {
        view.Card([highlight ? Card.Elevated : Card.Subtle, "p-4 gap-3", highlight ? "ring-2 ring-brand" : ""], content: card =>
        {
            card.Box(["w-9 h-9 rounded-lg flex items-center justify-center", highlight ? "bg-brand-solid" : "bg-brand-solid/15"], content: tile =>
                tile.Icon([Icon.Sm, highlight ? "text-primary-foreground" : "text-brand-700"], name: icon));
            card.Column(["gap-0.5"], content: text =>
            {
                text.Text(["font-heading text-3xl font-bold tabular-nums leading-none"], text: value);
                text.Text([Text.Caption, "text-muted-foreground"], text: label);
            });
        });
    }

    private void RenderHandoffRow(UIView view, Expert me, Handoff handoff)
    {
        var assignee = FindExpert(handoff.AssigneeId);
        var isAssignee = handoff.AssigneeId == me.Id;
        var isRequester = IsRequester(handoff, me);

        view.Card([isAssignee && handoff.Status == HandoffStatus.Waiting ? Card.Elevated : Card.Default, "p-5 gap-3",
                isAssignee && handoff.Status == HandoffStatus.Waiting ? "border-l-4 border-l-brand" : ""],
            key: $"handoff-{handoff.Id}",
            content: card =>
            {
                card.Row(["items-start justify-between gap-3 flex-wrap"], content: top =>
                {
                    top.Text(["font-semibold text-base flex-1 min-w-48"], text: handoff.Summary.Length > 0 ? handoff.Summary : Truncate(handoff.Problem, 70));
                    top.Row(["items-center gap-1.5 shrink-0"], content: badges =>
                    {
                        UrgencyBadge(badges, handoff.Urgency);
                        badges.Badge(StatusLabel(handoff.Status), tone: StatusTone(handoff.Status), size: BadgeSize.Sm);
                    });
                });

                card.Row(["items-center gap-2 flex-wrap text-sm"], content: route =>
                {
                    route.Text(["font-medium"], text: isRequester ? "You" : handoff.RequesterName);
                    route.Icon([Icon.Sm, "text-muted-foreground"], name: "arrow-right");
                    route.Text(["font-medium"], text: assignee is null ? "Nobody yet" : isAssignee ? "You" : assignee.Name);

                    if (handoff.CandidateIds.Length > 1 && handoff.Status is HandoffStatus.Waiting or HandoffStatus.Accepted)
                    {
                        route.Text([Text.Caption, "text-muted-foreground"], text: $"· choice {handoff.Index + 1} of {handoff.CandidateIds.Length}");
                    }

                    route.Text([Text.Caption, "text-muted-foreground"], text: $"· via {handoff.Source} · {Ago(handoff.CreatedAt)}");
                });

                card.Text([Text.Caption, "text-muted-foreground leading-relaxed line-clamp-3 whitespace-pre-line"], text: handoff.Problem);

                if (handoff.AssigneeReason is { Length: > 0 } reason && handoff.IsOpen)
                {
                    card.Row(["items-start gap-2 rounded-lg bg-foreground/5 px-3 py-2"], content: why =>
                    {
                        why.Icon([Icon.Sm, "text-brand-700 mt-0.5 shrink-0"], name: "sparkles");
                        why.Text([Text.Caption, "leading-relaxed"], text: reason);
                    });
                }

                var canAct = (isAssignee && handoff.Status == HandoffStatus.Waiting)
                    || (handoff.Status == HandoffStatus.Accepted && (isAssignee || isRequester))
                    || (isRequester && handoff.IsOpen);

                if (!canAct)
                {
                    return;
                }

                card.Row(["flex-wrap gap-2 pt-1"], content: actions =>
                {
                    if (isAssignee && handoff.Status == HandoffStatus.Waiting)
                    {
                        actions.Button([Button.PrimaryMd], text: "I'll take it", onClick: async () => await AcceptAsync(handoff.Id, me.Id));
                        actions.Button([Button.OutlineMd], text: "Not me, pass it on", onClick: async () => await DeclineAsync(handoff.Id, me.Id));
                    }

                    if (handoff.Status == HandoffStatus.Accepted && (isAssignee || isRequester))
                    {
                        actions.Button([Button.PrimaryMd, "gap-1.5"], text: "Mark done", onClick: async () => MarkDone(handoff.Id, me), content: b =>
                        {
                            b.Icon([Icon.Sm], name: "check");
                            b.Text(text: "Mark done");
                        });
                    }

                    // Demo colleagues can't answer for themselves, so the requester plays their part to try the flow
                    if (isRequester && handoff.Status == HandoffStatus.Waiting && assignee is { IsDemo: true } demo)
                    {
                        var first = demo.Name.Split(' ')[0];
                        actions.Button([Button.OutlineMd, "border-dashed"], text: $"Demo: {first} accepts", onClick: async () =>
                        {
                            if (FindExpert(demo.Id) is { IsDemo: true })
                            {
                                await AcceptAsync(handoff.Id, demo.Id);
                            }
                        });
                        actions.Button([Button.OutlineMd, "border-dashed"], text: $"Demo: {first} passes", onClick: async () =>
                        {
                            if (FindExpert(demo.Id) is { IsDemo: true })
                            {
                                await DeclineAsync(handoff.Id, demo.Id);
                            }
                        });
                    }

                    if (isRequester && handoff.IsOpen)
                    {
                        actions.Button([Button.GhostMd], text: "Withdraw request", onClick: async () => Cancel(handoff.Id, me));
                    }
                });
            });
    }
}
