using System;
using System.Text.Json.Serialization;

namespace SW.Bitween.Model;

/// <summary>
/// Calculates the delay before each successive retry attempt.
/// Concrete implementations are serialised polymorphically via <c>System.Text.Json</c>
/// using the <c>"type"</c> discriminator property.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(FixedDelayStrategy),       typeDiscriminator: "fixed")]
[JsonDerivedType(typeof(LinearDelayStrategy),      typeDiscriminator: "linear")]
[JsonDerivedType(typeof(ExponentialDelayStrategy), typeDiscriminator: "exponential")]
public abstract class DelayStrategy
{
    /// <summary>
    /// Returns the wait duration before the next retry.
    /// </summary>
    /// <param name="attemptIndex">
    /// Zero-based index: <c>0</c> = delay before the first retry,
    /// <c>1</c> = delay before the second, and so on.
    /// </param>
    public abstract TimeSpan GetDelay(int attemptIndex);
}

/// <summary>
/// Waits the same fixed duration before every retry attempt.
/// </summary>
public class FixedDelayStrategy : DelayStrategy
{
    /// <summary>Wait time in milliseconds for every attempt.</summary>
    public int DelayMs { get; init; }

    /// <inheritdoc/>
    public override TimeSpan GetDelay(int _) => TimeSpan.FromMilliseconds(DelayMs);
}

/// <summary>
/// Increases the wait by a fixed increment on each attempt:
/// <c>Initial</c>, <c>Initial + Increment</c>, <c>Initial + 2×Increment</c>, …
/// </summary>
public class LinearDelayStrategy : DelayStrategy
{
    /// <summary>Wait time in milliseconds before the first retry.</summary>
    public int InitialDelayMs { get; init; }

    /// <summary>Additional milliseconds added for each successive attempt.</summary>
    public int IncrementMs { get; init; }

    /// <inheritdoc/>
    public override TimeSpan GetDelay(int attemptIndex) =>
        TimeSpan.FromMilliseconds(InitialDelayMs + (long)attemptIndex * IncrementMs);
}

/// <summary>
/// Multiplies the delay on each attempt (default ×2), capped at <see cref="MaxDelayMs"/>.
/// Formula: <c>min(Initial × Multiplier^attemptIndex, MaxDelay)</c>.
/// </summary>
public class ExponentialDelayStrategy : DelayStrategy
{
    /// <summary>Wait time in milliseconds before the first retry.</summary>
    public int InitialDelayMs { get; init; }

    /// <summary>Growth factor applied on every attempt. Defaults to <c>2.0</c> (doubling).</summary>
    public double Multiplier { get; init; } = 2.0;

    /// <summary>Upper bound on the computed delay in milliseconds. Defaults to 30 seconds.</summary>
    public int MaxDelayMs { get; init; } = 30_000;

    /// <inheritdoc/>
    public override TimeSpan GetDelay(int attemptIndex)
    {
        var ms = InitialDelayMs * Math.Pow(Multiplier, attemptIndex);
        return TimeSpan.FromMilliseconds(Math.Min(ms, MaxDelayMs));
    }
}
