<template>
    <CardComponent title="Services">
        <template #description>
            Each row is one candidate: a provider plus an optional model. The same provider may
            appear more than once with different models. Closer language matches are tried first;
            within the same match, rows are tried top to bottom when a candidate fails.
        </template>
        <template #content>
            <SaveNotification ref="saveNotification" />

            <div v-if="!settingsLoaded" class="text-sm opacity-60">
                Loading translation services...
            </div>
            <div v-else class="space-y-2">
                <span class="font-semibold">Translation services</span>
                <ol class="space-y-1">
                    <li
                        v-for="(row, index) in rows"
                        :key="row.id"
                        class="rounded-md py-2.5"
                        :class="{ 'bg-accent/10': row.id === configuringRowId }">
                        <div
                            class="grid min-w-0 grid-cols-[auto_minmax(0,1fr)_auto] items-center gap-x-2 gap-y-1.5">
                            <span
                                class="bg-accent/20 text-accent-content shrink-0 rounded px-2 py-0.5 text-xs font-semibold tracking-wider whitespace-nowrap uppercase"
                                aria-hidden="true">
                                {{ index === 0 ? 'Primary' : `Fallback ${index}` }}
                            </span>
                            <div class="min-w-0">
                                <SelectComponent
                                    :selected="row.provider"
                                    :options="serviceOptions"
                                    size="sm"
                                    :no-options="
                                        pluginListUnavailable
                                            ? 'The provider list is unavailable.'
                                            : 'No providers available.'
                                    "
                                    @update:selected="
                                        (value: string) => updateProvider(row.id, value)
                                    " />
                            </div>
                            <div class="flex shrink-0 items-center">
                                <button
                                    type="button"
                                    class="text-primary-content hover:text-primary-content/50 focus-visible:ring-accent cursor-pointer rounded p-1 transition-colors focus-visible:ring-2 focus-visible:outline-none disabled:cursor-not-allowed disabled:opacity-30"
                                    :disabled="row.id === configuringRowId"
                                    :aria-pressed="row.id === configuringRowId"
                                    title="Configure credentials"
                                    aria-label="Configure credentials"
                                    @click="configuringRowId = row.id">
                                    <SettingIcon class="h-4 w-4" />
                                </button>
                                <button
                                    type="button"
                                    class="text-primary-content hover:text-primary-content/50 focus-visible:ring-accent cursor-pointer rounded p-1 transition-colors focus-visible:ring-2 focus-visible:outline-none disabled:cursor-not-allowed disabled:opacity-30"
                                    :disabled="index === 0"
                                    title="Move up"
                                    aria-label="Move up"
                                    @click="moveRow(row.id, -1)">
                                    <CaretUpIcon class="h-4 w-4" />
                                </button>
                                <button
                                    type="button"
                                    class="text-primary-content hover:text-primary-content/50 focus-visible:ring-accent cursor-pointer rounded p-1 transition-colors focus-visible:ring-2 focus-visible:outline-none disabled:cursor-not-allowed disabled:opacity-30"
                                    :disabled="index === rows.length - 1"
                                    title="Move down"
                                    aria-label="Move down"
                                    @click="moveRow(row.id, 1)">
                                    <CaretDownIcon class="h-4 w-4" />
                                </button>
                                <button
                                    type="button"
                                    class="text-primary-content hover:text-primary-content/50 focus-visible:ring-accent cursor-pointer rounded p-1 transition-colors focus-visible:ring-2 focus-visible:outline-none disabled:cursor-not-allowed disabled:opacity-30"
                                    :disabled="rows.length <= 1"
                                    title="Remove service"
                                    aria-label="Remove service"
                                    @click="removeRow(row.id)">
                                    <TrashIcon class="h-4 w-4" />
                                </button>
                            </div>
                            <div
                                v-if="supportsModelOverride(row.provider)"
                                class="col-span-2 col-start-2 min-w-0">
                                <TranslationChainModelField
                                    :provider="row.provider"
                                    :model-value="row.model"
                                    @update:model-value="
                                        (value: string) => updateModel(row.id, value)
                                    " />
                            </div>
                            <div
                                v-else-if="row.model.trim()"
                                class="col-span-2 col-start-2 min-w-0">
                                <TranslationChainModelField
                                    v-if="pluginListUnavailable"
                                    :provider="row.provider"
                                    :model-value="row.model"
                                    @update:model-value="
                                        (value: string) => updateModel(row.id, value)
                                    " />
                                <p v-else class="truncate text-xs" :title="row.model">
                                    {{ row.model }}
                                </p>
                                <button
                                    v-if="showClearUnsupportedModel(row)"
                                    type="button"
                                    class="text-xs text-red-500 hover:underline"
                                    @click="clearModel(row.id)">
                                    Clear unsupported model
                                </button>
                            </div>
                        </div>
                        <ul v-if="rowIssuesFor(index).length" class="mt-1 space-y-0.5 pl-1">
                            <li
                                v-for="(issue, issueIndex) in rowIssuesFor(index)"
                                :key="`${issue.kind}-${issueIndex}`"
                                class="text-xs text-red-500">
                                {{ issue.message }}
                            </li>
                        </ul>
                    </li>
                    <li>
                        <div class="flex items-center gap-2">
                            <ButtonComponent
                                variant="ghost"
                                size="xs"
                                :disabled="addRowDisabledReason !== null"
                                :title="addRowDisabledReason ?? undefined"
                                @click="addRow">
                                <PlusIcon class="mr-1 h-3 w-3" />
                                {{
                                    rows.length === 0
                                        ? 'Add translation service'
                                        : 'Add fallback service'
                                }}
                            </ButtonComponent>
                            <span v-if="addRowDisabledReason" class="text-xs opacity-60">
                                {{ addRowDisabledReason }}
                            </span>
                        </div>
                    </li>
                </ol>
                <p class="text-sm opacity-70">
                    Address, API key, protocol and the default model are shared across every row of
                    the same provider. Only the model override on each row is independent.
                </p>
                <p v-if="pluginListUnavailable" class="text-sm text-amber-500">
                    The provider list could not be loaded. Stored models still show and can be typed
                    or cleared.
                </p>
                <p v-if="storedChainNotice" class="text-sm text-amber-500">
                    {{ storedChainNotice }}
                </p>
                <p v-if="saveError" class="text-sm text-red-500">{{ saveError }}</p>
                <p v-if="unsavedNotice" class="text-sm text-amber-500">{{ unsavedNotice }}</p>
                <p
                    v-for="(issue, index) in chainIssues"
                    :key="`${issue.kind}-${index}`"
                    class="text-sm text-red-500">
                    {{ issue.message }}
                </p>
                <p v-if="fallbackNotice" class="text-sm text-amber-500">{{ fallbackNotice }}</p>
            </div>

            <div
                v-if="settingsLoaded && (configuringManifest || manifestError)"
                class="mt-4 space-y-2">
                <div class="text-sm">
                    <span class="text-secondary-content/60">Configuring credentials for</span>
                    <span class="ml-1 font-semibold">{{ configuringLabel }}</span>
                </div>
                <p v-if="sharedCredentialsCount > 1" class="text-sm opacity-70">
                    {{ sharedCredentialsCount }} {{ configuringLabel }} rows share the credentials
                    below.
                </p>
                <DynamicPluginForm
                    v-if="configuringManifest"
                    :manifest="configuringManifest"
                    @save="saveNotification?.show()" />
                <p v-else-if="manifestError" class="text-sm text-red-500">{{ manifestError }}</p>
            </div>

            <div v-if="settingsLoaded && configuringManifest?.hasRequestTemplate" class="mt-6">
                <div class="flex flex-col gap-4">
                    <div class="flex flex-col space-x-2">
                        <span class="font-semibold">Customize request template and prompts</span>
                        Adjust the AI request body, system prompt and context for translations.
                    </div>
                    <ButtonComponent variant="primary" size="md" @click="openRequestSettings">
                        Open Request Settings
                        <ArrowRight class="mt-1 ml-1 h-4 w-4" />
                    </ButtonComponent>
                </div>
            </div>

            <SourceAndTarget @save="saveNotification?.show()" />
        </template>
    </CardComponent>
