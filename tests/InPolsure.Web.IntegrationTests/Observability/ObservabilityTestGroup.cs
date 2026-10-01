namespace InPolsure.Web.IntegrationTests.Observability;

/// <summary>
/// Runs the observability tests without any other test running in parallel, because
/// <c>ActivitySource</c> listeners are process-wide (see <see cref="ObservabilityTests"/>).
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ObservabilityTestGroup
{
    public const string Name = "Observability";
}
