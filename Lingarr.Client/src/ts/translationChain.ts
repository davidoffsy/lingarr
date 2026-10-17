/**
 * Pure helpers for the `service_type` translation chain: the ordered list of
 * `{ provider, model? }` candidates the server walks when a translation fails.
 *
 * The canonical stored form is a JSON array of objects, for example
 * `[{"provider":"custom","model":"model-a"},{"provider":"deepl"}]`. Reading stays
 * compatible with every historical shape (a bare provider name, an array of provider
 * names, mixed arrays, a single object or a JSON string root), so this module mirrors
 * the server side parser/serializer contract without touching any store or component.
 *
 * Correspondence with the server implementation:
 * - `parseTranslationChain` mirrors `Lingarr.Server/Services/Translation/Chain/TranslationChainParser.cs`
 *   plus the fallback policy of `TranslationChainResolver.cs`.
 * - `serializeTranslationChain` mirrors `TranslationChainSerializer.cs` (a blank model is omitted,
 *   unknown properties are dropped).
 * - `chainEntryKey` mirrors the `(provider, model)` identity used by `TranslationChainValidator.cs`
 *   and by `TranslationFactory.cs` when it skips duplicate candidates.
 *
 * Known differences kept on purpose (this module must not invent behaviour the server does not have):
 * - malformed JSON and unusable values read as an empty chain here; the server reports an issue and
 *   falls back to the default provider instead, which `ITranslationChainValidation` surfaces;
 * - nested arrays are flattened recursively here, while the server's parser reports `InvalidEntry`
 *   for them (they are not part of the contract, flattening only keeps historical values usable);
 * - a provider or model that is not a string is dropped here (the server reports `InvalidModel` for
 *   a non-string model and keeps the entry with the provider default).
 */

export interface ITranslationChainEntry {
    provider: string
    model?: string
}

export interface ITranslationChainIssue {
    /** Machine readable kind, mirroring `TranslationChainIssueKind` on the server. */
    kind: string
    /** Zero based candidate index, or -1 for a chain level problem. */
    index: number
    /** Message intended for display in the settings UI. */
    message: string
    /** Offending provider or model text when the server sent one. */
    value?: string | null
}

/** Response of `POST api/plugin/translation-chain/validate`. */
export interface ITranslationChainValidation {
    /** Canonical object array form the server would store. */
    serviceType: string
    /** Candidates the chain resolves to, rendered as `custom (model-a)`. */
    candidates: string[]
    /** True when the value held no usable candidate and the default provider applies. */
    usedDefaultFallback: boolean
    issues: ITranslationChainIssue[]
    /** True when the chain can be used as configured. */
    isValid: boolean
}

/**
 * Separator of the `(provider, model)` identity key. Must stay identical to the server:
 * `Lingarr.Server/Services/Translation/Chain/TranslationChainValidator.cs` (`IdentitySeparator`)
 * and `Lingarr.Server/Services/Translation/TranslationFactory.cs` (the duplicate candidate check).
 */
export const CHAIN_IDENTITY_SEPARATOR = '\u001f'

/**
 * Parses a raw `service_type` value (setting string, array or object) into chain entries.
 * Never throws: unusable parts are dropped and malformed JSON reads as an empty chain.
 */
export function parseTranslationChain(raw: unknown): ITranslationChainEntry[] {
    if (typeof raw !== 'string') {
        return readValue(raw)
    }

    const trimmed = raw.trim()
    if (!trimmed) {
        return []
    }

    // Values that do not start like JSON are the historical single provider form.
    if (!hasJsonRoot(trimmed)) {
        return createEntry(trimmed, undefined)
    }

    try {
        return readValue(JSON.parse(trimmed))
    } catch {
        return []
    }
}

/**
 * Writes chain entries back into the canonical object array form. A blank model is omitted
 * (never written as `null`) so the provider keeps using its configured default model.
 */
export function serializeTranslationChain(entries: ITranslationChainEntry[]): string {
    return JSON.stringify(
        entries.map((entry) => {
            const provider = (entry.provider ?? '').trim()
            const model = normalizeModel(entry.model)
            return model ? { provider, model } : { provider }
        })
    )
}

/**
 * Identity of one entry as the server compares it: provider trimmed and lower cased,
 * model trimmed but case sensitive. Blank models count as "no override".
 */
export function chainEntryKey(entry: ITranslationChainEntry): string {
    return `${(entry.provider ?? '').trim().toLowerCase()}${CHAIN_IDENTITY_SEPARATOR}${
        normalizeModel(entry.model) ?? ''
    }`
}

/**
 * Index of the first entry that repeats an earlier `(provider, model)` identity, or `-1`
 * when every entry is unique. A provider may appear several times with different models.
 */
export function duplicateChainEntryIndex(entries: ITranslationChainEntry[]): number {
    const seen = new Set<string>()

    for (const [index, entry] of entries.entries()) {
        const key = chainEntryKey(entry)
        if (seen.has(key)) {
            return index
        }
        seen.add(key)
    }

    return -1
}

/**
 * Splits a backend `TranslationSource.DisplayName` key on the first colon. Keys are `provider:model`
 * (bare provider when there is no model); a model id may itself contain colons, so only the first
 * separator is the split.
 */
export function splitSourceDisplayName(displayName: string): { provider: string; model?: string } {
    const separator = displayName.indexOf(':')
    if (separator <= 0) {
        return { provider: displayName }
    }

    const model = displayName.slice(separator + 1)
    return model
        ? { provider: displayName.slice(0, separator), model }
        : { provider: displayName.slice(0, separator) }
}

/**
 * Providers of a chain, de-duplicated and lower cased in first appearance order. Used by
 * screens that only need "which providers are configured" (settings panels, template tabs).
 */
export function chainProviders(raw: unknown): string[] {
    const providers: string[] = []
    const seen = new Set<string>()

    for (const entry of parseTranslationChain(raw)) {
        const provider = entry.provider.toLowerCase()
        if (seen.has(provider)) {
            continue
        }
        seen.add(provider)
        providers.push(provider)
    }

    return providers
}

function readValue(value: unknown): ITranslationChainEntry[] {
    if (Array.isArray(value)) {
        return value.flatMap((item) => readValue(item))
    }

    if (typeof value === 'string') {
        return createEntry(value, undefined)
    }

    if (isRecord(value)) {
        const provider = value.provider
        if (typeof provider !== 'string') {
            return []
        }
        return createEntry(provider, typeof value.model === 'string' ? value.model : undefined)
    }

    return []
}

function createEntry(provider: string, model: unknown): ITranslationChainEntry[] {
    const trimmedProvider = provider.trim()
    if (!trimmedProvider) {
        return []
    }

    const normalizedModel = normalizeModel(model)
    return normalizedModel
        ? [{ provider: trimmedProvider, model: normalizedModel }]
        : [{ provider: trimmedProvider }]
}

function normalizeModel(model: unknown): string | undefined {
    if (typeof model !== 'string') {
        return undefined
    }

    const trimmed = model.trim()
    return trimmed ? trimmed : undefined
}

function isRecord(value: unknown): value is Record<string, unknown> {
    return value !== null && typeof value === 'object' && !Array.isArray(value)
}

function hasJsonRoot(value: string): boolean {
    const first = value[0]
    return first === '[' || first === '{' || first === '"'
}
