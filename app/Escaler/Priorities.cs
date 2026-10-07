// Priorities managers post for the teams they concern. Only that audience, the poster and admins see one.
public partial class EscalerApp
{
    private static readonly string[] PriorityLevels = ["Normal", "High", "Critical"];

    private const int MaxPriorityTitle = 100;
    private const int MaxPriorityDetails = 600;

    private readonly PersistentReactiveDictionary<string, PriorityTask> _priorities = new(key: "escaler-priority-tasks");

    private readonly ClientReactive<bool> _priorityFormOpen = new(false);
    private readonly ClientReactive<string> _priorityTitle = new("");
    private readonly ClientReactive<string> _priorityDetails = new("");
    private readonly ClientReactive<string> _priorityLevel = new("High");
    private readonly ClientReactive<string> _priorityDue = new("week");
    private readonly ClientReactiveList<string> _priorityTeams = new();
    private readonly ClientReactive<bool> _priorityNotifySlack = new(false);
    private readonly ClientReactive<string> _priorityFilter = new("open");
    private readonly ClientReactive<bool> _posting = new(false);
    private readonly ClientReactive<string?> _priorityNotice = new(null);
    private readonly Reactive<PriorityTask?> _deletedPriority = new(null);

    private bool CanManagePriorities(Expert me) => me.IsManager || _isAdmin.Value;

    // Whether a person belongs to a priority's audience; the poster and admins always see it
    private bool IsRelevant(PriorityTask task, Expert me) =>
        task.Teams.Length == 0 || task.Teams.Contains(me.Department) || task.CreatedById == me.Id || _isAdmin.Value;

    private bool IsAudience(PriorityTask task, Expert person) =>
        !person.IsExternal && !person.IsDemo && (task.Teams.Length == 0 || task.Teams.Contains(person.Department));

    private IReadOnlyList<PriorityTask> RelevantPriorities(Expert me, bool done) =>
        _priorities.Values
            .Where(t => t.Done == done && IsRelevant(t, me))
            .OrderByDescending(t => LevelRank(t.Level))
            .ThenBy(t => t.DueAt ?? DateTime.MaxValue)
            .ThenByDescending(t => t.CreatedAt)
            .ToList();

    private int UnseenPriorities(Expert me) =>
        _priorities.Values.Count(t => !t.Done && IsRelevant(t, me) && t.CreatedById != me.Id && !t.SeenBy.Contains(me.Id));

    private static int LevelRank(string level) => level switch
    {
        "Critical" => 2,
        "High" => 1,
        _ => 0,
    };

    private static SemanticTone LevelTone(string level) => level switch
    {
        "Critical" => SemanticTone.Error,
        "High" => SemanticTone.Warning,
        _ => SemanticTone.Neutral,
    };

    private static DateTime? DueFrom(string choice) => choice switch
    {
        "today" => DateTime.UtcNow.Date.AddDays(1).AddSeconds(-1),
        "week" => DateTime.UtcNow.Date.AddDays(7),
        "month" => DateTime.UtcNow.Date.AddDays(30),
        _ => null,
    };

    private static string DueText(DateTime? due)
    {
        if (due is not { } at)
        {
            return "No deadline";
        }

        var days = (at.Date - DateTime.UtcNow.Date).TotalDays;

        return days switch
        {
            < 0 => $"Overdue since {at:d MMM}",
            < 1 => "Due today",
            < 2 => "Due tomorrow",
            _ => $"Due {at:d MMM}",
        };
    }

    private IReadOnlyList<string> Teams() =>
        _experts.Values.Where(e => !e.IsExternal && e.Department.Length > 0).Select(e => e.Department).Distinct().Order().ToList();

