<template>
    <div v-if="entries.length" class="mt-4">
        <h3 class="mb-4 text-sm font-medium text-primary-content">Lines by Source</h3>
        <div class="grid grid-cols-2 gap-2 xl:grid-cols-3">
            <div v-for="[source, count] in entries" :key="source" class="rounded-sm bg-primary p-2">
                <h4 class="text-primary-content/70 truncate text-xs font-medium" :title="source">
                    {{ formatSource(source) }}
                </h4>
                <p class="text-lg font-bold text-primary-content">{{ formatNumber(count) }}</p>
            </div>
        </div>
    </div>
</template>

<script setup lang="ts">
import { computed } from 'vue'

const props = defineProps<{
    /** Line counts per source, keyed by `provider (model)`; absent while the statistics load. */
    counts?: Record<string, number>
}>()

const entries = computed(() => {
    if (!props.counts) {
        return []
    }

    return Object.entries(props.counts)
})

const formatSource = (source: string): string => {
    return source.charAt(0).toUpperCase() + source.slice(1)
}

const formatNumber = (num: number): string => {
    return num ? new Intl.NumberFormat().format(num) : '0'
}
</script>
