using AuditHistoryExtractorPro.Domain.Entities;
using AuditHistoryExtractorPro.Domain.ValueObjects;
using FluentAssertions;
using Xunit;

namespace AuditHistoryExtractorPro.Domain.Tests.Entities;

public class AuditRecordTests
{
    [Fact]
    public void AuditRecord_ShouldInitializeWithDefaultValues()
    {
        // Arrange & Act
        var auditRecord = new AuditRecord();

        // Assert
        auditRecord.Changes.Should().NotBeNull();
        auditRecord.Changes.Should().BeEmpty();
        auditRecord.AdditionalData.Should().NotBeNull();
        auditRecord.AdditionalData.Should().BeEmpty();
    }

    [Fact]
    public void AuditRecord_ShouldStoreFieldChanges()
    {
        // Arrange
        var auditRecord = new AuditRecord
        {
            AuditId = Guid.NewGuid(),
            EntityName = "account",
            RecordId = Guid.NewGuid()
        };

        var fieldChange = new AuditFieldChange
        {
            FieldName = "name",
            OldValue = "Old Name",
            NewValue = "New Name",
            FieldType = "string"
        };

        // Act
        auditRecord.Changes["name"] = fieldChange;

        // Assert
        auditRecord.Changes.Should().ContainKey("name");
        auditRecord.Changes["name"].Should().Be(fieldChange);
    }
}

public class AuditFieldChangeTests
{
    [Fact]
    public void HasChanged_ShouldReturnTrue_WhenValuesAreDifferent()
    {
        // Arrange
        var fieldChange = new AuditFieldChange
        {
            FieldName = "status",
            OldValue = "Active",
            NewValue = "Inactive"
        };

        // Act & Assert
        fieldChange.HasChanged.Should().BeTrue();
    }

    [Fact]
    public void HasChanged_ShouldReturnFalse_WhenValuesAreSame()
    {
        // Arrange
        var fieldChange = new AuditFieldChange
        {
            FieldName = "status",
            OldValue = "Active",
            NewValue = "Active"
        };

        // Act & Assert
        fieldChange.HasChanged.Should().BeFalse();
    }

    [Theory]
    [InlineData(null, "New Value", "Set to 'New Value'")]
    [InlineData("Old Value", null, "Cleared from 'Old Value'")]
    [InlineData("Old", "New", "Changed from 'Old' to 'New'")]
    public void GetChangeDescription_ShouldReturnCorrectDescription(
        string? oldValue,
        string? newValue,
        string expectedDescription)
    {
        // Arrange
        var fieldChange = new AuditFieldChange
        {
            FieldName = "field",
            OldValue = oldValue,
            NewValue = newValue
        };

        // Act
        var description = fieldChange.GetChangeDescription();

        // Assert
        description.Should().Be(expectedDescription);
    }
}

public class RecordComparisonTests
{
    [Fact]
    public void RecordComparison_ShouldInitializeWithEmptyDifferences()
    {
        // Arrange & Act
        var comparison = new RecordComparison
        {
            RecordId = Guid.NewGuid(),
            EntityName = "account",
            CurrentVersion = new AuditRecord()
        };

        // Assert
        comparison.Differences.Should().NotBeNull();
        comparison.Differences.Should().BeEmpty();
    }

    [Fact]
    public void RecordComparison_ShouldStoreMultipleDifferences()
    {
        // Arrange
        var comparison = new RecordComparison
        {
            RecordId = Guid.NewGuid(),
            EntityName = "account",
            CurrentVersion = new AuditRecord()
        };

        var diff1 = new FieldDifference
        {
            FieldName = "name",
            OldValue = "Old Name",
            NewValue = "New Name",
            Type = DifferenceType.Modified
        };

        var diff2 = new FieldDifference
        {
            FieldName = "status",
            NewValue = "Active",
            Type = DifferenceType.Added
        };

        // Act
        comparison.Differences.Add(diff1);
        comparison.Differences.Add(diff2);

        // Assert
        comparison.Differences.Should().HaveCount(2);
        comparison.Differences.Should().Contain(diff1);
        comparison.Differences.Should().Contain(diff2);
    }
}

public class AuditStatisticsTests
{
    [Fact]
    public void AuditStatistics_ShouldCalculateTotalOperations()
    {
        // Arrange
        var statistics = new AuditStatistics
        {
            CreateOperations = 10,
            UpdateOperations = 25,
            DeleteOperations = 5
        };

        // Act
        var total = statistics.CreateOperations + statistics.UpdateOperations + statistics.DeleteOperations;

        // Assert
        total.Should().Be(40);
    }

    [Fact]
    public void AuditStatistics_ShouldStoreRecordsByEntity()
    {
        // Arrange
        var statistics = new AuditStatistics();

        // Act
        statistics.RecordsByEntity["account"] = 100;
        statistics.RecordsByEntity["contact"] = 150;
        statistics.RecordsByEntity["opportunity"] = 75;

        // Assert
        statistics.RecordsByEntity.Should().HaveCount(3);
        statistics.RecordsByEntity["account"].Should().Be(100);
        statistics.RecordsByEntity["contact"].Should().Be(150);
        statistics.RecordsByEntity["opportunity"].Should().Be(75);
    }

    [Fact]
    public void AuditStatistics_ShouldStoreMostChangedFields()
    {
        // Arrange
        var statistics = new AuditStatistics();
        var mostChanged = new List<string> { "name", "status", "owner" };

        // Act
        statistics.MostChangedFields = mostChanged;

        // Assert
        statistics.MostChangedFields.Should().HaveCount(3);
        statistics.MostChangedFields.Should().ContainInOrder("name", "status", "owner");
    }
}
