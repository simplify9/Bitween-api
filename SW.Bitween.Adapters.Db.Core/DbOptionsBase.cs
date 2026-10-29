namespace SW.Bitween.Adapters.Db;

/// <summary>
/// The settings every relational adapter accepts, whatever the engine. Each concrete adapter
/// derives from this and adds its own connection fields, then marks its class
/// <c>[AdapterSettings(Kind = "Relational", …)]</c> so Bitween can build the form from it.
///
/// Everything here arrives as a DataSource property, bound by name.
/// </summary>
public abstract class DbOptionsBase
{
    // ------------------------------------------------------------------ pooling

    /// <summary>
    /// The reason this adapter is resident at all. An ephemeral adapter gets a cold pool per
    /// process, so every Xchange pays a TCP connect, a TLS handshake and an authentication round
    /// trip - 20-150 ms to a remote database, and considerably more to Oracle. A process that
    /// stays up pays it once.
    /// </summary>
    [AdapterSetting(Default = "2", Hint =
        "Connections kept open even while idle. Above zero, a scheduled job at 3am runs instead of "
        + "timing out reconnecting. Costs one server session per connection, per node.")]
    public int MinPoolSize { get; set; } = 2;

    [AdapterSetting(Default = "20", Hint =
        "Ceiling on concurrent connections FROM THIS NODE. Multiply by your replica count before "
        + "comparing it to the server's session limit.")]
    public int MaxPoolSize { get; set; } = 20;

    [AdapterSetting(Default = "15", Hint = "Seconds to wait for a connection before giving up.")]
    public int ConnectTimeoutSeconds { get; set; } = 15;

    // ------------------------------------------------------------------ execution

    [AdapterSetting(Default = "30", Hint =
        "Default seconds a statement may run. A statement can raise its own; this is the ceiling "
        + "for anything that does not.")]
    public int CommandTimeoutSeconds { get; set; } = 30;

    [AdapterSetting(Default = "1000", Hint =
        "Rows a single query may return. Exceeding it fails with a message pointing at the paging "
        + "commands rather than quietly handing back a truncated answer.")]
    public int MaxRows { get; set; } = 1000;

    [AdapterSetting(Default = "60", Hint =
        "Seconds an unread paging cursor is kept before it is closed. A cursor holds a pooled "
        + "connection open, so an abandoned one is a connection nobody can use.")]
    public int CursorIdleTimeoutSeconds { get; set; } = 60;

    // ------------------------------------------------------------------ safety

    /// <summary>
    /// Named statements as JSON: <c>{"getOrder": "select * from orders where id = :id"}</c>.
    /// The message supplies parameter values; it never supplies SQL.
    ///
    /// Composed by Bitween from the data source's statement rows, not typed by an operator - which
    /// is why it is hidden. Statements are their own entity so that writing a query and changing a
    /// database password are different permissions; the adapter still just receives name-to-SQL and
    /// knows nothing about where they were kept.
    /// </summary>
    [AdapterSetting(Hidden = true, Hint =
        "Named statements as JSON - {\"name\":\"select …\"}. Composed by Bitween from the data "
        + "source's statements; not edited here.")]
    public string Statements { get; set; }

    [AdapterSetting(Default = "false", AllowedValues = new[] { "true", "false" }, Hint =
        "Lets a caller send SQL text instead of naming a configured statement. Off, and it should "
        + "stay off: a mapper is a template over message content, so SQL it can emit is SQL an "
        + "inbound message can steer.")]
    public bool AllowAdHocSql { get; set; }

    [AdapterSetting(Default = "false", AllowedValues = new[] { "true", "false" }, Hint =
        "Writes parameter VALUES into the adapter log at Debug level. Off: trace-logging a payment "
        + "insert is a data-protection incident wearing a debug flag. For a controlled test only.")]
    public bool LogParameterValues { get; set; }

    // ------------------------------------------------------------------ receiving

    // Where these live is the point, and it is not all one place.
    //
    // A connection is shared by every subscription pointed at it, so anything that varies per
    // reader cannot sit here - one data source could otherwise only ever feed one receiver. What
    // is left divides cleanly: the SQL and the shape of its rows belong to the STATEMENT, and the
    // reading policy belongs to the SUBSCRIPTION doing the reading.
    //
    // So the four below that describe SQL are legacy: still honoured when set, so a receiver
    // configured before the split keeps working, but hidden from the form because the answer is
    // now a statement's name and the statement's own columns. Mode and batch size stay, as the
    // default a subscription may override.

    [AdapterSetting(
        AllowedValues = new[] { "bulk", "incrementing", "timestamp", "timestamp+incrementing", "marker" },
        Hint =
        "Default for subscriptions that do not choose their own. How the receiver finds new rows: "
        + "bulk re-reads everything each poll; incrementing follows an always-growing column; "
        + "timestamp follows a modified-at column; marker reads rows a flag says are unprocessed. "
        + "None of them can see a DELETE.")]
    public string ReceiveMode { get; set; }

    /// <summary>
    /// Superseded by naming one of the data source's statements as the subscription's
    /// ReceiveStatement. Kept because a receiver configured before the split has its SQL here.
    /// </summary>
    [AdapterSetting(Hidden = true)]
    public string ReceiveStatement { get; set; }

    /// <summary>Superseded by the polled statement's own CursorColumn.</summary>
    [AdapterSetting(Hidden = true)]
    public string CursorColumn { get; set; }

    /// <summary>Superseded by the polled statement's own KeyColumn.</summary>
    [AdapterSetting(Hidden = true)]
    public string KeyColumn { get; set; }

    /// <summary>
    /// Superseded by naming a statement as the subscription's MarkProcessedStatement - Camel's
    /// onConsume, run against each row once Bitween has accepted it.
    /// </summary>
    [AdapterSetting(Hidden = true)]
    public string MarkProcessedStatement { get; set; }

    [AdapterSetting(Default = "500", Hint =
        "Default rows one poll may take, which a subscription may override. The next poll takes "
        + "the next batch.")]
    public int ReceiveBatchSize { get; set; } = 500;
}
