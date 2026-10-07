// A person in the expert directory. Id is the Ikon user id, "slack-…" for a draft imported from Slack,
// "ext-…" for an external partner, or "demo-…" for seeded examples.
// The fields after JoinedAt were added later; each defaults to false or null so older saved profiles load unchanged.
public record Expert(
    string Id,
    string Name,
    string Title,
    string Department,
    string[] Skills,
    string About,
    string? Email,
    string? SlackUserId,
    bool Available,
    bool IsDemo,
    DateTime JoinedAt,
    bool IsDraft = false,
    bool IsExternal = false,
    string? Company = null,
    string? Website = null,
    bool Invited = false,
    bool IsManager = false)
{
    // Colleagues the router may pick: not outside partners, and with something to match on
    [JsonIgnore]
    public bool IsRoutable => !IsExternal && Available && Skills.Length > 0;
}

public enum HandoffStatus
{
    Waiting,
    Accepted,
    Done,
    Unassigned,
    Cancelled,
}

// One problem on its way to the right person. CandidateIds is the ranked list; Index points at whoever holds it now
public record Handoff(
    string Id,
    string Summary,
    string Problem,
    string Urgency,
    string RequesterName,
    string? RequesterExpertId,
    string? RequesterSlackId,
    string[] CandidateIds,
    string[] Reasons,
    int Index,
    HandoffStatus Status,
    string Source,
    DateTime CreatedAt,
    DateTime UpdatedAt)
{
    [JsonIgnore]
    public string? AssigneeId => Index < CandidateIds.Length ? CandidateIds[Index] : null;

    [JsonIgnore]
    public string? AssigneeReason => Index < Reasons.Length ? Reasons[Index] : null;

    [JsonIgnore]
    public bool IsOpen => Status is HandoffStatus.Waiting or HandoffStatus.Accepted;
}

public record RouteMatch(string ExpertId, int Score, string Reason);

public record RoutePlan(string Summary, string Urgency, bool NeedsClarification, string ClarifyingQuestion, List<RouteMatch> Matches);

public record SkillIdeas(List<string> Skills);

// What the Ask screen shows between "find" and "send"
public record RoutePreview(string Problem, RoutePlan Plan);

public record PendingClarification(string Text, DateTime At);

// One imported person's guessed team and skills, from their Slack title
public record ImportGuess(string SlackUserId, string Department, List<string> Skills);

public record ImportGuesses(List<ImportGuess> People);

// What the AI read out of a LinkedIn profile PDF the person uploaded themselves
public record LinkedInExtract(string Title, string About, List<string> Skills);

// A firm or freelancer found on the public web for a problem no colleague fits
public record OutsideOption(string Name, string Url, string Kind, string Why);

public record OutsideSearch(List<OutsideOption> Options);

// A web search query written to find specialists without leaking names or company details
public record OutsideQuery(string Query);

// Outside help shown under a weak or empty internal match: partner matches, then web results on request
public record OutsideHelp(string Problem, string Summary, List<RouteMatch> Partners, List<OutsideOption> Web, bool WebSearched);

// A priority a manager posts for some teams (or everyone). Teams empty = the whole company.
// SeenBy holds the expert ids that pressed "Got it".
public record PriorityTask(
    string Id,
    string Title,
    string Details,
    string Level,
    string[] Teams,
    string CreatedById,
    string CreatedByName,
    DateTime CreatedAt,
    DateTime? DueAt,
    bool Done,
    string[] SeenBy);
