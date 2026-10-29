using System;

namespace SW.Bitween.Adapters;

/// <summary>
/// Marks the type that describes an adapter's connection settings - one per adapter.
///
/// The point is that the UI stops knowing anything about a particular broker. Before this, the
/// fields a data source form offered, their defaults and their allowed values were a hand-written
/// table in the front end, which meant the form and the adapter could disagree silently: a hint
/// told operators to set "Tls" for a very long time while the adapter only ever read "UseSsl", so
/// a connection that looked encrypted in the UI was in the clear on the wire.
///
/// The adapter is the only thing that knows what it accepts, so it is the thing that says so.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class AdapterSettingsAttribute : Attribute
{
    /// <summary>What to call this provider in a menu. Falls back to the adapter id.</summary>
    public string Label { get; set; }

    /// <summary>
    /// What kind of thing this connects to, from Bitween's DataSourceKind vocabulary: Broker,
    /// Relational, Document, ObjectStore or Http. It is not decoration - a bus gateway can only
    /// read from a Broker, so a resident adapter that holds a database connection must not be
    /// offerable as one. Unstated means Broker, which is all that existed when this was added.
    /// </summary>
    public string Kind { get; set; } = "Broker";

    /// <summary>One line, shown under the provider chooser.</summary>
    public string Description { get; set; }
}

/// <summary>
/// One operator-visible setting, declared beside the property that consumes it.
///
/// Every public read/write property of an <see cref="AdapterSettingsAttribute"/> type is a setting
/// whether or not it carries this attribute - nothing an adapter binds is invisible to the person
/// configuring it. The attribute adds what reflection cannot see: why the field exists, what values
/// are legal, whether it holds a credential, and what the default is (a property initialiser lives
/// in IL that is deliberately never executed here).
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class AdapterSettingAttribute : Attribute
{
    /// <summary>Shown under the input. Say what it is for, not what its type is.</summary>
    public string Hint { get; set; }

    /// <summary>
    /// The value a new data source starts with. Stated rather than inferred: the describer reads
    /// the assembly without running it, so a property initialiser is not observable.
    /// </summary>
    public string Default { get; set; }

    /// <summary>
    /// The complete set of legal values. Present means the UI offers a choice instead of a text
    /// box, which is the difference between "quorum" and a typo the broker rejects at declare time.
    /// </summary>
    public string[] AllowedValues { get; set; }

    /// <summary>Holds a credential: masked in responses and never shown back.</summary>
    public bool Secret { get; set; }

    /// <summary>Offered on the create form and flagged when left empty.</summary>
    public bool Required { get; set; }

    /// <summary>
    /// Supplied by Bitween itself, not by an operator - endpoints come from the gateways bound to
    /// the data source, and Consume is how a connection test avoids draining a live queue.
    /// Hidden settings are not offered as fields at all.
    /// </summary>
    public bool Hidden { get; set; }
}
