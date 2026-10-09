namespace InPolsure.SharedKernel;

/// <summary>
/// Provides the current point in time. Code that needs "now" depends on this
/// abstraction instead of <see cref="DateTimeOffset.UtcNow"/>, so tests can control time.
/// </summary>
public interface IClock
{
    /// <summary>Gets the current date and time in UTC.</summary>
    DateTimeOffset UtcNow { get; }
}
