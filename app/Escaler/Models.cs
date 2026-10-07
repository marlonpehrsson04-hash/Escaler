// A person in the expert directory. Id is the Ikon user id, or "demo-…" for seeded examples
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
    DateTime JoinedAt);

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
