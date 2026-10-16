<template>
    <CardComponent title="Translation Request">
        <template #description>
            Modify translation request settings by changing retry options or batch size if available in the service.
        </template>
        <template #content>
            <SaveNotification ref="saveNotification" />

            <template v-if="isAiService">
                <div class="flex flex-col space-x-2">
                    <span class="font-semibold">Use batch translation</span>
                    Process multiple subtitle lines together in batches to improve translation
                    efficiency and context awareness. Note that single-line translations with context
                    are still more reliable and of higher quality.
                </div>
                <ToggleButton v-model="useBatchTranslation">
                    <span class="text-sm font-medium text-primary-content">
                        {{ useBatchTranslation == 'true' ? 'Enabled' : 'Disabled' }}
                    </span>
                </ToggleButton>
            </template>

            <template v-if="useBatchTranslation == 'true'">
                <div class="flex flex-col space-x-2">
                    <span class="font-semibold">Batch size:</span>
                    Number of subtitle lines per request. 50–100 is a good balance of speed and context.
                </div>
                <InputComponent
                    v-model="maxBatchSize"
                    :validation-type="INPUT_VALIDATION_TYPE.NUMBER"
                    @update:validation="(val) => (isValid.maxBatchSize = val)" />
            </template>

            <template v-if="isAiService">
                <div class="flex flex-col space-x-2">
                    <span class="font-semibold">Max tokens</span>
                    Maximum tokens for one completion (thinking plus the answer). Default 0 omits
                    the field so each provider keeps its own default. Set a positive value to cap
                    the response. Anthropic's request template defaults to 8192 when the field is
                    omitted.
                </div>
                <InputComponent
                    v-model="aiMaxTokens"
                    :validation-type="INPUT_VALIDATION_TYPE.INTEGER"
                    @update:validation="(val) => (isValid.aiMaxTokens = val)" />
            </template>

            <template v-if="usesSharedChatThinking">
                <div class="flex flex-col space-x-2">
                    <span class="font-semibold">Thinking</span>
                    Controls whether the model spends tokens on chain-of-thought. Auto (default)
                    sends no thinking field — the compatible choice for Ollama and most OpenCode
                    Go models. Off writes thinking.disabled (needed for OpenCode Go / MiMo). On
                    writes thinking.enabled. Only applied to the providers that translate through the
                    shared OpenAI-compatible chat (LocalAI and Custom).
                </div>
                <SelectComponent
                    :selected="aiThinking"
                    :options="thinkingOptions"
                    @update:selected="aiThinking = $event" />
            </template>

            <div class="flex flex-col space-x-2">
                <span class="font-semibold">Request timeout:</span>
                Maximum time in minutes to wait for a single request to the translation service.
                Increase this if you run a slow local AI on minimal hardware for example.
            </div>
            <InputComponent
                v-model="requestTimeout"
                :validation-type="INPUT_VALIDATION_TYPE.NUMBER"
                @update:validation="(val) => (isValid.requestTimeout = val)" />

            <div class="flex flex-col space-x-2">
                <span class="font-semibold">Max translation retries:</span>
                Maximum number of retries per line or batch.
            </div>
            <InputComponent
                v-model="maxRetries"
                :validation-type="INPUT_VALIDATION_TYPE.NUMBER"
                @update:validation="(val) => (isValid.maxRetries = val)" />

            <div class="flex flex-col space-x-2">
                <span class="font-semibold">Delay between retries:</span>
                Initial delay before retrying, in seconds.
            </div>
            <InputComponent
                v-model="retryDelay"
                :validation-type="INPUT_VALIDATION_TYPE.NUMBER"
                @update:validation="(val) => (isValid.retryDelay = val)" />

            <div class="flex flex-col space-x-2">
                <span class="font-semibold">Retry delay multiplier:</span>
                Factor by which the delay increases after each retry.
            </div>
            <InputComponent
                v-model="retryDelayMultiplier"
                :validation-type="INPUT_VALIDATION_TYPE.NUMBER"
                @update:validation="(val) => (isValid.retryDelayMultiplier = val)" />
        </template>
    </CardComponent>
</template>

<script setup lang="ts">
import { computed, ref, reactive } from 'vue'
import { useSettingStore } from '@/store/setting'
import { AI_THINKING_MODES, chainProviders, INPUT_VALIDATION_TYPE, SERVICE_TYPE, SETTINGS } from '@/ts'
import CardComponent from '@/components/common/CardComponent.vue'
import SaveNotification from '@/components/common/SaveNotification.vue'
import InputComponent from '@/components/common/InputComponent.vue'
import SelectComponent from '@/components/common/SelectComponent.vue'
import ToggleButton from '@/components/common/ToggleButton.vue'

