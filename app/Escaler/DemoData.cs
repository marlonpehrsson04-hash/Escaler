// A realistic example company so the app looks like it does in use: colleagues across every team, two weeks of
// requests in every state, a few manager priorities and two partners. Everything carries a "demo-" id, so
// "Remove demo data" clears it in one go (with undo) and it never comes back after that.
public partial class EscalerApp
{
    private sealed record DemoBackup(Expert[] Experts, Handoff[] Handoffs, PriorityTask[] Priorities, Handoff[] Reassigned);

    private readonly Reactive<DemoBackup?> _removedDemo = new(null);

    private static bool IsDemoId(string id) => id.StartsWith("demo-", StringComparison.Ordinal);

    // Tops up a directory that still shows demo people with any demo entries it lacks. Once the demo people are
    // removed there are none left, so nothing is added back; an empty directory gets the whole example company.
    private void SeedDemoData()
    {
        var demosInUse = _experts.Count == 0 || _experts.Values.Any(e => e.IsDemo);

        if (demosInUse)
        {
            foreach (var person in DemoExperts.Concat(DemoPartners).Where(p => !_experts.ContainsKey(p.Id)))
            {
                _experts[person.Id] = person;
            }

            foreach (var handoff in DemoHandoffs().Where(h => !_handoffs.ContainsKey(h.Id)))
            {
                _handoffs[handoff.Id] = handoff;
            }

            foreach (var priority in DemoPriorities().Where(p => !_priorities.ContainsKey(p.Id)))
            {
                _priorities[priority.Id] = priority;
            }
        }
    }

    private int DemoCount() => _experts.Values.Count(e => e.IsDemo);

    private void RemoveDemoData()
    {
        var experts = _experts.Values.Where(e => e.IsDemo).ToArray();
        var handoffs = _handoffs.Values.Where(h => IsDemoId(h.Id)).ToArray();
        var priorities = _priorities.Values.Where(p => IsDemoId(p.Id)).ToArray();

        // Real requests still waiting on a demo person would be stuck, so they become "No match" (undo puts them back)
        var reassigned = _handoffs.Values.Where(h => !IsDemoId(h.Id) && h.IsOpen && h.AssigneeId is { } a && IsDemoId(a)).ToArray();

        foreach (var expert in experts)
        {
            _experts.Remove(expert.Id);
        }

        foreach (var handoff in handoffs)
        {
            _handoffs.Remove(handoff.Id);
        }

        foreach (var priority in priorities)
        {
            _priorities.Remove(priority.Id);
        }

        foreach (var handoff in reassigned)
        {
            _handoffs[handoff.Id] = handoff with { Status = HandoffStatus.Unassigned, UpdatedAt = DateTime.UtcNow };
        }

        _removedDemo.Value = new DemoBackup(experts, handoffs, priorities, reassigned);
    }

    private void RestoreDemoData()
    {
        if (_removedDemo.Value is not { } backup)
        {
            return;
        }

        foreach (var expert in backup.Experts)
        {
            _experts[expert.Id] = expert;
        }

        foreach (var handoff in backup.Handoffs.Concat(backup.Reassigned))
        {
            _handoffs[handoff.Id] = handoff;
        }

        foreach (var priority in backup.Priorities)
        {
            _priorities[priority.Id] = priority;
        }

        _removedDemo.Value = null;
    }

