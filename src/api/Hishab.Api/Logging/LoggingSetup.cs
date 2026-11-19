using System.Globalization;
using Serilog;
using Serilog.Formatting.Compact;

namespace Hishab.Api.Logging;

public static class LoggingSetup
{
    private const string TextTemplate =
        "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj} {{CorrelationId={CorrelationId}, TenantId={TenantId}}}{NewLine}{Exception}";

    /// <summary>
    /// Serilog for the whole app: levels from the "Serilog" config section, sinks from
    /// <see cref="ObservabilityOptions"/>, plus any <see cref="Serilog.Core.ILogEventSink"/>
    /// registered in DI (tests use this to capture events).
    /// </summary>
    public static WebApplicationBuilder AddHishabLogging(this WebApplicationBuilder builder)
    {
        builder.Services.AddOptions<ObservabilityOptions>()
            .Bind(builder.Configuration.GetSection(ObservabilityOptions.SectionName))
            .Validate(o => o.HasValidSeqUrl, "Observability:SeqUrl must be an absolute URL when set.")
            .ValidateOnStart();

        var options = builder.Configuration.GetSection(ObservabilityOptions.SectionName).Get<ObservabilityOptions>()
            ?? new ObservabilityOptions();

        builder.Services.AddSerilog((services, logger) =>
        {
            logger
                .ReadFrom.Configuration(builder.Configuration)
                .ReadFrom.Services(services)
                .Enrich.FromLogContext()
                .Enrich.WithProperty("Application", "Hishab.Api")
                .Enrich.With<PhoneRedactionEnricher>(); // last, so it sees every property

            if (options.JsonConsole)
            {
                logger.WriteTo.Console(new RenderedCompactJsonFormatter());
            }
            else
            {
                logger.WriteTo.Console(outputTemplate: TextTemplate, formatProvider: CultureInfo.InvariantCulture);
            }

            if (!string.IsNullOrWhiteSpace(options.SeqUrl) && options.HasValidSeqUrl)
            {
                logger.WriteTo.Seq(options.SeqUrl, formatProvider: CultureInfo.InvariantCulture);
            }
        });

        return builder;
    }
}
