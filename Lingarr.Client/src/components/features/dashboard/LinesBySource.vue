<template>
    <div v-if="entries.length" class="mt-4">
        <h3 class="text-primary-content mb-4 text-sm font-medium">Lines by Source</h3>
        <div class="grid grid-cols-2 gap-2 xl:grid-cols-3">
            <div v-for="[source, count] in entries" :key="source" class="bg-primary rounded-sm p-2">
                <h4 class="text-primary-content/70 truncate text-xs font-medium" :title="source">
                    {{ formatSource(source) }}
                </h4>
                <p class="text-primary-content text-lg font-bold">{{ formatNumber(count) }}</p>
            </div>
        </div>
    </div>
</template>

<script setup lang="ts">
import { computed } from 'vue'
import { splitSourceDisplayName } from '@/ts'

const props = defineProps<{
    /** Line counts per source, keyed by backend `TranslationSource.DisplayName` (`provider:model`). */
    counts?: Record<string, number>
}>()

const entries = computed(() => {
    if (!props.counts) {
        return []
    }

    return Object.entries(props.counts)
})

const formatSource = (source: string): string => {
    const { provider, model } = splitSourceDisplayName(source)
    const label = provider ? provider.charAt(0).toUpperCase() + provider.slice(1) : provider
    return model ? `${label} (${model})` : label
}

const formatNumber = (num: number): string => {
    return num ? new Intl.NumberFormat().format(num) : '0'
}
</script>