</template>

<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { useRouter } from 'vue-router'
import { useSettingStore } from '@/store/setting'
import {
    applyChainEntries,
    chainRowEntries,
    createChainRow,
    duplicateChainEntryIndex,
    IChainRow,
    IPluginManifest,
    IPluginSummary,
    ISelectOption,
    ITranslationChainEntry,
    ITranslationChainIssue,
    parseTranslationChain,
    serializeTranslationChain,
    SETTINGS,
    usedChainProviders
} from '@/ts'
import servicesApi from '@/services'
import CardComponent from '@/components/common/CardComponent.vue'
import SelectComponent from '@/components/common/SelectComponent.vue'
import ButtonComponent from '@/components/common/ButtonComponent.vue'
import SaveNotification from '@/components/common/SaveNotification.vue'
import DynamicPluginForm from '@/components/features/settings/DynamicPluginForm.vue'
import SourceAndTarget from '@/components/features/settings/SourceAndTarget.vue'
import TranslationChainModelField from '@/components/features/settings/TranslationChainModelField.vue'
import ArrowRight from '@/components/icons/ArrowRight.vue'
import CaretUpIcon from '@/components/icons/CaretUpIcon.vue'
import CaretDownIcon from '@/components/icons/CaretDownIcon.vue'
import TrashIcon from '@/components/icons/TrashIcon.vue'
import PlusIcon from '@/components/icons/PlusIcon.vue'
import SettingIcon from '@/components/icons/SettingIcon.vue'

