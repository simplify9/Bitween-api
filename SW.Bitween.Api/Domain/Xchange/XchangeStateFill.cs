using System;

namespace SW.Bitween.Domain;

/// <summary>
/// How far the background job has got filling <see cref="Xchange.Status"/> and
/// <see cref="Xchange.Retried"/> on exchanges from before those columns existed. One row, or none
/// before the job first runs.
/// </summary>
/// <remarks>
/// Kept in its own table rather than in Settings, which holds what people edit.
/// </remarks>
public class XchangeStateFill
{
    /// <summary>The id of the only row.</summary>
    public const int SingleId = 1;

    private XchangeStateFill()
    {
    }

    public XchangeStateFill(DateTime fillBegan)
    {
        Id = SingleId;
        Phase = XchangeStateFillPhase.Fill;
        FillBegan = fillBegan;
        UpdatedOn = fillBegan;
    }

    public int Id { get; private set; }

    public XchangeStateFillPhase Phase { get; private set; }

    /// <summary>
    /// When the fill started. Exchanges started before it are filled in; those started after it are
    /// labeled as they run, and checked once more at the end.
    /// </summary>
    public DateTime FillBegan { get; private set; }

    /// <summary>
    /// The last exchange filled in, newest first, by its start time and id. Blank before the first batch.
    /// </summary>
    public DateTime? PositionStartedOn { get; private set; }

    /// <inheritdoc cref="PositionStartedOn"/>
    public string PositionId { get; private set; }

    public DateTime UpdatedOn { get; private set; }
}

public enum XchangeStateFillPhase
{
    /// <summary>Labeling exchanges from before the columns existed.</summary>
    Fill = 1,

    /// <summary>Building the index the views read.</summary>
    Index = 2,

    /// <summary>Checking once more the exchanges that ran while the fill did.</summary>
    Recheck = 3,

    /// <summary>Done: the views read the columns.</summary>
    Ready = 4,
}
