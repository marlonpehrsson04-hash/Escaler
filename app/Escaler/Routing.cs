public partial class EscalerApp
{
    private const string RoutingSystemPrompt =
        """
        You route workplace problems to the colleague best placed to solve them, so work lands with the right person on the first try.
        Pick only from the roster you are given and never invent people.
        Prefer direct, demonstrated expertise over loose association: someone whose skills name the exact system, tool or process beats a generalist.
        When two people fit about equally, prefer the one with fewer open handoffs, so nobody gets buried.
        Scores are 0-100 for fit. Leave out anyone under 30.
        """;

    // Ranks the roster for one problem; throws when the model fails, so callers show a retry
    private async Task<RoutePlan> PlanRouteAsync(string problem, IReadOnlyList<Expert> candidates)
    {
        var roster = string.Join("\n", candidates.Select(e =>
            $"- id: {e.Id} | {e.Name} | {e.Title}, {e.Department} | skills: {string.Join(", ", e.Skills)} | about: {e.About} | open handoffs: {OpenCount(e.Id)}"));

        var plan = await Emerge.Run<RoutePlan>(LLMModel.Claude46Sonnet, pass =>
        {
            pass.SystemPrompt = RoutingSystemPrompt;
            pass.Command =
                $"""
                Problem from a colleague:
                {problem}

                Roster:
                {roster}

                Return up to 3 matches, best first.
                - Summary: the task as a short imperative title, under 70 characters.
                - Urgency: Low, Normal or High, judged from the problem's own words.
                - Reason: one plain sentence on why this person fits, citing their actual skills. No flattery.
                - NeedsClarification: true only when the problem is too vague to route at all (like "help" or "question"); then ClarifyingQuestion is one short question and Matches is empty. Otherwise ClarifyingQuestion is "".

                Return JSON:
                {pass.JsonSchema}
                """;
        });

        // The model can still name an id that is not on the roster; drop those rather than route to nobody
        var valid = candidates.Select(c => c.Id).ToHashSet();
        var matches = plan.Matches
            .Where(m => valid.Contains(m.ExpertId) && m.Score >= 30)
            .DistinctBy(m => m.ExpertId)
            .OrderByDescending(m => m.Score)
            .Take(3)
            .ToList();

        var urgency = plan.Urgency is "Low" or "High" ? plan.Urgency : "Normal";
        return plan with { Matches = matches, Urgency = urgency };
    }

    private int OpenCount(string expertId) => _handoffs.Values.Count(h => h.IsOpen && h.AssigneeId == expertId);

    private Expert? FindExpert(string? id) => id is not null && _experts.ContainsKey(id) ? _experts[id] : null;

    private Expert? FindBySlack(string slackUserId) => _experts.Values.FirstOrDefault(e => e.SlackUserId == slackUserId);

    // Creates the handoff and tells the first candidate. Works with or without Slack
    private async Task<Handoff> StartHandoffAsync(string problem, RoutePlan plan, string requesterName, string? requesterExpertId, string? requesterSlackId, string source, string? firstChoiceId = null)
    {
        var ordered = plan.Matches.ToList();

        // On the web the person can pick a runner-up; that one goes first and the rest stay as fallbacks
        var at = firstChoiceId is null ? -1 : ordered.FindIndex(m => m.ExpertId == firstChoiceId);

        if (at > 0)
        {
            var chosen = ordered[at];
            ordered.RemoveAt(at);
            ordered.Insert(0, chosen);
        }

        var now = DateTime.UtcNow;
        var handoff = new Handoff(
            Id: Guid.NewGuid().ToString("N")[..10],
            Summary: plan.Summary,
            Problem: problem,
            Urgency: plan.Urgency,
            RequesterName: requesterName,
            RequesterExpertId: requesterExpertId,
            RequesterSlackId: requesterSlackId,
            CandidateIds: [.. ordered.Select(m => m.ExpertId)],
            Reasons: [.. ordered.Select(m => m.Reason)],
            Index: 0,
            Status: ordered.Count > 0 ? HandoffStatus.Waiting : HandoffStatus.Unassigned,
            Source: source,
            CreatedAt: now,
            UpdatedAt: now);

        _handoffs[handoff.Id] = handoff;

        if (handoff.Status == HandoffStatus.Waiting)
        {
            await SlackAskAssigneeAsync(handoff);
        }

        return handoff;
    }

    private Handoff? Save(string id, Func<Handoff, Handoff> change)
    {
        if (!_handoffs.ContainsKey(id))
        {
            return null;
        }

        var updated = change(_handoffs[id]) with { UpdatedAt = DateTime.UtcNow };
        _handoffs[id] = updated;
        return updated;
    }

    private async Task<bool> AcceptAsync(string id, string expertId)
    {
        var current = _handoffs.ContainsKey(id) ? _handoffs[id] : null;

        if (current is not { Status: HandoffStatus.Waiting } || current.AssigneeId != expertId)
        {
            return false;
        }

        var handoff = Save(id, h => h with { Status = HandoffStatus.Accepted })!;
        await SlackConnectAsync(handoff);
        return true;
    }

    // Passes the handoff to the next candidate, or marks it unassigned when nobody is left
    private async Task<bool> DeclineAsync(string id, string expertId)
    {
        var current = _handoffs.ContainsKey(id) ? _handoffs[id] : null;

        if (current is not { Status: HandoffStatus.Waiting } || current.AssigneeId != expertId)
        {
            return false;
        }

        var declinedBy = FindExpert(expertId);
        var next = current.Index + 1;
        var handoff = Save(id, h => h with
        {
            Index = next,
            Status = next < h.CandidateIds.Length ? HandoffStatus.Waiting : HandoffStatus.Unassigned,
        })!;

        if (handoff.Status == HandoffStatus.Waiting)
        {
            var nextExpert = FindExpert(handoff.AssigneeId);
            await SlackTellRequesterAsync(handoff, $"{declinedBy?.Name ?? "They"} can't take *{handoff.Summary}*, so I've passed it to *{nextExpert?.Name}* ({nextExpert?.Title}).");
            await SlackAskAssigneeAsync(handoff);
        }
        else
        {
            await SlackTellRequesterAsync(handoff, $"Nobody on my list could take *{handoff.Summary}*. Try rephrasing it with more detail, or ask in a team channel.");
        }

        return true;
    }

    private bool MarkDone(string id)
    {
        if (!_handoffs.ContainsKey(id) || _handoffs[id].Status != HandoffStatus.Accepted)
        {
            return false;
        }

        Save(id, h => h with { Status = HandoffStatus.Done });
        return true;
    }

    private bool Cancel(string id)
    {
        if (!_handoffs.ContainsKey(id) || !_handoffs[id].IsOpen)
        {
            return false;
        }

        Save(id, h => h with { Status = HandoffStatus.Cancelled });
        return true;
    }

    private static readonly Expert[] DemoExperts =
    [
        Demo("demo-1", "Sofia Lindqvist", "Senior Backend Engineer", "Engineering", ["Payments API", "Stripe", "PostgreSQL", "Webhooks", "On-call"], "Ask me about billing code, refunds and anything that touches the database."),
        Demo("demo-2", "Jonas Berg", "Head of People", "People & HR", ["Parental leave", "Onboarding", "Contracts", "Benefits", "Recruiting"], "Policies, contracts and the awkward conversations."),
        Demo("demo-3", "Amira Haddad", "Product Designer", "Design", ["Figma", "Design system", "User research", "Accessibility"], "Happy to review flows, components and anything that needs to look right."),
        Demo("demo-4", "Mikael Nyström", "Financial Controller", "Finance", ["Invoicing", "Expense reports", "Budgeting", "VAT", "Fortnox"], "Invoices, budgets and how to get an expense approved."),
        Demo("demo-5", "Elin Svensson", "Account Executive", "Sales", ["Enterprise deals", "Pricing", "HubSpot", "RFPs", "Negotiation"], "Pricing questions, deal desk and customer contracts."),
        Demo("demo-6", "David Okafor", "IT & Security Lead", "IT", ["SSO", "Laptops", "Google Workspace", "GDPR", "Security reviews"], "Access, devices, security questionnaires and data protection."),
        Demo("demo-7", "Hanna Virtanen", "Customer Success Manager", "Customer Success", ["Renewals", "Onboarding customers", "Zendesk", "Escalations"], "When a customer is unhappy or needs a hand getting started."),
        Demo("demo-8", "Lucas Moreau", "Data Analyst", "Data", ["SQL", "Looker", "Dashboards", "Forecasting", "Python"], "Numbers, dashboards and 'can we measure this?'"),
    ];

    private static Expert Demo(string id, string name, string title, string department, string[] skills, string about) =>
        new(id, name, title, department, skills, about, null, null, true, true, DateTime.UtcNow);
}
