using AuditHistoryExtractorPro.Domain.ValueObjects;
using FluentAssertions;
using Xunit;

namespace AuditHistoryExtractorPro.Domain.Tests.ValueObjects;

public class ExtractionCriteriaTests
{
    [Fact]
    public void Validate_ShouldThrowException_WhenNoEntitiesSpecified()
    {
        // Arrange
        var criteria = new ExtractionCriteria
        {
            EntityNames = new List<string>()
        };

        // Act
        Action act = () => criteria.Validate();

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("At least one entity name must be specified");
    }

    [Fact]
    public void Validate_ShouldThrowException_WhenFromDateIsGreaterThanToDate()
    {
        // Arrange
        var criteria = new ExtractionCriteria
        {
            EntityNames = new List<string> { "account" },
            FromDate = new DateTime(2024, 12, 31),
            ToDate = new DateTime(2024, 1, 1)
        };

        // Act
        Action act = () => criteria.Validate();

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("FromDate cannot be greater than ToDate");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(10001)]
    public void Validate_ShouldThrowException_WhenPageSizeIsInvalid(int pageSize)
    {
        // Arrange
        var criteria = new ExtractionCriteria
        {
            EntityNames = new List<string> { "account" },
            PageSize = pageSize
        };

        // Act
        Action act = () => criteria.Validate();

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("PageSize must be between 1 and 10000");
    }

    [Fact]
    public void Validate_ShouldNotThrow_WhenCriteriaIsValid()
    {
        // Arrange
        var criteria = new ExtractionCriteria
        {
            EntityNames = new List<string> { "account", "contact" },
            FromDate = new DateTime(2024, 1, 1),
            ToDate = new DateTime(2024, 12, 31),
            PageSize = 5000
        };

        // Act
        Action act = () => criteria.Validate();

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void ExtractionCriteria_ShouldSetDefaultPageSize()
    {
        // Arrange & Act
        var criteria = new ExtractionCriteria
        {
            EntityNames = new List<string> { "account" }
        };

        // Assert
        criteria.PageSize.Should().Be(5000);
    }

    [Fact]
    public void ExtractionCriteria_ShouldSetDefaultMaxParallelRequests()
    {
        // Arrange & Act
        var criteria = new ExtractionCriteria
        {
            EntityNames = new List<string> { "account" }
        };

        // Assert
        criteria.MaxParallelRequests.Should().Be(10);
    }
}

public class AuthenticationConfigurationTests
{
    [Fact]
    public void Validate_ShouldThrowException_WhenEnvironmentUrlIsEmpty()
    {
        // Arrange
        var config = new AuthenticationConfiguration
        {
            EnvironmentUrl = "",
            Type = AuthenticationType.OAuth2
        };

        // Act
        Action act = () => config.Validate();

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("EnvironmentUrl is required");
    }

    [Fact]
    public void Validate_ShouldThrowException_ForOAuth2WithoutRequiredFields()
    {
        // Arrange
        var config = new AuthenticationConfiguration
        {
            EnvironmentUrl = "https://test.crm.dynamics.com",
            Type = AuthenticationType.OAuth2,
            TenantId = null,
            ClientId = null
        };

        // Act
        Action act = () => config.Validate();

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("TenantId and ClientId are required for OAuth2");
    }

    [Fact]
    public void Validate_ShouldThrowException_ForClientSecretWithoutRequiredFields()
    {
        // Arrange
        var config = new AuthenticationConfiguration
        {
            EnvironmentUrl = "https://test.crm.dynamics.com",
            Type = AuthenticationType.ClientSecret,
            ClientId = "test-client-id",
            ClientSecret = null
        };

        // Act
        Action act = () => config.Validate();

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("ClientId and ClientSecret are required");
    }

    [Fact]
    public void Validate_ShouldThrowException_ForCertificateWithoutCertificateInfo()
    {
        // Arrange
        var config = new AuthenticationConfiguration
        {
            EnvironmentUrl = "https://test.crm.dynamics.com",
            Type = AuthenticationType.Certificate,
            CertificateThumbprint = null,
            CertificatePath = null
        };

        // Act
        Action act = () => config.Validate();

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("Certificate thumbprint or path is required");
    }

    [Fact]
    public void Validate_ShouldNotThrow_ForValidOAuth2Configuration()
    {
        // Arrange
        var config = new AuthenticationConfiguration
        {
            EnvironmentUrl = "https://test.crm.dynamics.com",
            Type = AuthenticationType.OAuth2,
            TenantId = "tenant-id",
            ClientId = "client-id"
        };

        // Act
        Action act = () => config.Validate();

        // Assert
        act.Should().NotThrow();
    }
}

public class ExportConfigurationTests
{
    [Fact]
    public void ExportConfiguration_ShouldSetDefaultValues()
    {
        // Arrange & Act
        var config = new ExportConfiguration();

        // Assert
        config.Format.Should().Be(ExportFormat.Excel);
        config.OutputPath.Should().Be("./exports");
        config.FileName.Should().Be("audit_export");
        config.IncludeTimestamp.Should().BeTrue();
        config.BatchSize.Should().Be(10000);
    }

    [Fact]
    public void ExportConfiguration_ShouldAllowCustomValues()
    {
        // Arrange & Act
        var config = new ExportConfiguration
        {
            Format = ExportFormat.Csv,
            OutputPath = "./custom/path",
            FileName = "custom_export",
            CompressOutput = true,
            IncludeTimestamp = false,
            BatchSize = 5000
        };

        // Assert
        config.Format.Should().Be(ExportFormat.Csv);
        config.OutputPath.Should().Be("./custom/path");
        config.FileName.Should().Be("custom_export");
        config.CompressOutput.Should().BeTrue();
        config.IncludeTimestamp.Should().BeFalse();
        config.BatchSize.Should().Be(5000);
    }
}

public class ExtractionResultTests
{
    [Fact]
    public void ExtractionResult_ShouldCalculateDuration()
    {
        // Arrange
        var result = new ExtractionResult
        {
            StartTime = new DateTime(2024, 1, 1, 10, 0, 0),
            EndTime = new DateTime(2024, 1, 1, 10, 5, 30)
        };

        // Act
        var duration = result.Duration;

        // Assert
        duration.TotalMinutes.Should().BeApproximately(5.5, 0.01);
        duration.TotalSeconds.Should().Be(330);
    }

    [Fact]
    public void GetSummary_ShouldReturnFormattedString()
    {
        // Arrange
        var result = new ExtractionResult
        {
            Success = true,
            RecordsExtracted = 1000,
            RecordsFailed = 5,
            StartTime = DateTime.Now,
            EndTime = DateTime.Now.AddMinutes(2)
        };

        // Act
        var summary = result.GetSummary();

        // Assert
        summary.Should().Contain("1000 records");
        summary.Should().Contain("Failed: 5");
        summary.Should().Contain("Success: True");
    }

    [Fact]
    public void ExtractionResult_ShouldInitializeCollections()
    {
        // Arrange & Act
        var result = new ExtractionResult();

        // Assert
        result.Errors.Should().NotBeNull();
        result.Errors.Should().BeEmpty();
        result.Warnings.Should().NotBeNull();
        result.Warnings.Should().BeEmpty();
        result.Metadata.Should().NotBeNull();
        result.Metadata.Should().BeEmpty();
    }
}
