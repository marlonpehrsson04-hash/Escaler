public partial class EscalerApp
{
    private readonly ClientReactive<string> _peopleSearch = new("");
    private readonly ClientReactive<string> _peopleTeam = new("all");
    private readonly ClientReactive<string> _peopleAvailability = new("all");
    private readonly ClientReactive<string> _peopleSort = new("name");

    // Demo people removed in one go, kept so Undo can put them back
    private readonly Reactive<Expert[]?> _removedDemo = new(null);

    private IReadOnlyList<Expert> VisiblePeople()
    {
        IEnumerable<Expert> rows = _experts.Values;
        var search = _peopleSearch.Value.Trim();

        if (search.Length > 0)
        {
            rows = rows.Where(e =>
                e.Name.Contains(search, StringComparison.OrdinalIgnoreCase)
                || e.Title.Contains(search, StringComparison.OrdinalIgnoreCase)
                || e.About.Contains(search, StringComparison.OrdinalIgnoreCase)
                || e.Skills.Any(s => s.Contains(search, StringComparison.OrdinalIgnoreCase)));
        }

        if (_peopleTeam.Value != "all")
        {
            rows = rows.Where(e => e.Department == _peopleTeam.Value);
        }

        rows = _peopleAvailability.Value switch
        {
            "available" => rows.Where(e => e.Available),
            "away" => rows.Where(e => !e.Available),
            _ => rows,
        };

        rows = _peopleSort.Value == "load"
            ? rows.OrderBy(e => OpenCount(e.Id)).ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            : rows.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase);

        return rows.ToList();
    }

    private void RenderPeople(UIView view, Expert me)
    {
        var all = _experts.Values.ToList();
        var demoCount = all.Count(e => e.IsDemo);

        view.Row(["items-end justify-between gap-4 flex-wrap"], content: head =>
        {
            head.Column(["gap-1"], content: title =>
            {
                title.Text([Text.H2, "font-heading"], text: "People");
                title.Text([Text.Body, "text-muted-foreground"], text: $"{all.Count} in the directory · {all.Count(e => e.Available)} taking requests");
            });

            if (demoCount > 0)
            {
                head.Button([Button.GhostSm, "gap-1.5"], text: $"Remove {demoCount} demo people", onClick: async () =>
                {
                    var demos = _experts.Values.Where(e => e.IsDemo).ToArray();

                    foreach (var demo in demos)
                    {
                        _experts.Remove(demo.Id);
                    }

                    _removedDemo.Value = demos;
                }, content: b =>
                {
                    b.Icon([Icon.Sm], name: "user-x");
                    b.Text(text: $"Remove {demoCount} demo people");
                });
            }
        });

        if (_removedDemo.Value is { Length: > 0 } removed)
        {
            view.Alert($"Removed {removed.Length} demo people", tone: SemanticTone.Neutral, onDismiss: async () => _removedDemo.Value = null, content: alert =>
                alert.Button([Button.OutlineSm, "mt-3 self-start"], text: "Undo", onClick: async () =>
                {
                    foreach (var demo in removed)
                    {
                        _experts[demo.Id] = demo;
                    }

                    _removedDemo.Value = null;
                }));
        }

        // Team options come from the data, with a count, so a filter never hides people silently
        var teams = all.GroupBy(e => e.Department).OrderBy(g => g.Key).ToList();

        view.Row(["flex-wrap items-center gap-2"], content: bar =>
        {
            bar.TextField([Input.DefaultSm, "w-full sm:w-64"], placeholder: "Search name, title or skill", ariaLabel: "Search people", bind: _peopleSearch);
            bar.Select(["w-full sm:w-48"], triggerStyle: [Select.Size.Sm], ariaLabel: "Team", bind: _peopleTeam,
                options: [new SelectOption("all", $"All teams ({all.Count})"), .. teams.Select(g => new SelectOption(g.Key, $"{g.Key} ({g.Count()})"))]);
            bar.Select(["w-full sm:w-40"], triggerStyle: [Select.Size.Sm], ariaLabel: "Availability", bind: _peopleAvailability,
                options: [new SelectOption("all", "Anyone"), new SelectOption("available", "Available"), new SelectOption("away", "Away")]);
            bar.Select(["w-full sm:w-44"], triggerStyle: [Select.Size.Sm], ariaLabel: "Sort", bind: _peopleSort,
                options: [new SelectOption("name", "Sort by name"), new SelectOption("load", "Least busy first")]);
        });

        var visible = VisiblePeople();

        if (visible.Count == 0)
        {
            view.EmptyState("No one matches", icon: "search-x", description: "Try another skill or clear the filters.", action: a =>
                a.Button([Button.OutlineMd], text: "Clear filters", onClick: async () =>
                {
                    _peopleSearch.Value = "";
                    _peopleTeam.Value = "all";
                    _peopleAvailability.Value = "all";
                }));
            return;
        }

        view.Column(["gap-4 md:grid md:grid-cols-2 lg:grid-cols-3"], content: grid =>
        {
            foreach (var person in visible)
            {
                grid.Card([Card.Default, "p-5 gap-4 hover:shadow-md transition-shadow"], key: $"person-{person.Id}", content: card =>
                {
                    card.Row(["items-center gap-3"], content: who =>
                    {
                        Avatar(who, person.Name, mine: person.Id == me.Id, size: "w-11 h-11 text-sm");
                        who.Column(["min-w-0 flex-1"], content: name =>
                        {
                            name.Text(["font-semibold truncate"], text: person.Id == me.Id ? $"{person.Name} (you)" : person.Name);
                            name.Text([Text.Caption, "text-muted-foreground truncate"], text: person.Title);
                        });
                    });

                    card.Row(["flex-wrap gap-1.5"], content: tags =>
                    {
                        tags.Badge(person.Department, tone: SemanticTone.Brand, size: BadgeSize.Sm, outline: true);

                        if (person.IsDemo)
                        {
                            tags.Badge("Demo", size: BadgeSize.Sm, outline: true);
                        }
                    });

                    SkillChips(card, person.Skills, 5);

                    if (person.About.Length > 0)
                    {
                        card.Text([Text.Caption, "text-muted-foreground line-clamp-2"], text: person.About);
                    }

                    card.Row(["items-center justify-between gap-2 pt-3 border-t border-border mt-auto"], content: foot =>
                    {
                        if (person.Available)
                        {
                            Workload(foot, person);
                        }
                        else
                        {
                            foot.Row(["items-center gap-2"], content: away =>
                            {
                                away.Box(["w-2 h-2 rounded-full bg-muted-foreground/50"]);
                                away.Text([Text.Caption, "text-muted-foreground"], text: "Away");
                            });
                        }

                        foot.Row(["items-center gap-1 text-muted-foreground"], content: slack =>
                        {
                            slack.Icon([Icon.Sm], name: person.SlackUserId is null ? "unlink" : "slack");
                            slack.Text([Text.Caption], text: person.SlackUserId is null ? "Not on Slack" : "Slack");
                        });
                    });
                });
            }
        });
    }
}
