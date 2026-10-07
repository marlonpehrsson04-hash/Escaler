public partial class EscalerApp
{
    private static readonly string[] CommonTitles =
        ["Software Engineer", "Product Manager", "Product Designer", "Account Executive", "HR Partner", "Financial Controller", "Data Analyst", "Customer Success Manager"];

    private static readonly string[] CommonDepartments =
        ["Engineering", "Product", "Design", "Sales", "Marketing", "Customer Success", "Finance", "People & HR", "Legal", "Operations", "IT", "Data", "Leadership"];

    // The form is user-scoped so a half-filled profile survives a reload
    private readonly UserReactive<bool> _formLoaded = new(false);
    private readonly UserReactive<string> _formName = new("");
    private readonly UserReactive<string> _formTitle = new("");
    private readonly UserReactive<string> _formDepartment = new("");
    private readonly UserReactive<string> _formAbout = new("");
    private readonly UserReactive<string> _formEmail = new("");
    private readonly UserReactive<bool> _formAvailable = new(true);
    private readonly UserReactiveList<string> _formSkills = new();
    private readonly UserReactiveList<string> _skillIdeas = new();
    private readonly UserReactive<bool> _suggesting = new(false);
    private readonly UserReactive<string?> _suggestError = new(null);
    private readonly UserReactive<bool> _saving = new(false);
    private readonly UserReactive<string?> _formNotice = new(null);

    private void FillForm(Expert? existing, ClientProfile? profile)
    {
        _formName.Value = existing?.Name ?? profile?.Name ?? string.Join(' ', new[] { profile?.FirstName, profile?.LastName }.Where(p => !string.IsNullOrWhiteSpace(p)));
        _formTitle.Value = existing?.Title ?? "";
        _formDepartment.Value = existing?.Department ?? "";
        _formAbout.Value = existing?.About ?? "";
        _formEmail.Value = existing?.Email ?? profile?.Email ?? "";
        _formAvailable.Value = existing?.Available ?? true;
        _formSkills.ReplaceAll(existing?.Skills ?? []);
        _skillIdeas.Clear();
        _formNotice.Value = null;
        _formLoaded.Value = true;
    }

    private void OpenProfileEditor(Expert me)
    {
        FillForm(me, null);
        _tab.Value = "profile";
    }

    private void RenderOnboarding(UIView view)
    {
        view.ScrollArea(["flex-1 min-h-0"], content: scroll =>
        {
            scroll.Column(["min-h-full w-full max-w-6xl mx-auto px-4 md:px-10 py-10 md:py-16 gap-10 lg:flex-row lg:items-start lg:gap-16"], content: page =>
            {
                page.Column(["gap-6 lg:flex-1 lg:sticky lg:top-16 motion-[0:opacity-0_translate-y-[10px],100:opacity-100_translate-y-0] motion-duration-500ms"], content: hero =>
                {
                    hero.Row(["items-center gap-3"], content: logo =>
                    {
                        logo.Box(["w-11 h-11 rounded-xl bg-brand-solid flex items-center justify-center shadow-sm"], content: mark =>
                            mark.Icon(["text-primary-foreground"], name: "arrow-up-right", size: IconSize.Lg));
                        logo.Text(["font-heading text-2xl font-bold tracking-tight"], text: "Escaler");
                    });

                    hero.Text(["font-heading text-5xl md:text-6xl font-bold tracking-tight leading-[0.95]"], text: "Know who knows.");
                    hero.Text([Text.BodyLg, "text-muted-foreground max-w-md"],
                        text: "Tell Escaler in Slack what's blocking you. It finds the colleague with the right expertise and room in their week, and puts you two in a conversation.");

                    hero.Column(["gap-3 pt-2"], content: steps =>
                    {
                        Step(steps, "user-round-pen", "Add what you know", "Your title, team and the things people can ask you about.");
                        Step(steps, "sparkles", "AI matches every request", "By real expertise first, then by who has the lightest load.");
                        Step(steps, "messages-square", "Connected in Slack", "Accept or pass it on with one tap. Nothing gets stuck in a DM.");
                    });
                });

                page.Card([Card.Elevated, "w-full lg:w-[480px] shrink-0 p-6 md:p-8 gap-6 motion-[0:opacity-0_translate-y-[16px],100:opacity-100_translate-y-0] motion-duration-600ms motion-delay-[120ms] motion-fill-both"], content: card =>
                {
                    card.Column(["gap-1"], content: head =>
                    {
                        head.Text([Text.Overline, "text-muted-foreground"], text: "Step 1 of 1");
                        head.Text([Text.H3, "font-heading"], text: "Set up your expert profile");
                    });

                    RenderProfileForm(card, isNew: true);
                });
            });
        });
    }

    private static void Step(UIView view, string icon, string title, string body)
    {
        view.Row(["gap-3 items-start"], content: row =>
        {
            row.Box(["w-9 h-9 shrink-0 rounded-lg bg-brand-solid/15 flex items-center justify-center"], content: tile =>
                tile.Icon([Icon.Sm, "text-brand-700"], name: icon));
            row.Column(["gap-0.5"], content: text =>
            {
                text.Text(["text-sm font-semibold"], text: title);
                text.Text([Text.Caption, "text-muted-foreground"], text: body);
            });
        });
    }

    private void RenderProfilePage(UIView view)
    {
        view.Column(["gap-1"], content: head =>
        {
            head.Text([Text.H2, "font-heading"], text: "Your profile");
            head.Text([Text.Body, "text-muted-foreground"], text: "This is what Escaler reads when it decides who should get a request.");
        });

        view.Card([Card.Elevated, "p-6 md:p-8 gap-6 max-w-2xl"], content: card => RenderProfileForm(card, isNew: false));
    }

    private void RenderProfileForm(UIView view, bool isNew)
    {
        var missing = new List<string>();

        if (_formName.Value.Trim().Length == 0)
        {
            missing.Add("your name");
        }

        if (_formTitle.Value.Trim().Length == 0)
        {
            missing.Add("a title");
        }

        if (_formDepartment.Value.Length == 0)
        {
            missing.Add("a team");
        }

        if (_formSkills.Count == 0)
        {
            missing.Add("at least one skill");
        }

        view.Column(["gap-5"], content: form =>
        {
            form.TextField(["default"], label: "Full name", placeholder: "Alex Johansson", bind: _formName);

            form.Column(["gap-2"], content: title =>
            {
                title.TextField(["default"], label: "Profession title", placeholder: "Senior Backend Engineer", bind: _formTitle);

                if (_formTitle.Value.Length == 0)
                {
                    title.Row(["flex-wrap gap-1.5"], content: chips =>
                    {
                        foreach (var common in CommonTitles)
                        {
                            chips.Button([Badge.NeutralSm, "cursor-pointer hover:bg-foreground/10 transition-colors"], text: common, onClick: async () => _formTitle.Value = common);
                        }
                    });
                }
            });

            form.Column(["gap-1.5"], content: team =>
            {
                team.Text([Text.Label], text: "Team");
                var departments = CommonDepartments.Concat(_experts.Values.Select(e => e.Department)).Where(d => d.Length > 0).Distinct().Order();
                team.Select(["w-full"], ariaLabel: "Team", placeholder: "Choose your team", bind: _formDepartment,
                    options: [.. departments.Select(d => new SelectOption(d, d))]);
            });

            RenderSkillEditor(form);

            form.TextArea(["default"], label: "Ask me about…", placeholder: "Billing code, refunds and anything that touches the database.", rows: 2, autoResize: true, maxRows: 5, bind: _formAbout);

            form.TextField(["default"], label: "Work email", placeholder: "you@company.com", type: "email", bind: _formEmail);
            form.Text([Text.Caption, "text-muted-foreground -mt-3"], text: "Escaler finds your Slack account with this, so use the email you sign in to Slack with.");

            form.Row(["items-center justify-between gap-4 rounded-lg border border-border px-4 py-3"], content: row =>
            {
                row.Column(["gap-0.5"], content: text =>
                {
                    text.Text(["text-sm font-medium"], text: "Available for requests");
                    text.Text([Text.Caption, "text-muted-foreground"], text: "Turn off when you're on leave or heads-down.");
                });
                row.Switch(ariaLabel: "Available for requests", bind: _formAvailable);
            });

            if (_formNotice.Value is { } notice)
            {
                form.Alert(notice, tone: notice.StartsWith("Saved") ? SemanticTone.Success : SemanticTone.Warning, onDismiss: async () => _formNotice.Value = null);
            }

            form.Column(["gap-2"], content: actions =>
            {
                actions.Button([Button.PrimaryLg, "w-full"],
                    text: _saving.Value ? "Saving…" : isNew ? "Join the directory" : "Save profile",
                    disabled: missing.Count > 0 || _saving.Value,
                    onClick: SaveProfileAsync);

                if (missing.Count > 0)
                {
                    actions.Text([Text.Caption, "text-muted-foreground text-center"], text: $"Add {string.Join(", ", missing)} to continue.");
                }
            });
        });
    }

    private void RenderSkillEditor(UIView view)
    {
        view.Column(["gap-2"], content: skills =>
        {
            skills.Text([Text.Label], text: "What can people ask you about?");

            skills.TextField(["default"], placeholder: "Type a skill and press Enter, e.g. Stripe, contracts, Figma", ariaLabel: "Add a skill", clearOnSubmit: true,
                onSubmit: async text => AddSkills(text));

            if (_formSkills.Count > 0)
            {
                skills.Row(["flex-wrap gap-1.5"], content: chips =>
                {
                    foreach (var skill in _formSkills)
                    {
                        chips.Button(["inline-flex items-center gap-1 h-8 pl-3 pr-2 rounded-full bg-foreground text-background text-xs font-medium hover:opacity-80 transition-opacity"],
                            text: $"Remove {skill}",
                            key: $"skill-{skill}",
                            onClick: async () => _formSkills.Remove(skill),
                            content: chip =>
                            {
                                chip.Text(text: skill);
                                chip.Icon([Icon.Sm, "opacity-70"], name: "x");
                            });
                    }
                });
            }

            var ideas = _skillIdeas.Where(i => !_formSkills.Contains(i, StringComparer.OrdinalIgnoreCase)).ToList();

            if (ideas.Count > 0)
            {
                skills.Row(["flex-wrap gap-1.5 motion-[0:opacity-0,100:opacity-100] motion-duration-300ms"], content: chips =>
                {
                    foreach (var idea in ideas)
                    {
                        chips.Button(["inline-flex items-center gap-1 h-8 px-3 rounded-full border border-dashed border-brand text-xs font-medium bg-transparent hover:bg-brand-solid/10 transition-colors"],
                            text: $"Add {idea}",
                            key: $"idea-{idea}",
                            onClick: async () => _formSkills.Add(idea),
                            content: chip =>
                            {
                                chip.Icon([Icon.Sm], name: "plus");
                                chip.Text(text: idea);
                            });
                    }
                });
            }

            if (_suggestError.Value is { } error)
            {
                skills.Text([Text.Caption, "text-error-700"], text: error);
            }

            if (_formTitle.Value.Trim().Length > 0)
            {
                skills.Button([Button.GhostSm, "self-start gap-1.5"],
                    text: "Suggest skills from my title",
                    disabled: _suggesting.Value,
                    onClick: SuggestSkillsAsync,
                    content: b =>
                    {
                        if (_suggesting.Value)
                        {
                            b.Spinner(size: SpinnerSize.Sm);
                        }
                        else
                        {
                            b.Icon([Icon.Sm], name: "sparkles");
                        }

                        b.Text(text: _suggesting.Value ? "Thinking…" : $"Suggest skills for a {_formTitle.Value.Trim()}");
                    });
            }
        });
    }

    private void AddSkills(string text)
    {
        foreach (var raw in text.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var skill = raw.Length > 40 ? raw[..40] : raw;

            if (!_formSkills.Contains(skill, StringComparer.OrdinalIgnoreCase))
            {
                _formSkills.Add(skill);
            }
        }
    }

    private async Task SuggestSkillsAsync()
    {
        if (_suggesting.Value)
        {
            return;
        }

        using var _ = _suggesting.AsToken();
        _suggestError.Value = null;

        try
        {
            var team = _formDepartment.Value.Length > 0 ? $" in {_formDepartment.Value}" : "";
            var ideas = await Emerge.AskAsync<SkillIdeas>(
                $"List 8 concrete things colleagues would ask a {_formTitle.Value.Trim()}{team} for help with at a mid-sized company. " +
                "Each is a short skill, tool or topic of 1-3 words (like \"Stripe refunds\", \"Parental leave\", \"Figma\"). No generic soft skills.");

            _skillIdeas.ReplaceAll(ideas.Skills.Select(s => s.Trim()).Where(s => s.Length is > 0 and <= 40).Distinct(StringComparer.OrdinalIgnoreCase).Take(8));
        }
        catch (Exception e)
        {
            Log.Instance.Warning($"Skill suggestions failed: {e.GetType().Name}: {e.Message}");
            _suggestError.Value = "Couldn't suggest skills just now. Try again, or type your own.";
        }
    }

    private async Task SaveProfileAsync()
    {
        if (_saving.Value || ReactiveScope.UserIdOrNull is not { Length: > 0 } userId)
        {
            return;
        }

        using var _ = _saving.AsToken();

        var existing = FindExpert(userId);
        var email = _formEmail.Value.Trim();
        var slackUserId = existing?.Email == email ? existing.SlackUserId : null;
        string? slackProblem = null;

        if (SlackReady && (slackUserId is null || existing?.Email != email))
        {
            (slackUserId, slackProblem) = await LookupSlackByEmailAsync(email);
        }

        _experts[userId] = new Expert(
            Id: userId,
            Name: _formName.Value.Trim(),
            Title: _formTitle.Value.Trim(),
            Department: _formDepartment.Value,
            Skills: [.. _formSkills],
            About: _formAbout.Value.Trim(),
            Email: email.Length > 0 ? email : null,
            SlackUserId: slackUserId,
            Available: _formAvailable.Value,
            IsDemo: false,
            JoinedAt: existing?.JoinedAt ?? DateTime.UtcNow);

        _formNotice.Value = slackProblem ?? (existing is null
            ? null
            : SlackReady && slackUserId is not null ? "Saved. Linked to your Slack account." : "Saved.");

        if (existing is null)
        {
            _tab.Value = "ask";
        }
    }
}
