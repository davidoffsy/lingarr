<template>
    <CardComponent title="Webhook">
        <template #description>
            Configure webhooks in Radarr and Sonarr to automatically trigger translations when new
            media is added. The URL includes a Lingarr-generated secret — do not use Lingarr's API
            key or the Radarr/Sonarr API keys. Radarr and Sonarr share one secret; regenerating it
            invalidates both URLs.
        </template>
        <template #content>
            <div class="flex flex-col space-y-2">
                <p v-if="loadError" class="text-sm text-red-500">
                    {{ loadError }}
                </p>
                <span v-else-if="loading">Loading…</span>
                <template v-else-if="!secret">
                    <p class="text-secondary-content/80 text-sm">
                        No webhook secret yet. Existing Radarr/Sonarr connections without a token
                        keep working until you generate one.
                    </p>
                    <ButtonComponent variant="primary" :loading="regenerating" @click="generateSecret">
                        Generate secret
                    </ButtonComponent>
                </template>
                <template v-else>
                    <span>
                        Go to
                        <b>Settings → Connect → Connections → +</b>
                        and paste this URL:
                    </span>
                    <span class="font-semibold">Radarr</span>
                    <code class="block break-all rounded bg-black/20 p-3 font-mono text-sm">
                        {{ radarrWebhookUrl }}
                    </code>
                    <span class="font-semibold">Sonarr</span>
                    <code class="block break-all rounded bg-black/20 p-3 font-mono text-sm">
                        {{ sonarrWebhookUrl }}
                    </code>
                    <div class="flex flex-wrap gap-2">
                        <ButtonComponent variant="secondary" @click="copyUrl(radarrWebhookUrl)">
                            {{ copied === 'radarr' ? 'Copied' : 'Copy Radarr' }}
                        </ButtonComponent>
                        <ButtonComponent variant="secondary" @click="copyUrl(sonarrWebhookUrl)">
                            {{ copied === 'sonarr' ? 'Copied' : 'Copy Sonarr' }}
                        </ButtonComponent>
                        <ButtonComponent variant="ghost" :loading="regenerating" @click="regenerateSecret">
                            Regenerate secret
                        </ButtonComponent>
                    </div>
                </template>
            </div>
        </template>
    </CardComponent>
</template>

<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import CardComponent from '@/components/common/CardComponent.vue'
import ButtonComponent from '@/components/common/ButtonComponent.vue'
import services from '@/services'
import { resolveUrl } from '@/utils/baseUrl'

const secret = ref('')
const loading = ref(true)
const regenerating = ref(false)
const loadError = ref('')
const copied = ref<'radarr' | 'sonarr' | ''>('')

const radarrWebhookUrl = computed(() => webhookUrl('/api/webhook/radarr'))
const sonarrWebhookUrl = computed(() => webhookUrl('/api/webhook/sonarr'))

const webhookUrl = (path: string) => {
    if (!secret.value) {
        return ''
    }
    return `${resolveUrl(path)}?token=${secret.value}`
}

const loadSecret = async () => {
    loading.value = true
    loadError.value = ''
    try {
        const response = await services.webhook.getArrSecret()
        secret.value = response.secret ?? ''
    } catch {
        loadError.value = 'Failed to load webhook secret'
    } finally {
        loading.value = false
    }
}

const generateSecret = async () => {
    regenerating.value = true
    loadError.value = ''
    try {
        const response = await services.webhook.regenerateArrSecret()
        secret.value = response.secret
    } catch {
        loadError.value = 'Failed to generate webhook secret'
    } finally {
        regenerating.value = false
    }
}

const copyUrl = async (url: string) => {
    if (!url) {
        return
    }
    await navigator.clipboard.writeText(url)
    copied.value = url.includes('/radarr') ? 'radarr' : 'sonarr'
    window.setTimeout(() => {
        copied.value = ''
    }, 2000)
}

const regenerateSecret = async () => {
    if (
        !window.confirm(
            'Regenerating this secret invalidates existing Radarr and Sonarr webhook URLs. Continue?'
        )
    ) {
        return
    }

    await generateSecret()
}

onMounted(async () => {
    await loadSecret()
})
</script>
