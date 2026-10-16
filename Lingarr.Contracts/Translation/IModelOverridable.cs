namespace Lingarr.Contracts.Translation;

/// <summary>
/// Optional capability of translation providers that accept a per-candidate model override.
/// <para>
/// The chain lets one provider appear several times with different models, but the model a provider
/// uses is otherwise a single global setting. Providers implementing this interface agree to use the
/// supplied model for the instance the override was applied to, without touching their settings.
/// Providers that do not implement it keep their single-model behaviour and refuse overrides.
/// </para>
/// </summary>
public interface IModelOverridable
{
    /// <summary>
    /// Uses <paramref name="model"/> for this instance instead of the provider's configured default
    /// model. Applied by the translation factory right after construction, before any translation.
    /// </summary>
    void OverrideModel(string model);
}