    private static readonly Expert[] DemoExperts =
    [
        Demo("demo-1", "Sofia Lindqvist", "Senior Backend Engineer", "Engineering", ["Payments API", "Stripe", "PostgreSQL", "Webhooks", "On-call"], "Ask me about billing code, refunds and anything that touches the database."),
        Demo("demo-2", "Jonas Berg", "Head of People", "People & HR", ["Parental leave", "Onboarding", "Contracts", "Benefits", "Recruiting"], "Policies, contracts and the awkward conversations.", manager: true),
        Demo("demo-3", "Amira Haddad", "Product Designer", "Design", ["Figma", "Design system", "User research", "Accessibility"], "Happy to review flows, components and anything that needs to look right."),
        Demo("demo-4", "Mikael Nyström", "Financial Controller", "Finance", ["Invoicing", "Expense reports", "Budgeting", "VAT", "Fortnox"], "Invoices, budgets and how to get an expense approved."),
        Demo("demo-5", "Elin Svensson", "Account Executive", "Sales", ["Enterprise deals", "Pricing", "HubSpot", "RFPs", "Negotiation"], "Pricing questions, deal desk and customer contracts."),
        Demo("demo-6", "David Okafor", "IT & Security Lead", "IT", ["SSO", "Laptops", "Google Workspace", "GDPR", "Security reviews"], "Access, devices, security questionnaires and data protection."),
        Demo("demo-7", "Hanna Virtanen", "Customer Success Manager", "Customer Success", ["Renewals", "Onboarding customers", "Zendesk", "Escalations"], "When a customer is unhappy or needs a hand getting started."),
        Demo("demo-8", "Lucas Moreau", "Data Analyst", "Data", ["SQL", "Looker", "Dashboards", "Forecasting", "Python"], "Numbers, dashboards and 'can we measure this?'"),
        Demo("demo-9", "Karin Holm", "CTO", "Leadership", ["Architecture", "Tech strategy", "Hiring engineers", "Incident leadership"], "Big technical decisions, and anything that needs an engineering yes or no.", manager: true),
        Demo("demo-10", "Oskar Nilsson", "Frontend Engineer", "Engineering", ["React", "TypeScript", "Web performance", "Accessibility testing"], "The web app, page speed and anything in the browser."),
        Demo("demo-11", "Priya Raman", "DevOps Engineer", "Engineering", ["Kubernetes", "AWS", "Terraform", "CI/CD", "Docker"], "Deploys, environments and why the pipeline is red."),
        Demo("demo-12", "Emil Johansson", "Mobile Engineer", "Engineering", ["iOS", "Swift", "Android", "App Store releases"], "On parental leave until January. Ask Oskar about the apps meanwhile.", available: false),
        Demo("demo-13", "Fatima Al-Sayed", "QA Lead", "Engineering", ["Test automation", "Playwright", "Release testing", "Bug triage"], "Test plans, flaky tests and 'is this a bug?'"),
        Demo("demo-14", "Johan Lund", "Product Manager", "Product", ["Roadmap", "Customer interviews", "Prioritisation", "Jira"], "What's coming next and why, and where a feature request should go.", manager: true),
        Demo("demo-15", "Maja Ek", "Growth Product Manager", "Product", ["Onboarding funnel", "A/B tests", "Activation", "Pricing experiments"], "Sign-ups, activation and experiments."),
        Demo("demo-16", "Noah Andersson", "Brand Designer", "Design", ["Brand guidelines", "Illustration", "Presentation decks", "Adobe Creative Cloud"], "Logos, decks and making things look on-brand.", available: false),
        Demo("demo-17", "Sara Karlsson", "Head of Sales", "Sales", ["Sales forecasting", "Enterprise accounts", "Pipeline reviews", "Discount approvals"], "Big deals, forecasts and discounts above 10%.", manager: true),
        Demo("demo-18", "Ali Hassan", "Sales Development Rep", "Sales", ["Prospecting", "LinkedIn Sales Navigator", "Cold outreach", "Demo booking"], "Finding leads and booking first meetings."),
        Demo("demo-19", "Linnea Persson", "Content Marketing Manager", "Marketing", ["Blog", "SEO", "Webinars", "Case studies"], "Stories, case studies and getting the word out."),
        Demo("demo-20", "Viktor Berglund", "Performance Marketer", "Marketing", ["Google Ads", "LinkedIn Ads", "Attribution", "Budget pacing"], "Paid campaigns and where the leads come from."),
        Demo("demo-21", "Ida Lehtonen", "Support Engineer", "Customer Success", ["API troubleshooting", "Integrations", "Bug reproduction", "Zendesk"], "Technical customer questions and reproducing their bugs."),
        Demo("demo-22", "Anna Wikström", "Payroll & Accounting", "Finance", ["Payroll", "Salary questions", "Commissions", "Year-end closing"], "Salaries, commissions and accounts payable."),
        Demo("demo-23", "Tobias Sandberg", "HR Business Partner", "People & HR", ["Performance reviews", "Conflict resolution", "Sick leave", "Compensation"], "Reviews, tricky situations and anything you'd rather discuss in private."),
        Demo("demo-24", "Rebecka Strand", "Legal Counsel", "Legal", ["Contracts", "NDAs", "GDPR", "Data processing agreements", "Terms of service"], "Contracts, NDAs and 'are we allowed to do this?'"),
        Demo("demo-25", "Marcus Dahl", "Office & Operations Manager", "Operations", ["Office", "Travel booking", "Equipment", "Events", "Suppliers"], "The office, travel, equipment and company events."),
        Demo("demo-26", "Leo Fischer", "IT Support Specialist", "IT", ["Laptop setup", "Password resets", "Okta", "VPN", "Printers"], "Logins, laptops, VPN and the printer that never works."),
    ];

