namespace Application.DTOs;
public sealed record TypeSummary(string EventType, int Count, long TotalQuantity);
public sealed record ZoneSummary(string Zone, int Count);
public sealed record AnalyticsSummary(int TotalEvents, long TotalQuantity,
    TypeSummary[] ByEventType, ZoneSummary[] ByZone, DateTime GeneratedAtUtc);
public sealed record AuthenticatedUser(int Id, string Username, string Role);