    private async Task PostPriorityAsync(Expert me)
    {
        var title = _priorityTitle.Value.Trim();

        // The form hides itself from non-managers, but the handler is the boundary
        if (!CanManagePriorities(me) || _posting.Value || title.Length is 0 or > MaxPriorityTitle || _priorityDetails.Value.Trim().Length > MaxPriorityDetails)
        {
            return;
        }

        using var _ = _posting.AsToken();

        var known = Teams();
        var task = new PriorityTask(
            Id: Guid.NewGuid().ToString("N")[..10],
            Title: title,
            Details: _priorityDetails.Value.Trim(),
            Level: PriorityLevels.Contains(_priorityLevel.Value) ? _priorityLevel.Value : "High",
            Teams: [.. _priorityTeams.Where(known.Contains).Distinct()],
            CreatedById: me.Id,
            CreatedByName: me.Name,
            CreatedAt: DateTime.UtcNow,
            DueAt: DueFrom(_priorityDue.Value),
            Done: false,
            SeenBy: []);

        _priorities[task.Id] = task;
        var audience = _experts.Values.Count(e => IsAudience(task, e) && e.Id != me.Id);
        _priorityNotice.Value = $"Posted to {audience} {(audience == 1 ? "person" : "people")}.";

        if (_priorityNotifySlack.Value && SlackReady)
        {
            var sent = await NotifyPriorityOnSlackAsync(task, me);
            _priorityNotice.Value = $"Posted to {audience} {(audience == 1 ? "person" : "people")} and sent {sent} Slack DM{(sent == 1 ? "" : "s")}.";
        }

        ResetPriorityForm();
    }

    private async Task<int> NotifyPriorityOnSlackAsync(PriorityTask task, Expert me)
    {
        var people = _experts.Values.Where(e => IsAudience(task, e) && e.Id != me.Id && e.SlackUserId is not null).ToList();
        var marker = task.Level == "Critical" ? ":rotating_light:" : task.Level == "High" ? ":warning:" : ":pushpin:";
        var link = AppUrl is { } url ? $"\n<{url}|Open priorities in Escaler>" : "";
        var details = task.Details.Length > 0 ? "\n" + string.Join("\n", Truncate(task.Details, 600).Split('\n').Select(line => "> " + Esc(line))) : "";
        var text = $"{marker} *{Esc(task.Level)} priority* from {Esc(me.Name)}: *{Esc(task.Title)}*\n{Esc(DueText(task.DueAt))}{details}{link}";
        var sent = 0;

        foreach (var person in people)
        {
            if (await SlackCallAsync("chat.postMessage", new() { ["channel"] = person.SlackUserId!, ["text"] = text }) is not null)
            {
                sent++;
            }

            await Task.Delay(400);
        }

        return sent;
    }

    private void ResetPriorityForm()
    {
        _priorityFormOpen.Value = false;
        _priorityTitle.Value = "";
        _priorityDetails.Value = "";
        _priorityLevel.Value = "High";
        _priorityDue.Value = "week";
        _priorityTeams.Clear();
        _priorityNotifySlack.Value = false;
    }

    private void MarkSeen(string id, Expert me)
    {
        if (_priorities.ContainsKey(id) && _priorities[id] is { } task && IsRelevant(task, me) && !task.SeenBy.Contains(me.Id))
        {
            _priorities[id] = task with { SeenBy = [.. task.SeenBy, me.Id] };
        }
    }

    // Only the poster or an admin may close, reopen or delete a priority
    private bool CanEdit(PriorityTask task, Expert me) => task.CreatedById == me.Id || _isAdmin.Value;

    private void SetDone(string id, Expert me, bool done)
    {
        if (_priorities.ContainsKey(id) && _priorities[id] is { } task && CanEdit(task, me))
        {
            _priorities[id] = task with { Done = done };
        }
    }

    private void DeletePriority(string id, Expert me)
    {
        if (_priorities.ContainsKey(id) && _priorities[id] is { } task && CanEdit(task, me))
        {
            _priorities.Remove(id);
            _deletedPriority.Value = task;
        }
    }

