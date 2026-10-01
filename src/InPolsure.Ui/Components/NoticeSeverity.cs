namespace InPolsure.Ui.Components;

/// <summary>Severity of a <see cref="Notice"/>; it sets the ARIA role, the hidden text prefix and the colours.</summary>
public enum NoticeSeverity
{
    /// <summary>Neutral information (<c>role="status"</c>, polite).</summary>
    Info,

    /// <summary>Something the user should be aware of (<c>role="status"</c>, polite).</summary>
    Warning,

    /// <summary>An error the user must act on (<c>role="alert"</c>, assertive).</summary>
    Error,
}
