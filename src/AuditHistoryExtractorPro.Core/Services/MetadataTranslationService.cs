using Microsoft.Xrm.Sdk.Metadata;

namespace AuditHistoryExtractorPro.Core.Services;

public sealed class MetadataTranslationService : IMetadataTranslationService
{
    private readonly Dictionary<string, Dictionary<string, AttributeTranslationMetadata>> _cache =
        new(StringComparer.OrdinalIgnoreCase);

    public void CacheEntityMetadata(string entityLogicalName, EntityMetadata entityMetadata)
    {
        if (string.IsNullOrWhiteSpace(entityLogicalName))
        {
            return;
        }

        var attributeMap = new Dictionary<string, AttributeTranslationMetadata>(StringComparer.OrdinalIgnoreCase);

        foreach (var attribute in entityMetadata.Attributes)
        {
            if (string.IsNullOrWhiteSpace(attribute.LogicalName))
            {
                continue;
            }

            if (attribute is BooleanAttributeMetadata booleanAttribute)
            {
                var trueLabel = booleanAttribute.OptionSet?.TrueOption?.Label?.UserLocalizedLabel?.Label
                    ?? booleanAttribute.OptionSet?.TrueOption?.Label?.LocalizedLabels?.FirstOrDefault()?.Label
                    ?? "Sí";

                var falseLabel = booleanAttribute.OptionSet?.FalseOption?.Label?.UserLocalizedLabel?.Label
                    ?? booleanAttribute.OptionSet?.FalseOption?.Label?.LocalizedLabels?.FirstOrDefault()?.Label
                    ?? "No";

                attributeMap[attribute.LogicalName] = AttributeTranslationMetadata.ForBoolean(trueLabel, falseLabel);
                continue;
            }

            if (attribute is not EnumAttributeMetadata enumAttribute)
            {
                continue;
            }

            var options = enumAttribute.OptionSet?.Options
                ?.Where(o => o.Value.HasValue)
                .ToDictionary(
                    o => o.Value!.Value,
                    o => o.Label?.UserLocalizedLabel?.Label
                        ?? o.Label?.LocalizedLabels?.FirstOrDefault()?.Label
                        ?? o.Value!.Value.ToString())
                ?? new Dictionary<int, string>();

            if (options.Count > 0)
            {
                attributeMap[attribute.LogicalName] = AttributeTranslationMetadata.ForOptions(options);
            }
        }

        _cache[entityLogicalName] = attributeMap;
    }

    public string TranslateValue(string entityLogicalName, string attributeLogicalName, string rawValue)
    {
        if (string.IsNullOrWhiteSpace(entityLogicalName)
            || string.IsNullOrWhiteSpace(attributeLogicalName)
            || string.IsNullOrWhiteSpace(rawValue))
        {
            return rawValue;
        }

        if (!_cache.TryGetValue(entityLogicalName, out var attributes)
            || !attributes.TryGetValue(attributeLogicalName, out var metadata))
        {
            return rawValue;
        }

        if (metadata.IsBoolean)
        {
            if (bool.TryParse(rawValue, out var boolValue))
            {
                return boolValue ? metadata.TrueLabel : metadata.FalseLabel;
            }

            if (int.TryParse(rawValue, out var numericBool))
            {
                return numericBool != 0 ? metadata.TrueLabel : metadata.FalseLabel;
            }

            return rawValue;
        }

        if (int.TryParse(rawValue, out var numericValue)
            && metadata.OptionLabels.TryGetValue(numericValue, out var label))
        {
            return label;
        }

        return rawValue;
    }

    private sealed class AttributeTranslationMetadata
    {
        public bool IsBoolean { get; private init; }
        public string TrueLabel { get; private init; } = "Sí";
        public string FalseLabel { get; private init; } = "No";
        public Dictionary<int, string> OptionLabels { get; private init; } = new();

        public static AttributeTranslationMetadata ForOptions(Dictionary<int, string> labels)
        {
            return new AttributeTranslationMetadata
            {
                IsBoolean = false,
                OptionLabels = labels
            };
        }

        public static AttributeTranslationMetadata ForBoolean(string trueLabel, string falseLabel)
        {
            return new AttributeTranslationMetadata
            {
                IsBoolean = true,
                TrueLabel = trueLabel,
                FalseLabel = falseLabel
            };
        }
    }
}