    // Pinned at the top of the Ask page: the open priorities that concern this person
    private void RenderPinnedPriorities(UIView view, Expert me)
    {
        var open = RelevantPriorities(me, done: false);

        if (open.Count == 0)
        {
            return;
        }

        view.Column(["gap-2"], content: pinned =>
        {
            pinned.Row(["items-center justify-between gap-2"], content: head =>
            {
                head.Text([Text.Overline, "text-muted-foreground"], text: "Team priorities");
                head.Button([Button.GhostSm], text: open.Count > 3 ? $"See all {open.Count}" : "Open priorities", onClick: async () => _tab.Value = "priorities");
            });

            foreach (var task in open.Take(3))
            {
                pinned.Button(["w-full text-left flex items-center gap-3 rounded-lg border border-border bg-card px-4 py-3 hover:bg-foreground/5 transition-colors",
                        task.Level == "Critical" ? "border-l-4 border-l-error-500" : task.Level == "High" ? "border-l-4 border-l-warning-500" : ""],
                    text: $"Open priority: {task.Title}",
                    key: $"pin-{task.Id}",
                    onClick: async () => _tab.Value = "priorities",
                    content: row =>
                    {
                        row.Badge(task.Level, tone: LevelTone(task.Level), size: BadgeSize.Sm, dot: task.Level == "Critical");
                        row.Text(["text-sm font-medium flex-1 min-w-0 truncate"], text: task.Title);
                        row.Text([Text.Caption, "text-muted-foreground shrink-0 hidden sm:block"], text: DueText(task.DueAt));

                        if (!task.SeenBy.Contains(me.Id) && task.CreatedById != me.Id)
                        {
                            row.Box(["w-2 h-2 rounded-full bg-brand-solid shrink-0"]);
                        }
                    });
            }
        });
    }

    private void RenderPriorities(UIView view, Expert me)
    {
        var canManage = CanManagePriorities(me);

        view.Row(["items-end justify-between gap-4 flex-wrap"], content: head =>
        {
            head.Column(["gap-1"], content: title =>
            {
                title.Text([Text.H2, "font-heading"], text: "Priorities");
                title.Text([Text.Body, "text-muted-foreground"], text: canManage
                    ? "Post what matters most right now. Only the teams you pick see it."
                    : "What your managers flagged as most important for your team.");
            });

            if (canManage && !_priorityFormOpen.Value)
            {
                head.Button([Button.PrimaryMd, "gap-2"], text: "New priority", onClick: async () => _priorityFormOpen.Value = true, content: b =>
                {
                    b.Icon([Icon.Sm], name: "flag");
                    b.Text(text: "New priority");
                });
            }
        });

        if (_priorityNotice.Value is { } notice)
        {
            view.Alert(notice, tone: SemanticTone.Success, onDismiss: async () => _priorityNotice.Value = null);
        }

        if (_deletedPriority.Value is { } deleted && CanEdit(deleted, me))
        {
            view.Alert($"Deleted \"{deleted.Title}\"", tone: SemanticTone.Neutral, onDismiss: async () => _deletedPriority.Value = null, content: alert =>
                alert.Button([Button.OutlineSm, "mt-3 self-start"], text: "Undo", onClick: async () =>
                {
                    if (CanEdit(deleted, me))
                    {
                        _priorities[deleted.Id] = deleted;
                        _deletedPriority.Value = null;
                    }
                }));
        }

        if (canManage && _priorityFormOpen.Value)
        {
            RenderPriorityForm(view, me);
        }

        view.Row(["p-1 rounded-full bg-foreground/5 gap-1 self-start"], content: seg =>
        {
            foreach (var (value, label) in new[] { ("open", "Open"), ("done", "Done") })
            {
                seg.Button(["h-9 px-4 rounded-full text-sm font-medium transition-colors",
                        _priorityFilter.Value == value ? "bg-card text-foreground shadow-xs" : "bg-transparent text-muted-foreground hover:text-foreground"],
                    text: label, onClick: async () => _priorityFilter.Value = value);
            }
        });

        var list = RelevantPriorities(me, done: _priorityFilter.Value == "done");

        if (list.Count == 0)
        {
            view.EmptyState(_priorityFilter.Value == "done" ? "Nothing finished yet" : "No priorities right now", icon: "flag",
                description: canManage ? "Post one when something needs your team's attention first." : "When a manager flags something for your team, it shows up here.");
            return;
        }

        view.Column(["gap-3"], content: column =>
        {
            foreach (var task in list)
            {
                RenderPriorityCard(column, me, task);
            }
        });
    }

