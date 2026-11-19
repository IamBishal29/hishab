namespace Hishab.Api.Logging;

public sealed class ObservabilityOptions
{
    public const string SectionName = "Observability";

    /// <summary>Seq server URL (e.g. http://localhost:5341). Empty disables the Seq sink.</summary>
    public string? SeqUrl { get; set; }

    /// <summary>Write compact JSON to the console (containers, log shippers) instead of readable text.</summary>
    public bool JsonConsole { get; set; }

    public bool HasValidSeqUrl =>
        string.IsNullOrWhiteSpace(SeqUrl) || Uri.TryCreate(SeqUrl, UriKind.Absolute, out _);
}