interface IChainValidationResult {
    issues: ITranslationChainIssue[]
    usedDefaultFallback: boolean
    candidates: string[]
    isValid: boolean
}

const saveNotification = ref<InstanceType<typeof SaveNotification> | null>(null)
const settingsStore = useSettingStore()
const router = useRouter()

const serviceOptions = ref<ISelectOption[]>([])
const pluginSummaries = ref<Map<string, IPluginSummary>>(new Map())
const providersLoaded = ref(false)

const settingsLoaded = computed(() => settingsStore.getSetting(SETTINGS.SERVICE_TYPE) !== undefined)

const pluginListUnavailable = computed(
    () => providersLoaded.value && serviceOptions.value.length === 0
)

/** Last value the server is known to hold; restored when a save request fails. */
const savedChain = ref<string>('')
const rows = ref<IChainRow[]>([])
const validation = ref<IChainValidationResult | null>(null)
const saveError = ref<string | null>(null)
const configuringRowId = ref<string | null>(null)
const configuringManifest = ref<IPluginManifest | null>(null)
const manifestError = ref<string | null>(null)
/** Monotonic counter so overlapping persist calls can drop a stale validate or save. */
let persistGeneration = 0
/** True while the persist loop is running; overlapping calls only replace `pendingNext`. */
let persistInFlight = false
/** Latest chain to persist; always replaced, never queued behind an older value. */
let pendingNext: IChainRow[] | null = null
/** True while a settings write is in flight, so the store watcher ignores our own optimistic write. */
let saveInFlight = false

const configuringRow = computed<IChainRow | null>(
    () => rows.value.find((row) => row.id === configuringRowId.value) ?? null
)

const rowIssues = computed(() => {
    const byRow = new Map<number, ITranslationChainIssue[]>()
    for (const issue of validation.value?.issues ?? []) {
        if (issue.index < 0) {
            continue
        }
        const current = byRow.get(issue.index)
        if (current) {
            current.push(issue)
        } else {
            byRow.set(issue.index, [issue])
        }
    }
    return byRow
})

const chainIssues = computed(() =>
    (validation.value?.issues ?? []).filter((issue) => issue.index < 0)
)

/**
 * A rejected edit stays in the editor so the user can fix the row, which means the editor diverges
 * from the stored chain for a moment. Say so out loud instead of leaving a silently unsaved value.
 */
const unsavedNotice = computed<string | null>(() =>
    validation.value && !validation.value.isValid
        ? 'The chain is not saved. Fix the errors above to store it.'
        : null
)

/**
 * Empty or unreadable stored values must not look like a saved LibreTranslate row. Runtime still
 * falls back to the default provider, which is said here instead of rendered as an editable chain.
 */
const storedChainNotice = computed<string | null>(() => {
    if (!settingsLoaded.value) {
        return null
    }

    const raw = savedChain.value
    if (parseTranslationChain(raw).length > 0 || rows.value.length > 0) {
        return null
    }

    return raw.trim()
        ? 'The stored value is not a valid translation chain. It has not been replaced with a default provider. Add a service to overwrite it.'
        : 'No translation service is stored. The default provider applies at runtime until a valid chain is saved.'
})