    private void RenderPriorityCard(UIView view, Expert me, PriorityTask task)
    {
        var seen = task.SeenBy.Contains(me.Id);
        var mine = CanEdit(task, me);

        view.Card([task.Level == "Critical" && !task.Done ? Card.Elevated : Card.Default, "p-5 gap-3",
                task.Done ? "opacity-70" : task.Level == "Critical" ? "border-l-4 border-l-error-500" : task.Level == "High" ? "border-l-4 border-l-warning-500" : ""],
            key: $"priority-{task.Id}",
            content: card =>
            {
                card.Row(["items-start justify-between gap-3 flex-wrap"], content: top =>
                {
                    top.Text(["font-semibold text-base flex-1 min-w-48"], text: task.Title);
                    top.Row(["items-center gap-1.5 shrink-0"], content: badges =>
                    {
                        badges.Badge(task.Level, tone: LevelTone(task.Level), size: BadgeSize.Sm, dot: task.Level == "Critical");

                        if (task.Done)
                        {
                            badges.Badge("Done", tone: SemanticTone.Success, size: BadgeSize.Sm);
                        }
                    });
                });

                if (task.Details.Length > 0)
                {
                    card.Text([Text.Body, "text-muted-foreground leading-relaxed whitespace-pre-line"], text: task.Details);
                }

                card.Row(["items-center gap-x-3 gap-y-1 flex-wrap"], content: meta =>
                {
                    meta.Row(["items-center gap-1.5"], content: due =>
                    {
                        due.Icon([Icon.Sm, "text-muted-foreground"], name: "calendar");
                        due.Text([Text.Caption, task.DueAt < DateTime.UtcNow && !task.Done ? "text-error-700 font-semibold" : "text-muted-foreground"], text: DueText(task.DueAt));
                    });
                    meta.Row(["items-center gap-1.5"], content: who =>
                    {
                        who.Icon([Icon.Sm, "text-muted-foreground"], name: "users");
                        who.Text([Text.Caption, "text-muted-foreground"], text: task.Teams.Length == 0 ? "Everyone" : string.Join(", ", task.Teams));
                    });
                    meta.Text([Text.Caption, "text-muted-foreground"], text: $"{(task.CreatedById == me.Id ? "You" : task.CreatedByName)} · {Ago(task.CreatedAt)}");

                    // Only the poster and admins see who has read it
                    if (mine)
                    {
                        var audience = _experts.Values.Count(e => IsAudience(task, e) && e.Id != task.CreatedById);
                        var count = task.SeenBy.Count(id => id != task.CreatedById);
                        meta.Text([Text.Caption, "text-muted-foreground"], text: $"· Seen by {count} of {audience}");
                    }
                });

                card.Row(["flex-wrap gap-2 pt-1"], content: actions =>
                {
                    if (!task.Done && task.CreatedById != me.Id)
                    {
                        if (seen)
                        {
                            actions.Row(["items-center gap-1.5 text-muted-foreground"], content: ok =>
                            {
                                ok.Icon([Icon.Sm], name: "check");
                                ok.Text([Text.Caption], text: "You've seen this");
                            });
                        }
                        else
                        {
                            actions.Button([Button.PrimaryMd, "gap-1.5"], text: "Got it", onClick: async () => MarkSeen(task.Id, me), content: b =>
                            {
                                b.Icon([Icon.Sm], name: "check");
                                b.Text(text: "Got it");
                            });
                        }
                    }

                    if (mine)
                    {
                        actions.Button([Button.OutlineMd], text: task.Done ? "Reopen" : "Mark done", onClick: async () => SetDone(task.Id, me, !task.Done));
                        actions.Button([Button.GhostMd], text: "Delete", onClick: async () => DeletePriority(task.Id, me));
                    }
                });
            });
    }

