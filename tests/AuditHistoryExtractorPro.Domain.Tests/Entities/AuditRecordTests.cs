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

/// <summary>
/// Tests para el comportamiento de dominio agregado en AuditRecord.
/// Valida que las propiedades computadas y el método AddFieldChange funcionen correctamente.
/// </summary>
public class AuditRecordBehaviorTests
{
    [Theory]
    [InlineData("Create", true,  false, false)]
    [InlineData("create", true,  false, false)] // case-insensitive
    [InlineData("Update", false, true,  false)]
    [InlineData("DELETE", false, false, true)]  // case-insensitive
    [InlineData("",       false, false, false)] // sin operación
    public void OperationFlags_ShouldReflectOperationString(
        string operation,
        bool expectedCreate,
        bool expectedUpdate,
        bool expectedDelete)
    {
        var record = new AuditRecord { Operation = operation };

        record.IsCreateOperation.Should().Be(expectedCreate);
        record.IsUpdateOperation.Should().Be(expectedUpdate);
        record.IsDeleteOperation.Should().Be(expectedDelete);
    }

    [Fact]
    public void ChangedFieldCount_ShouldCountOnlyActuallyChangedFields()
    {
        var record = new AuditRecord();
        record.Changes["name"]   = new AuditFieldChange { FieldName = "name",   OldValue = "A", NewValue = "B" };
        record.Changes["status"] = new AuditFieldChange { FieldName = "status", OldValue = "X", NewValue = "X" }; // sin cambio
        record.Changes["email"]  = new AuditFieldChange { FieldName = "email",  OldValue = null, NewValue = "new@x.com" };

        record.ChangedFieldCount.Should().Be(2); // name + email
    }

    [Fact]
    public void HasFieldChange_ShouldReturnTrue_WhenFieldExists()
    {
        var record = new AuditRecord();
        record.Changes["name"] = new AuditFieldChange { FieldName = "name", OldValue = "A", NewValue = "B" };

        record.HasFieldChange("name").Should().BeTrue();
        record.HasFieldChange("nonexistent").Should().BeFalse();
    }

    [Fact]
    public void AddFieldChange_ShouldAddChangeToCollection()
    {
        var record = new AuditRecord();
        var change = new AuditFieldChange { FieldName = "telephone", OldValue = "111", NewValue = "222" };

        record.AddFieldChange(change);

        record.Changes.Should().ContainKey("telephone");
        record.Changes["telephone"].Should().Be(change);
    }

    [Fact]
    public void AddFieldChange_ShouldReplaceExistingChange_WhenSameFieldAdded()
    {
        var record = new AuditRecord();
        record.AddFieldChange(new AuditFieldChange { FieldName = "email", OldValue = "a@a.com", NewValue = "b@b.com" });
        var updated = new AuditFieldChange { FieldName = "email", OldValue = "a@a.com", NewValue = "c@c.com" };

        record.AddFieldChange(updated);

        record.Changes["email"].NewValue.Should().Be("c@c.com");
        record.Changes.Should().HaveCount(1);
    }

    [Fact]
    public void AddFieldChange_ShouldThrow_WhenChangeIsNull()
    {
        var record = new AuditRecord();
        Action act = () => record.AddFieldChange(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void AddFieldChange_ShouldThrow_WhenFieldNameIsEmpty()
    {
        var record = new AuditRecord();
        var change = new AuditFieldChange { FieldName = "", OldValue = "x", NewValue = "y" };
        Action act = () => record.AddFieldChange(change);
        act.Should().Throw<ArgumentException>().WithMessage("*FieldName*");
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