const fallbackNotice = computed<string | null>(() => {
    const result = validation.value
    if (!result?.usedDefaultFallback) {
        return null
    }

    const candidates = result.candidates.join(', ')
    return candidates
        ? `The stored value holds no usable candidate, so the default provider (${candidates}) applies until a valid chain is saved.`
        : 'The stored value holds no usable candidate, so the default provider applies until a valid chain is saved.'
})

const configuringLabel = computed(() => {
    const provider = configuringRow.value?.provider
    if (!provider) {
        return ''
    }
    return serviceOptions.value.find((option) => option.value === provider)?.label ?? provider
})

/** Rows that reuse the provider whose credentials are currently open. */
const sharedCredentialsCount = computed(() => {
    const provider = configuringRow.value?.provider.trim().toLowerCase()
    if (!provider) {
        return 0
    }

    return rows.value.filter((row) => row.provider.trim().toLowerCase() === provider).length
})

/**
 * Provider a new row starts with: an unused provider when one is left, otherwise the first entry of
 * the list. The unique unit of the chain is the `(provider, model)` candidate, not the provider, so a
 * second row for the same provider stays valid and is how one provider gets several models
 * (`custom (model-a)`, `custom (model-b)`).
 */
const newRowProvider = computed<ISelectOption | null>(() => {
    const used = usedChainProviders(rows.value)
    return (
        serviceOptions.value.find((option) => !used.has(option.value.trim().toLowerCase())) ??
        serviceOptions.value[0] ??
        null
    )
})

/** A missing provider list is the only reason the add button is disabled. */
const addRowDisabledReason = computed<string | null>(() => {
    if (serviceOptions.value.length > 0) {
        return null
    }

    return providersLoaded.value ? 'The provider list is unavailable.' : 'Loading providers...'
})

function describeEntry(entry: ITranslationChainEntry): string {
    const model = entry.model?.trim()
    return model ? `${entry.provider} (${model})` : entry.provider
}

function rowIndexById(rowId: string): number {
    return rows.value.findIndex((row) => row.id === rowId)
}

function supportsModelOverride(provider: string): boolean {
    return pluginSummaries.value.get(provider.trim().toLowerCase())?.supportsModelOverride === true
}

function showClearUnsupportedModel(row: IChainRow): boolean {
    return row.model.trim() !== '' && providersLoaded.value && !supportsModelOverride(row.provider)
}

/** Provider of the row whose credentials are being configured, lower cased for comparisons. */
function configuredProvider(): string {
    return configuringRow.value?.provider.trim().toLowerCase() ?? ''
}

function rowIssuesFor(index: number): ITranslationChainIssue[] {
    return rowIssues.value.get(index) ?? []
}

/**
 * Replaces the editor with a stored chain. Candidates that did not change keep their row id, so a save
 * round-trip neither steals focus nor drops a model draft that is still being typed.
 */
function adoptStoredChain(raw: unknown): void {
    rows.value = applyChainEntries(rows.value, parseTranslationChain(raw))
    validation.value = null

    if (!rows.value.some((row) => row.id === configuringRowId.value)) {
        configuringRowId.value = rows.value[0]?.id ?? null
    }
}

/**
 * Client-side checks that match the server validator for empty/unknown/duplicate rows. Used both as
 * a fast reject before calling the endpoint and as the only check we can still run if the plugin
 * list never loaded (unknown providers then cannot be proven).
 */
