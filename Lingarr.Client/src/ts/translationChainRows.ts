import { ITranslationChainEntry } from './translationChain'

/**
 * One editable chain row. Every row carries a stable `id` so row handlers are never bound to a list
 * index: a model edit can still be in flight while the row is moved or removed, and an index captured
 * in a template closure would then write the value into a different row.
 *
 * `model` is a plain string and an empty one means "no model override", which is how the wire form
 * stores it (a blank model is omitted).
 */
export interface IChainRow {
    id: string
    provider: string
    model: string
}

let rowSequence = 0

function createChainRowId(): string {
    // crypto.randomUUID only exists in a secure context; a LAN deployment over plain http is not one.
    return globalThis.crypto?.randomUUID?.() ?? `chain-row-${++rowSequence}`
}

export function createChainRow(provider: string, model = ''): IChainRow {
    return { id: createChainRowId(), provider, model }
}

/**
 * Rewrites `rows` from `entries`, keeping the id of every row that still describes the same candidate
 * at the same position. Stable ids keep the model field's uncommitted draft and focus alive while a
 * save round-trip re-derives the list from the settings store. `fallbackProvider` keeps the editor
 * non-empty when the stored value holds no usable entry; `entries` are never written back unchanged.
 */
export function applyChainEntries(
    rows: IChainRow[],
    entries: ITranslationChainEntry[],
    fallbackProvider: string
): IChainRow[] {
    const source = entries.length > 0 ? entries : [{ provider: fallbackProvider }]

    return source.map((entry, index) => {
        const current = rows[index]
        const model = entry.model ?? ''
        if (current && current.provider === entry.provider && current.model === model) {
            return current
        }

        return createChainRow(entry.provider, model)
    })
}

/** Wire form of the edited rows; a blank model means "use the provider default". */
export function chainRowEntries(rows: IChainRow[]): ITranslationChainEntry[] {
    return rows.map((row) =>
        row.model.trim() ? { provider: row.provider, model: row.model } : { provider: row.provider }
    )
}

/** Providers of the chain, lower cased for comparison against the provider list. */
export function usedChainProviders(rows: IChainRow[]): Set<string> {
    return new Set(rows.map((row) => row.provider.trim().toLowerCase()))
}
