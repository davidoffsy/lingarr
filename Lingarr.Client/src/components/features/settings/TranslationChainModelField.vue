<template>
    <div class="flex flex-col gap-1">
        <SelectComponent
            v-if="!manualOnly"
            ref="remoteSelect"
            :options="options"
            :selected="modelValue"
            :load-on-open="true"
            size="sm"
            placeholder="Default model"
            :no-options="statusMessage ?? 'No models available.'"
            @update:selected="selectModel"
            @fetch-options="loadOptions" />
        <InputComponent
            v-if="showManualInput"
            :model-value="draft"
            :debounce="0"
            size="sm"
            placeholder="Or type a model id"
            @update:model-value="(value: string) => (draft = value)"
            @change="commitDraft"
            @blur="commitDraft" />
        <p v-if="statusMessage" class="text-xs text-red-500">{{ statusMessage }}</p>
    </div>
</template>

<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import {
    IPluginManifest,
    IPluginSettingField,
    LabelValue,
    PLUGIN_SETTING_TYPE,
    SelectComponentExpose
} from '@/ts'
import services from '@/services'
import { getCachedPluginManifest } from '@/services/pluginManifestCache'
import InputComponent from '@/components/common/InputComponent.vue'
import SelectComponent from '@/components/common/SelectComponent.vue'

const props = defineProps<{
    provider: string
    modelValue: string
}>()

const emit = defineEmits<{
    (e: 'update:modelValue', value: string): void
}>()

const remoteSelect = ref<SelectComponentExpose | null>(null)
const manifest = ref<IPluginManifest | null>(null)
const options = ref<LabelValue[]>([])
const statusMessage = ref<string | null>(null)
/**
 * Typed model id. Kept local until the input is committed: the owner stores the value, validates the
 * chain and may roll it back, so a keystroke must not reach the store on its own.
 */
const draft = ref(props.modelValue)
/** Value already handed to the owner; `change` and `blur` both fire for one edit. */
const committed = ref(props.modelValue)

const modelField = computed<IPluginSettingField | null>(
    () =>
        manifest.value?.settings.find(
            (field) => field.type === PLUGIN_SETTING_TYPE.REMOTE_DROPDOWN
        ) ?? null
)

/** A provider that declares model overrides without a model catalogue only accepts typed ids. */
const manualOnly = computed(() => manifest.value !== null && modelField.value === null)

const showManualInput = computed(
    () => manualOnly.value || statusMessage.value !== null || modelField.value?.allowCustom === true
)

watch(
    () => [props.modelValue, props.provider],
    () => {
        // The owner is the source of truth: a stored, rejected or provider-cleared value replaces a
        // draft that was never stored, while a draft the owner accepted reads back as itself.
        committed.value = props.modelValue
        draft.value = props.modelValue
    }
)

function commitDraft(): void {
    const next = draft.value.trim()
    if (next === committed.value) {
        return
    }

    committed.value = next
    emit('update:modelValue', next)
}

function selectModel(value: string): void {
    if (value === props.modelValue) {
        // SelectComponent emits for the already selected option too, and re-selecting the stored model
        // must not start a save round-trip.
        return
    }

    emit('update:modelValue', value)
}

watch(
    () => props.provider,
    () => {
        void initialize()
    },
    { immediate: true }
)

function defaultOption(): LabelValue {
    return { value: '', label: 'Default model' }
}

async function initialize(): Promise<void> {
    const provider = props.provider
    manifest.value = null
    options.value = [defaultOption()]
    statusMessage.value = null

    const loaded = await getCachedPluginManifest(provider)
    if (provider !== props.provider) {
        return
    }

    manifest.value = loaded
    if (loaded === null) {
        statusMessage.value = 'Model list unavailable. Type the model id instead.'
    }
}

async function loadOptions(): Promise<void> {
    const provider = props.provider

    try {
        const loaded = manifest.value ?? (await getCachedPluginManifest(provider))
        if (provider !== props.provider) {
            return
        }

        if (loaded === null) {
            statusMessage.value = 'Model list unavailable. Type the model id instead.'
            return
        }

        manifest.value = loaded
        const field = loaded.settings.find(
            (item) => item.type === PLUGIN_SETTING_TYPE.REMOTE_DROPDOWN
        )
        if (!field?.optionsEndpoint) {
            statusMessage.value = field
                ? 'This provider has no model list. Type the model id instead.'
                : 'Model list unavailable. Type the model id instead.'
            return
        }

        const response = await services.plugin.getOptions(field.optionsEndpoint)
        if (provider !== props.provider) {
            return
        }

        const remoteOptions = response.options ?? []
        options.value = [defaultOption(), ...remoteOptions]
        statusMessage.value =
            remoteOptions.length === 0
                ? (response.message ?? 'No models were returned. Type the model id instead.')
                : null
    } catch (error) {
        console.error(`Failed to load model options for ${provider}`, error)
        statusMessage.value = 'Error loading models. Type the model id instead.'
    } finally {
        remoteSelect.value?.setLoadingState(false)
    }
}
</script>