function localValidation(entries: ITranslationChainEntry[]): IChainValidationResult {
    const issues: ITranslationChainIssue[] = []
    if (entries.length === 0) {
        issues.push({
            kind: 'EmptyChain',
            index: -1,
            message: 'The translation chain has no entries.',
            value: null
        })
    }

    const known = pluginSummaries.value
    for (const [index, entry] of entries.entries()) {
        const provider = (entry.provider ?? '').trim()
        if (!provider) {
            issues.push({
                kind: 'EmptyProvider',
                index,
                message: 'Chain entry has no provider identifier.',
                value: null
            })
            continue
        }

        const key = provider.toLowerCase()
        if (known.size > 0 && !known.has(key)) {
            issues.push({
                kind: 'UnknownProvider',
                index,
                message: `Unknown translation provider '${provider}'.`,
                value: provider
            })
            continue
        }

        const model = entry.model?.trim()
        if (model && known.size > 0 && known.get(key)?.supportsModelOverride !== true) {
            issues.push({
                kind: 'ModelOverrideNotSupported',
                index,
                message: `Provider '${provider}' does not support a per-candidate model; remove the model override.`,
                value: model
            })
        }
    }

    const duplicate = duplicateChainEntryIndex(entries)
    if (duplicate >= 0) {
        issues.push({
            kind: 'DuplicateProviderModel',
            index: duplicate,
            message: `This row repeats ${describeEntry(entries[duplicate])} earlier in the chain.`,
            value: null
        })
    }

    return { issues, usedDefaultFallback: false, candidates: [], isValid: issues.length === 0 }
}

async function validateChain(entries: ITranslationChainEntry[]): Promise<IChainValidationResult> {
    const local = localValidation(entries)
    if (!local.isValid) {
        return local
    }

    try {
        const response = await servicesApi.translationChain.validateTranslationChain(
            serializeTranslationChain(entries)
        )

        return {
            issues: response.issues ?? [],
            usedDefaultFallback: response.usedDefaultFallback === true,
            candidates: response.candidates ?? [],
            isValid: response.isValid === true
        }
    } catch (error) {
        console.error('Failed to validate the translation chain', error)
        return {
            issues: [
                {
                    kind: 'ValidationUnavailable',
                    index: -1,
                    message:
                        'The translation chain could not be validated. Check the connection and try again.',
                    value: null
                }
            ],
            usedDefaultFallback: false,
            candidates: [],
            isValid: false
        }
    }
}

/**
 * Applies an edit and tries to store it.
 *
 * Overlapping calls collapse to the latest `next`: only one validate/save pair runs at a time, a
 * stale result is discarded, and a newer edit replaces `pendingNext` instead of starting a second
 * request. A chain the server rejects stays in the editor; a settings write that fails rolls back to
 * the last saved chain, unless a newer persist has already superseded it.
 */
async function persist(next: IChainRow[]): Promise<void> {
    if (!settingsLoaded.value) {
        return
    }

    persistGeneration += 1
    pendingNext = next
    rows.value = next
    saveError.value = null

    if (persistInFlight) {
        return
    }

    persistInFlight = true
    try {
        while (pendingNext !== null) {
            const snapshot = pendingNext
            pendingNext = null
            const generation = persistGeneration
            await persistSnapshot(snapshot, generation)
        }
    } finally {
        persistInFlight = false
    }
}

async function persistSnapshot(snapshot: IChainRow[], generation: number): Promise<void> {
    const entries = chainRowEntries(snapshot)
    const result = await validateChain(entries)
    if (generation !== persistGeneration) {
        return
    }

    validation.value = result
    if (!result.isValid) {
        return
    }

    if (generation !== persistGeneration) {
        return
    }

    const serialized = serializeTranslationChain(entries)
    saveInFlight = true
    try {
        await settingsStore.updateSetting(SETTINGS.SERVICE_TYPE, serialized, true)
        // The server now holds this value even if a newer edit is already queued.
        savedChain.value = serialized
        if (generation !== persistGeneration) {
            return
        }

        validation.value = null
        saveNotification.value?.show()
    } catch (error) {
        console.error('Failed to save the translation chain', error)
        if (generation !== persistGeneration) {
            return
        }

        // updateSetting writes the setting into the store before the request, so a failed save leaves
        // the in-memory value ahead of the server. Restore explicitly: the watcher is muted while
        // saveInFlight is true.
        settingsStore.storeSetting(SETTINGS.SERVICE_TYPE, savedChain.value)
        adoptStoredChain(savedChain.value)
        saveError.value =
            'The translation chain could not be saved. Check the connection and try again.'
    } finally {
        saveInFlight = false
    }
}