    private static readonly Expert[] DemoPartners =
    [
        new("demo-p1", "Karin Ek", "External partner", "External", ["Translation", "Localization", "Japanese", "Korean", "German", "Product documentation"],
            "Translated the product docs for the German launch. Quick and careful.", "karin@linguanordic.example", null, true, true, DateTime.UtcNow,
            IsExternal: true, Company: "Lingua Nordic AB"),
        new("demo-p2", "Erik Wahl", "External partner", "External", ["Penetration testing", "ISO 27001", "Security questionnaires", "SOC 2 readiness"],
            "Ran the 2025 security audit. Good with bank and insurance customers.", "erik@northbound.example", null, true, true, DateTime.UtcNow,
            IsExternal: true, Company: "Northbound Security"),
    ];

    private static Expert Demo(string id, string name, string title, string department, string[] skills, string about, bool available = true, bool manager = false) =>
        new(id, name, title, department, skills, about, null, null, available, true, DateTime.UtcNow, IsManager: manager);

    // Two weeks of requests in every state, timed relative to when the demo is seeded
    private static IEnumerable<Handoff> DemoHandoffs()
    {
        var now = DateTime.UtcNow;

        Handoff Make(string id, string requester, string requesterName, string[] candidates, string[] reasons, int index, HandoffStatus status,
            string summary, string problem, string urgency, string source, double createdHoursAgo, double updatedHoursAgo) =>
            new(id, summary, problem, urgency, requesterName, requester, null, candidates, reasons, index, status, source,
                now.AddHours(-createdHoursAgo), now.AddHours(-updatedHoursAgo));

        yield return Make("demo-h1", "demo-18", "Ali Hassan", ["demo-5"],
            ["Elin runs the deal desk and handles pricing and enterprise discounts."],
            0, HandoffStatus.Done, "Approve a 15% discount on a 200-seat deal",
            "The prospect wants 15% off for 200 seats on a two-year contract. Can we do that, and who signs off?", "Normal", "Slack", 216, 210);

        yield return Make("demo-h2", "demo-15", "Maja Ek", ["demo-8"],
            ["Lucas builds the Looker dashboards and owns funnel data."],
            0, HandoffStatus.Done, "Build a dashboard for onboarding drop-off",
            "We lose a lot of new sign-ups between step 2 and 3 of onboarding. I need a dashboard that shows where and for which plans.", "Normal", "Web", 170, 120);

        yield return Make("demo-h3", "demo-19", "Linnea Persson", ["demo-16", "demo-3"],
            ["Noah owns brand guidelines and presentation design.", "Amira designs in Figma and knows the design system well."],
            1, HandoffStatus.Done, "Design a cover for the Q4 customer report",
            "Need a cover and three chart pages for the Q4 customer report, on brand, by Thursday.", "Normal", "Slack", 150, 100);

        yield return Make("demo-h4", "demo-7", "Hanna Virtanen", ["demo-24"],
            ["Rebecka reviews contracts and data processing agreements."],
            0, HandoffStatus.Done, "Review a customer's data processing agreement",
            "A customer sent their own DPA and wants it signed before renewal. Is anything in it a problem for us?", "Normal", "Web", 120, 96);

        yield return Make("demo-h5", "demo-10", "Oskar Nilsson", ["demo-11"],
            ["Priya runs the Kubernetes clusters and the deploy pipeline."],
            0, HandoffStatus.Done, "Fix failing staging deploys on the new cluster",
            "Every deploy to staging fails since we moved to the new Kubernetes cluster. Pods crash on start with a config error.", "High", "Slack", 96, 92);

        yield return Make("demo-h6", "demo-17", "Sara Karlsson", ["demo-22"],
            ["Anna handles payroll and sales commissions."],
            0, HandoffStatus.Accepted, "Check October commission payouts",
            "Two reps say their October commission is too low. Can we check the calculation before payday?", "High", "Slack", 46, 44);

        yield return Make("demo-h7", "demo-14", "Johan Lund", ["demo-13"],
            ["Fatima leads QA and plans release testing."],
            0, HandoffStatus.Accepted, "Plan regression testing for the 3.0 release",
            "Release 3.0 goes out in two weeks. I need a test plan and someone to own the regression run.", "Normal", "Web", 26, 20);

        yield return Make("demo-h8", "demo-20", "Viktor Berglund", ["demo-4"],
            ["Mikael owns budgeting and can move budget between quarters."],
            0, HandoffStatus.Waiting, "Move 40k SEK of ad budget from Q4 to Q1",
            "We're underspending on ads this quarter. Can 40k SEK move to Q1 instead of being lost?", "Normal", "Slack", 3, 3);

        yield return Make("demo-h9", "demo-21", "Ida Lehtonen", ["demo-1", "demo-11"],
            ["Sofia built the webhooks and the payments API.", "Priya runs the infrastructure behind the API and can check timeouts."],
            1, HandoffStatus.Waiting, "Customer webhooks time out after the API change",
            "Since yesterday's API release, a big customer's webhooks time out after 10 seconds. They're escalating.", "High", "Slack", 5, 2);

        yield return Make("demo-h10", "demo-23", "Tobias Sandberg", ["demo-2"],
            ["Jonas handles leave policies and contracts."],
            0, HandoffStatus.Accepted, "Plan a return to work after long sick leave",
            "An employee returns from four months of sick leave next week. We need a gradual return plan.", "Normal", "Web", 50, 47);

        yield return Make("demo-h11", "demo-25", "Marcus Dahl", [], [],
            0, HandoffStatus.Unassigned, "Find a supplier for standing desks",
            "The Malmö office needs 20 standing desks before the new team starts. Who knows a good supplier?", "Low", "Slack", 72, 72);

        yield return Make("demo-h12", "demo-18", "Ali Hassan", ["demo-26"],
            ["Leo handles Okta and password resets."],
            0, HandoffStatus.Cancelled, "Reset my Okta MFA",
            "Got a new phone and can't log in to Okta.", "High", "Slack", 190, 189);

        yield return Make("demo-h13", "demo-8", "Lucas Moreau", ["demo-26"],
            ["Leo looks after the VPN and laptop setup."],
            0, HandoffStatus.Waiting, "VPN drops every hour since the update",
            "Since last week's laptop update my VPN disconnects about once an hour. Others on the data team too.", "Normal", "Web", 1, 1);

        yield return Make("demo-h14", "demo-12", "Emil Johansson", ["demo-6"],
            ["David answers security questionnaires and owns GDPR questions."],
            0, HandoffStatus.Done, "Answer a bank's security questionnaire",
            "A bank customer sent a 120-question security questionnaire for the app. Due in a week.", "High", "Web", 240, 200);
    }

    private static IEnumerable<PriorityTask> DemoPriorities()
    {
        var now = DateTime.UtcNow;

        yield return new PriorityTask("demo-pr1", "Code freeze from Friday for the 3.0 release",
            "Only bug fixes merge after Friday 17:00. Anything else waits for 3.1. Ask Fatima if you're unsure whether something counts.",
            "Critical", ["Engineering"], "demo-9", "Karin Holm", now.AddHours(-20), now.Date.AddDays(3), false, []);

        yield return new PriorityTask("demo-pr2", "Lock in renewals before year-end",
            "Every renewal due in Q1 should have a signed quote by 20 December. Flag at-risk accounts in the pipeline review.",
            "High", ["Sales", "Customer Success"], "demo-17", "Sara Karlsson", now.AddHours(-30), now.Date.AddDays(30), false, []);

        yield return new PriorityTask("demo-pr3", "Book your performance review",
            "Reviews run over the next two weeks. Book 45 minutes with your manager and fill in the self-review first.",
            "Normal", [], "demo-2", "Jonas Berg", now.AddHours(-50), now.Date.AddDays(14), false, []);
    }
}
