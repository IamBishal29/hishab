using Hishab.Application.Common.Privacy;
using Serilog.Core;
using Serilog.Events;

namespace Hishab.Api.Logging;

/// <summary>
/// Safety net: masks any Bangladeshi mobile number that reaches a log property, even when
/// the developer forgot to call <see cref="PhoneMasker"/>. Exception messages and stack
/// traces are not rewritten, so domain exception messages must stay free of personal data.
/// </summary>
public sealed class PhoneRedactionEnricher : ILogEventEnricher
{
    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        foreach (var (name, value) in logEvent.Properties.ToList())
        {
            var redacted = Redact(value);
            if (!ReferenceEquals(redacted, value))
            {
                logEvent.AddOrUpdateProperty(new LogEventProperty(name, redacted));
            }
        }
    }

    /// <summary>Returns the same instance when nothing needed masking (no allocation on the hot path).</summary>
    private static LogEventPropertyValue Redact(LogEventPropertyValue value) => value switch
    {
        ScalarValue { Value: string s } when PhoneMasker.ContainsPhone(s) => new ScalarValue(PhoneMasker.MaskAll(s)),
        SequenceValue seq => RedactSequence(seq),
        StructureValue structure => RedactStructure(structure),
        DictionaryValue dictionary => RedactDictionary(dictionary),
        _ => value,
    };

    private static SequenceValue RedactSequence(SequenceValue seq)
    {
        var elements = seq.Elements.Select(Redact).ToList();
        return elements.SequenceEqual(seq.Elements) ? seq : new SequenceValue(elements);
    }

    private static StructureValue RedactStructure(StructureValue structure)
    {
        var properties = structure.Properties.Select(p => new { p, v = Redact(p.Value) }).ToList();
        return properties.All(x => ReferenceEquals(x.v, x.p.Value))
            ? structure
            : new StructureValue(properties.Select(x => new LogEventProperty(x.p.Name, x.v)), structure.TypeTag);
    }

    private static DictionaryValue RedactDictionary(DictionaryValue dictionary)
    {
        var entries = dictionary.Elements.Select(e => new { e, v = Redact(e.Value) }).ToList();
        return entries.All(x => ReferenceEquals(x.v, x.e.Value))
            ? dictionary
            : new DictionaryValue(entries.Select(x => new KeyValuePair<ScalarValue, LogEventPropertyValue>(x.e.Key, x.v)));
    }
}