function updateProvider(rowId: string, provider: string): void {
    const index = rowIndexById(rowId)
    if (index < 0) {
        return
    }

    const row = rows.value[index]
    if (row.provider.trim().toLowerCase() === provider.trim().toLowerCase()) {
        // SelectComponent emits for the already selected option too, and re-selecting a provider must
        // not drop the model override the row carries.
        return
    }

    // A model id belongs to one provider, so switching provider clears the row model; a failed save
    // rolls the whole row back to the stored chain.
    void persist(
        rows.value.map((entry, current) =>
            current === index ? { ...entry, provider, model: '' } : entry
        )
    )
}

function updateModel(rowId: string, model: string): void {
    const index = rowIndexById(rowId)
    if (index < 0) {
        // The row was removed while the edit was in flight; dropping the value beats writing it into
        // whichever row took its place.
        return
    }

    void persist(
        rows.value.map((entry, current) => (current === index ? { ...entry, model } : entry))
    )
}

function clearModel(rowId: string): void {
    updateModel(rowId, '')
}

function addRow(): void {
    const provider = newRowProvider.value
    if (!provider) {
        return
    }

    // With every provider already in the chain the new row repeats a candidate on purpose: the inline
    // error then asks for a different model, which is the only way to compose `custom (model-b)`.
    void persist([...rows.value, createChainRow(provider.value)])
}

function removeRow(rowId: string): void {
    if (rows.value.length <= 1) {
        return
    }

    const index = rowIndexById(rowId)
    const next = rows.value.filter((row) => row.id !== rowId)
    if (index < 0 || next.length === rows.value.length) {
        return
    }

    if (configuringRowId.value === rowId) {
        configuringRowId.value = next[Math.min(index, next.length - 1)].id
    }

    void persist(next)
}

function moveRow(rowId: string, delta: number): void {
    const index = rowIndexById(rowId)
    const target = index + delta
    if (index < 0 || target < 0 || target >= rows.value.length) {
        return
    }

    const next = [...rows.value]
    next[index] = rows.value[target]
    next[target] = rows.value[index]
    void persist(next)
}

function openRequestSettings(): void {
    // RequestTemplatePage compares the route parameter against lower cased chain providers, so a
    // stored value such as `LocalAI` has to be normalised or the page silently opens another tab.
    const provider = configuringRow.value?.provider.trim().toLowerCase()
    if (!provider) {
        return
    }

    void router.push({ name: 'request-template-settings', params: { service: provider } })
}

async function loadManifest(provider: string): Promise<void> {
    if (!provider) {
        configuringManifest.value = null
        manifestError.value = null
        return
    }

    const requested = provider.trim().toLowerCase()
    try {
        const manifest = await servicesApi.plugin.getManifest(provider)
        if (configuredProvider() !== requested) {
            return
        }

        await settingsStore.setPluginSettings(manifest.settings)
        // setPluginSettings awaits as well: re-check before publishing the form, otherwise the title
        // and the rendered fields can describe two different providers.
        if (configuredProvider() !== requested) {
            return
        }

        configuringManifest.value = manifest
        manifestError.value = null
    } catch (error) {
        console.error('Failed to load manifest', error)
        if (configuredProvider() !== requested) {
            return
        }

        configuringManifest.value = null
        manifestError.value = `No manifest available for ${provider}.`
    }
}

watch(
    () => settingsStore.getSetting(SETTINGS.SERVICE_TYPE),
    (raw) => {
        if (saveInFlight) {
            // Our own optimistic write; a failed request restores the store value explicitly.
            return
        }

        if (raw === undefined) {
            // Settings have not loaded yet. Do not treat that as an empty chain or render a placeholder.
            return
        }

        savedChain.value = typeof raw === 'string' ? raw : ''
        adoptStoredChain(raw)
        saveError.value = null
    },
    { immediate: true }
)

watch(
    () => configuringRow.value?.provider,
    (provider) => {
        void loadManifest(provider ?? '')
    },
    { immediate: true }
)

onMounted(async () => {
    try {
        const summaries = await servicesApi.plugin.list()
        pluginSummaries.value = new Map(
            summaries.map((summary) => [summary.provider.trim().toLowerCase(), summary])
        )
        serviceOptions.value = summaries
            .map((summary) => ({ value: summary.provider, label: summary.displayName }))
            .sort((a, b) => a.label.localeCompare(b.label))
    } catch (error) {
        console.error('Failed to load translation provider list', error)
    } finally {
        providersLoaded.value = true
    }
})
</script>
