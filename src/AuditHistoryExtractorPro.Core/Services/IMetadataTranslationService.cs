using Microsoft.Xrm.Sdk.Metadata;

namespace AuditHistoryExtractorPro.Core.Services;

public interface IMetadataTranslationService
{
    void CacheEntityMetadata(string entityLogicalName, EntityMetadata entityMetadata);
    string TranslateValue(string entityLogicalName, string attributeLogicalName, string rawValue);
}