const saveNotification = ref<InstanceType<typeof SaveNotification> | null>(null)
const settingsStore = useSettingStore()
const isValid = reactive({
    maxBatchSize: true,
    aiMaxTokens: true,
    requestTimeout: true,
    maxRetries: true,
    retryDelay: true,
    retryDelayMultiplier: true
})

const thinkingOptions = [
    { value: AI_THINKING_MODES.AUTO, label: 'Auto' },
    { value: AI_THINKING_MODES.OFF, label: 'Off' },
    { value: AI_THINKING_MODES.ON, label: 'On' }
]

const serviceType = computed(
    () => chainProviders(settingsStore.getSetting(SETTINGS.SERVICE_TYPE))[0] ?? ''
)

const isAiService = computed(() =>
    [
        SERVICE_TYPE.ANTHROPIC,
        SERVICE_TYPE.DEEPSEEK,
        SERVICE_TYPE.GEMINI,
        SERVICE_TYPE.LOCALAI,
        SERVICE_TYPE.CUSTOM,
        SERVICE_TYPE.MISTRAL,
        SERVICE_TYPE.OPENAI,
        SERVICE_TYPE.XAI
    ].includes(
        serviceType.value as
            | 'openai'
            | 'anthropic'
            | 'localai'
            | 'custom'
            | 'gemini'
            | 'deepseek'
            | 'mistral'
            | 'xai'
    )
)

/**
 * Providers that read the shared `ai_thinking` setting. `LocalAiService` and `CustomService` both
 * translate through the shared OpenAI-compatible chat, so the setting applies to both of them.
 */
const usesSharedChatThinking = computed(
    () => serviceType.value === SERVICE_TYPE.LOCALAI || serviceType.value === SERVICE_TYPE.CUSTOM
)

const useBatchTranslation = computed({
    get: (): string => settingsStore.getSetting(SETTINGS.USE_BATCH_TRANSLATION) as string,
    set: (newValue: string): void => {
        settingsStore.updateSetting(SETTINGS.USE_BATCH_TRANSLATION, newValue, true)
        saveNotification.value?.show()
    }
})

const maxBatchSize = computed({
    get: (): string => settingsStore.getSetting(SETTINGS.MAX_BATCH_SIZE) as string,
    set: (newValue: string): void => {
        settingsStore.updateSetting(SETTINGS.MAX_BATCH_SIZE, newValue, isValid.maxBatchSize)
        saveNotification.value?.show()
    }
})

const aiMaxTokens = computed({
    get: (): string => settingsStore.getSetting(SETTINGS.AI_MAX_TOKENS) as string,
    set: (newValue: string): void => {
        settingsStore.updateSetting(SETTINGS.AI_MAX_TOKENS, newValue, isValid.aiMaxTokens)
        saveNotification.value?.show()
    }
})

const aiThinking = computed({
    get: (): string => settingsStore.getSetting(SETTINGS.AI_THINKING) as string,
    set: (newValue: string): void => {
        settingsStore.updateSetting(SETTINGS.AI_THINKING, newValue, true)
        saveNotification.value?.show()
    }
})

const requestTimeout = computed({
    get: (): string => settingsStore.getSetting(SETTINGS.REQUEST_TIMEOUT) as string,
    set: (newValue: string): void => {
        settingsStore.updateSetting(SETTINGS.REQUEST_TIMEOUT, newValue, isValid.requestTimeout)
        saveNotification.value?.show()
    }
})

const maxRetries = computed({
    get: (): string => settingsStore.getSetting(SETTINGS.MAX_RETRIES) as string,
    set: (newValue: string): void => {
        settingsStore.updateSetting(SETTINGS.MAX_RETRIES, newValue, isValid.maxRetries)
        saveNotification.value?.show()
    }
})

const retryDelay = computed({
    get: (): string => settingsStore.getSetting(SETTINGS.RETRY_DELAY) as string,
    set: (newValue: string): void => {
        settingsStore.updateSetting(SETTINGS.RETRY_DELAY, newValue, isValid.retryDelay)
        saveNotification.value?.show()
    }
})

const retryDelayMultiplier = computed({
    get: (): string => settingsStore.getSetting(SETTINGS.RETRY_DELAY_MULTIPLIER) as string,
    set: (newValue: string): void => {
        settingsStore.updateSetting(
            SETTINGS.RETRY_DELAY_MULTIPLIER,
            newValue,
            isValid.retryDelayMultiplier
        )
        saveNotification.value?.show()
    }
})
</script>