    private void RenderPriorityForm(UIView view, Expert me)
    {
        var teams = Teams();
        var title = _priorityTitle.Value.Trim();
        var problem = title.Length == 0 ? "Add a title to post."
            : title.Length > MaxPriorityTitle ? $"Keep the title under {MaxPriorityTitle} characters."
            : _priorityDetails.Value.Trim().Length > MaxPriorityDetails ? $"Keep the details under {MaxPriorityDetails} characters."
            : null;

        var draft = new PriorityTask("", title, "", _priorityLevel.Value, [.. _priorityTeams], me.Id, me.Name, DateTime.UtcNow, null, false, []);
        var audience = _experts.Values.Count(e => IsAudience(draft, e) && e.Id != me.Id);
        var onSlack = _experts.Values.Count(e => IsAudience(draft, e) && e.Id != me.Id && e.SlackUserId is not null);

        view.Card([Card.Elevated, "p-6 gap-5"], content: card =>
        {
            card.Text([Text.H4, "font-heading"], text: "New priority");
            card.TextField(["default"], label: "What needs attention", placeholder: "Close the Q4 security questionnaire for Acme", bind: _priorityTitle);
            card.TextArea(["default"], label: "Details", placeholder: "Why it matters, what done looks like, who to ask.", rows: 3, autoResize: true, maxRows: 8, bind: _priorityDetails);

            card.Column(["gap-4 sm:grid sm:grid-cols-2"], content: grid =>
            {
                grid.Column(["gap-1.5"], content: level =>
                {
                    level.Text([Text.Label], text: "Level");
                    level.Row(["p-1 rounded-full bg-foreground/5 gap-1 self-start"], content: seg =>
                    {
                        foreach (var option in PriorityLevels)
                        {
                            seg.Button(["h-9 px-4 rounded-full text-sm font-medium transition-colors",
                                    _priorityLevel.Value == option ? "bg-card text-foreground shadow-xs" : "bg-transparent text-muted-foreground hover:text-foreground"],
                                text: option, onClick: async () => _priorityLevel.Value = option);
                        }
                    });
                });

                grid.Column(["gap-1.5"], content: due =>
                {
                    due.Text([Text.Label], text: "Due");
                    due.Select(["w-full"], ariaLabel: "Due", bind: _priorityDue, options:
                    [
                        new SelectOption("today", "Today"),
                        new SelectOption("week", "Within a week"),
                        new SelectOption("month", "Within a month"),
                        new SelectOption("none", "No deadline"),
                    ]);
                });
            });

            card.Column(["gap-2"], content: who =>
            {
                who.Text([Text.Label], text: "Who should see it");
                who.Row(["flex-wrap gap-1.5"], content: chips =>
                {
                    var everyone = _priorityTeams.Count == 0;
                    chips.Button(["h-8 px-3 rounded-full text-xs font-medium transition-colors border",
                            everyone ? "bg-foreground text-background border-foreground" : "bg-transparent border-border hover:bg-foreground/5"],
                        text: "Everyone", onClick: async () => _priorityTeams.Clear());

                    foreach (var team in teams)
                    {
                        var on = _priorityTeams.Contains(team);
                        chips.Button(["h-8 px-3 rounded-full text-xs font-medium transition-colors border",
                                on ? "bg-foreground text-background border-foreground" : "bg-transparent border-border hover:bg-foreground/5"],
                            text: team, key: $"team-{team}",
                            onClick: async () =>
                            {
                                if (!_priorityTeams.Remove(team))
                                {
                                    _priorityTeams.Add(team);
                                }
                            });
                    }
                });
                who.Text([Text.Caption, "text-muted-foreground"], text: $"{audience} {(audience == 1 ? "person" : "people")} will see this.");
            });

            if (SlackReady && _slackWorkspace.Value is not null)
            {
                card.Row(["items-center justify-between gap-4 rounded-lg border border-border px-4 py-3"], content: row =>
                {
                    row.Column(["gap-0.5"], content: text =>
                    {
                        text.Text(["text-sm font-medium"], text: "Also DM them on Slack");
                        text.Text([Text.Caption, "text-muted-foreground"], text: $"{onSlack} of them are on Slack.");
                    });
                    row.Switch(ariaLabel: "Also DM them on Slack", bind: _priorityNotifySlack);
                });
            }

            card.Row(["flex-wrap items-center gap-2"], content: actions =>
            {
                var label = _priorityNotifySlack.Value && SlackReady ? $"Post and DM {onSlack} on Slack" : "Post priority";
                actions.Button([Button.PrimaryMd, "gap-2"], text: label, disabled: problem is not null || _posting.Value, onClick: async () => await PostPriorityAsync(me), content: b =>
                {
                    if (_posting.Value)
                    {
                        b.Spinner(size: SpinnerSize.Sm);
                    }
                    else
                    {
                        b.Icon([Icon.Sm], name: "send");
                    }

                    b.Text(text: _posting.Value ? "Posting…" : label);
                });
                actions.Button([Button.GhostMd], text: "Cancel", onClick: async () => ResetPriorityForm());

                if (problem is not null)
                {
                    actions.Text([Text.Caption, "text-muted-foreground"], text: problem);
                }
            });
        });
    }
}
