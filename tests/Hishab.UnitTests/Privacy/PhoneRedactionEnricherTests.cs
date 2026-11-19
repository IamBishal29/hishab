using Hishab.Api.Logging;
using Serilog.Events;
using Serilog.Parsing;

namespace Hishab.UnitTests.Privacy;

public class PhoneRedactionEnricherTests
{
    private static LogEvent Event(params LogEventProperty[] properties) => new(
        DateTimeOffset.UtcNow,
        LogEventLevel.Information,
        exception: null,
        new MessageTemplateParser().Parse("test"),
        properties);

    private static object? Scalar(LogEvent e, string name) => ((ScalarValue)e.Properties[name]).Value;

    [Fact]
    public void Phone_in_a_scalar_property_is_masked()
    {
        var logEvent = Event(new LogEventProperty("Phone", new ScalarValue("01712345678")));

        new PhoneRedactionEnricher().Enrich(logEvent, null!);

        Scalar(logEvent, "Phone").ShouldBe("01712****78");
    }

    [Fact]
    public void Phone_inside_a_destructured_object_is_masked()
    {
        var customer = new StructureValue([
            new LogEventProperty("Name", new ScalarValue("Nusrat")),
            new LogEventProperty("Contact", new ScalarValue("+8801819876543")),
        ]);
        var logEvent = Event(new LogEventProperty("Customer", customer));

        new PhoneRedactionEnricher().Enrich(logEvent, null!);

        var contact = ((StructureValue)logEvent.Properties["Customer"]).Properties.Single(p => p.Name == "Contact");
        ((ScalarValue)contact.Value).Value.ShouldBe("01819****43");
    }

    [Fact]
    public void Properties_without_phones_are_left_as_the_same_instance()
    {
        var original = new ScalarValue("Order 42 confirmed");
        var logEvent = Event(new LogEventProperty("Note", original), new LogEventProperty("Count", new ScalarValue(3)));

        new PhoneRedactionEnricher().Enrich(logEvent, null!);

        logEvent.Properties["Note"].ShouldBeSameAs(original);
        Scalar(logEvent, "Count").ShouldBe(3);
    }
}
