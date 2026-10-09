namespace InPolsure.ArchitectureTests;

// Proves that each AC-14 rule reports a violation when one exists (synthetic input).
public sealed class DependencyRulesTests
{
    [Theory]
    [InlineData("InPolsure.Web")]
    [InlineData("InPolsure.Ui")]
    [InlineData("InPolsure.Tickets")]
    [InlineData("Microsoft.AspNetCore.Http.Abstractions")]
    [InlineData("Microsoft.AspNetCore.Components")]
    public void SharedKernelViolations_ForbiddenReference_IsReported(string forbidden)
    {
        var violations = DependencyRules.SharedKernelViolations(["System.Runtime", forbidden]);

        Assert.Equal([forbidden], violations);
    }

    [Fact]
    public void SharedKernelViolations_OnlyBclReferences_ReportsNothing()
    {
        var violations = DependencyRules.SharedKernelViolations(
            ["System.Runtime", "System.Collections", "Microsoft.Extensions.DependencyInjection.Abstractions"]);

        Assert.Empty(violations);
    }

    [Fact]
    public void UiViolations_ReferenceToWeb_IsReported()
    {
        var violations = DependencyRules.UiViolations(
            ["Microsoft.AspNetCore.Components", "InPolsure.SharedKernel", "InPolsure.Web"]);

        Assert.Equal(["InPolsure.Web"], violations);
    }

    [Fact]
    public void UiViolations_FrameworkAndSharedKernelReferences_ReportsNothing()
    {
        var violations = DependencyRules.UiViolations(
            ["Microsoft.AspNetCore.Components", "InPolsure.SharedKernel", "InPolsure.Web.Extensions.Unrelated"]);

        Assert.Empty(violations);
    }

    [Theory]
    [InlineData("InPolsure.UnitTests")]
    [InlineData("InPolsure.Web.IntegrationTests")]
    [InlineData("InPolsure.ArchitectureTests")]
    [InlineData("InPolsure.Web.UiTests")]
    [InlineData("InPolsure.Tickets.Tests")]
    [InlineData("xunit.v3.core")]
    [InlineData("xunit.v3.assert")]
    [InlineData("Bunit.Web")]
    [InlineData("Microsoft.NET.Test.Sdk")]
    [InlineData("Microsoft.TestPlatform.CoreUtilities")]
    [InlineData("Microsoft.Testing.Platform")]
    [InlineData("Microsoft.Playwright")]
    [InlineData("Deque.AxeCore.Playwright")]
    [InlineData("Microsoft.AspNetCore.Mvc.Testing")]
    [InlineData("Microsoft.AspNetCore.TestHost")]
    [InlineData("OpenTelemetry.Exporter.InMemory")]
    [InlineData("Microsoft.Extensions.TimeProvider.Testing")]
    [InlineData("Microsoft.Extensions.Diagnostics.Testing")]
    [InlineData("Testcontainers.MsSql")]
    [InlineData("NSubstitute")]
    [InlineData("Moq")]
    [InlineData("FluentAssertions")]
    [InlineData("AwesomeAssertions")]
    [InlineData("Shouldly")]
    [InlineData("AutoFixture")]
    public void TestReferenceViolations_TestAssemblyReference_IsReported(string forbidden)
    {
        var violations = DependencyRules.TestReferenceViolations(["System.Runtime", "InPolsure.SharedKernel", forbidden]);

        Assert.Equal([forbidden], violations);
    }

    [Fact]
    public void TestReferenceViolations_ProductionReferences_ReportsNothing()
    {
        var violations = DependencyRules.TestReferenceViolations(
            ["System.Runtime", "InPolsure.SharedKernel", "InPolsure.Ui", "Microsoft.AspNetCore.Components", "OpenTelemetry"]);

        Assert.Empty(violations);
    }

    [Fact]
    public void Rules_MultipleAndDuplicateViolations_AreReportedOnceInOrder()
    {
        var violations = DependencyRules.SharedKernelViolations(
            ["Microsoft.AspNetCore.Http", "InPolsure.Web", "InPolsure.Web"]);

        Assert.Equal(["InPolsure.Web", "Microsoft.AspNetCore.Http"], violations);
    }

    [Fact]
    public void FailureMessage_Violations_ListsAssemblyRuleAndOffendingReferences()
    {
        var message = DependencyRules.FailureMessage("InPolsure.Ui", "no reference to InPolsure.Web", ["InPolsure.Web"]);

        Assert.Equal(
            "InPolsure.Ui violates rule 'no reference to InPolsure.Web'. Offending references: InPolsure.Web.",
            message);
    }
}
