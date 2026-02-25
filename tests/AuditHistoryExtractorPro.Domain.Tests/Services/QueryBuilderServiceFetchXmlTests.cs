using AuditHistoryExtractorPro.Core.Models;
using AuditHistoryExtractorPro.Core.Services;
using FluentAssertions;
using System.Text.RegularExpressions;
using Xunit;

namespace AuditHistoryExtractorPro.Domain.Tests.Services;

public class QueryBuilderServiceFetchXmlTests
{
    [Fact]
    public void BuildBaseAuditQuery_ShouldGenerateSanitizedUtcFetchXml_WithoutDuplicateCreatedOnFilters()
    {
        // Arrange
        var service = new QueryBuilderService();
        var filters = new AuditQueryFilters
        {
            EntityName = "10142",
            SelectedDateRange = DateRangeFilter.Personalizado,
            StartDate = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Local),
            EndDate = new DateTime(2026, 2, 10, 23, 59, 0, DateTimeKind.Local),
            SelectedOperations = new[] { 1, 2, 2, 3 },
            SelectedActions = new[] { 64, 64, 65 },
            RecordId = string.Empty
        };

        // Act
        var fetchXml = service.BuildBaseAuditQuery(filters, pageNumber: 1, pageSize: 5000, pagingCookie: null);

        // Assert
        fetchXml.Should().Contain("no-lock='true'");
        fetchXml.Should().Contain("attribute='objecttypecode' operator='eq' value='10142'");

        // createdon solo una vez por operador (sin duplicados)
        Regex.Matches(fetchXml, "attribute='createdon' operator='on-or-after'", RegexOptions.IgnoreCase)
            .Count.Should().Be(1);
        Regex.Matches(fetchXml, "attribute='createdon' operator='on-or-before'", RegexOptions.IgnoreCase)
            .Count.Should().Be(1);

        // ISO 8601 UTC estricto (sin offset -05:00 ni milisegundos)
        fetchXml.Should().MatchRegex(@"value='\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}Z'");
        fetchXml.Should().NotContain("-05:00");
        fetchXml.Should().NotContain("+00:00");

        // sanitización: no se emiten condiciones con value vacío
        fetchXml.Should().NotContain("value='' ");
        fetchXml.Should().NotContain("value='' />");

        // operadores IN presentes y deduplicados
        Regex.Matches(fetchXml, "<condition attribute='operation' operator='in'>", RegexOptions.IgnoreCase)
            .Count.Should().Be(1);
        Regex.Matches(fetchXml, "<condition attribute='action' operator='in'>", RegexOptions.IgnoreCase)
            .Count.Should().Be(1);
    }
}
