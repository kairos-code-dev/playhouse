#nullable enable

namespace PlayHouse.Abstractions.Play;

/// <summary>
/// Specifies how a stage type is managed.
/// </summary>
public enum StageMode
{
    /// <summary>
    /// Stage is selected by explicit stage ID.
    /// </summary>
    Multi = 0,

    /// <summary>
    /// Stage is resolved per account and stage type at authentication time.
    /// </summary>
    Single = 1
}
